using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnmptoolRAV;

sealed class Agent : IDisposable {
    readonly Socket socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
    readonly ManualResetEventSlim ready=new ManualResetEventSlim(false);
    readonly Task loop; volatile bool stopping;
    public readonly int Port; public IPAddress LastSource; public readonly List<int> Versions=new List<int>();
    public Agent(Func<SnmpMessage,byte[]> handler) {
        socket.Bind(new IPEndPoint(IPAddress.Loopback,0));Port=((IPEndPoint)socket.LocalEndPoint).Port;
        loop=Task.Factory.StartNew(()=>{var buffer=new byte[65535];ready.Set();while(!stopping){try{EndPoint endpoint=new IPEndPoint(IPAddress.Any,0);int count=socket.ReceiveFrom(buffer,ref endpoint);LastSource=((IPEndPoint)endpoint).Address;var request=SnmpMessage.Decode(buffer.Take(count).ToArray());lock(Versions)Versions.Add(request.Version);byte[] reply=handler(request);if(reply!=null)socket.SendTo(reply,endpoint);}catch(SocketException){if(!stopping)throw;}catch(ObjectDisposedException){break;}}},TaskCreationOptions.LongRunning);
        if(!ready.Wait(5000))throw new Exception("Mock agent did not become ready.");
    }
    public void Dispose(){stopping=true;socket.Dispose();if(!loop.Wait(3000))throw new Exception("Mock agent did not stop.");ready.Dispose();}
}
static class Tests {
    static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
    static void Reject(Action action){try{action();throw new Exception("Expected rejection");}catch(ArgumentException){}catch(FormatException){}}
    static byte[] Hex(string value){return Enumerable.Range(0,value.Length/2).Select(i=>Convert.ToByte(value.Substring(i*2,2),16)).ToArray();}
    static byte[] Reply(SnmpMessage request,string oid,Ber value,int requestOffset=0,string community=null){return SnmpMessage.Encode(request.Version,community??Encoding.UTF8.GetString(request.Community),request.RequestId+requestOffset,0xa2,new[]{new SnmpValue{Oid=oid,Value=value}},0);}
    static Ber Text(string value){return new Ber(4,Encoding.UTF8.GetBytes(value));}
    static Adapter Loopback(){return new Adapter{Name="Loopback test",Address=IPAddress.Loopback,Mask=IPAddress.Parse("255.255.255.255"),Index=0,Up=true};}
    static void CodecTests(){
        var get=SnmpMessage.Encode(1,"public",1,0xa0,new[]{new SnmpValue{Oid="1.3.6.1.2.1.1.1.0",Value=new Ber(5,new byte[0])}},0);
        Check(get.SequenceEqual(Hex("302602010104067075626c6963a019020101020100020100300e300c06082b060102010101000500")),"GET matches independent BER wire fixture");
        Reject(()=>SnmpMessage.Encode(1,"public",1,0xa3,new SnmpValue[0],0));
        var response=SnmpMessage.Decode(Hex("302a02010104067075626c6963a21d0201010201000201003012301006082b06010201010100040474657374"));
        Check(response.Pdu==0xa2&&response.Values[0].Value.Display()=="test","Decode independent response fixture");
        string longText=new String('x',600)+" / zażółć";
        var longReply=SnmpMessage.Encode(1,"public",1,0xa2,new[]{new SnmpValue{Oid="1.3.6.1.2.1.1.1.0",Value=Text(longText)}},0);
        Check(SnmpMessage.Decode(longReply).Values[0].Value.Display()==longText,"Long-form BER lengths and UTF-8 strings");
        foreach(string oid in new[]{"0.0","1.39.4294967295","2.999.128.16384","2.4294967295.4294967295"}){int offset=0;Check(Ber.Read(Ber.Oid(oid),ref offset).OidText()==oid,"OID base-128 roundtrip");}
        foreach(int value in new[]{-2147483648,-129,-128,-1,0,127,128,2147483647}){int p=0;Check(Ber.Read(Ber.Integer(value),ref p).Signed()==value,"Signed integer roundtrip");}
        Check(new Ber(0x46,new byte[]{0,255,255,255,255,255,255,255,255}).Display()==UInt64.MaxValue.ToString(),"Counter64 does not overflow signed integers");
        Reject(()=>new Ber(0x43,new byte[]{1,0,0,0,0}).Display());
        foreach(string oid in new[]{"3.1","1.40","1","1.3.-1","1.3.4294967296","1.3.x"})Reject(()=>Ber.Oid(oid));
        for(int i=0;i<get.Length;i++){int size=i;Reject(()=>SnmpMessage.Decode(get.Take(size).ToArray()));}
        Reject(()=>SnmpMessage.Decode(new byte[]{0x30,0x80,0,0}));
        var random=new Random(3416);for(int i=0;i<2500;i++){var bytes=new byte[random.Next(256)];random.NextBytes(bytes);try{var packet=SnmpMessage.Decode(bytes);foreach(var binding in packet.Values)binding.Value.Display();}catch(FormatException){}catch(OverflowException){}}
        Check(Walker.CompareOid("1.3.6.1.2.1.9","1.3.6.1.2.1.10")<0,"Numeric OID order");
        Check(Mib.Name("1.3.6.1.2.1.43.11.1.1.9.1.1").StartsWith("Supply level"),"Printer-MIB field labels");
        Check(Csv.Cell("=1+1")=="\"'=1+1\""&&Csv.Cell("\t@SUM(1)").Contains("'"),"CSV formula neutralization");
        Check(Csv.Cell("a,\"b\"")=="\"a,\"\"b\"\"\"","CSV quoting");
    }
    static void RangeTests(){
        var adapter=new Adapter{Address=IPAddress.Parse("192.168.10.9"),Mask=IPAddress.Parse("255.255.0.0")};
        var range=IpRange.Parse("192.168.10.9/24",adapter);Check(range.ToString()=="192.168.10.0/24"&&range.Count==254&&IpRange.Address(range.FirstHost).ToString()=="192.168.10.1","Subnet boundary calculation");
        Check(IpRange.Parse("192.168.10.0/31",adapter).Count==2&&IpRange.Parse("192.168.10.9/32",adapter).Count==1,"Point-to-point and single-host ranges");
        Check(IpRange.Parse("192.168.0.0/16",adapter).Count==65534,"Maximum scan bound");
        Reject(()=>IpRange.Parse("192.169.1.0/24",adapter));Reject(()=>IpRange.Parse("192.168.0.0/15",adapter));Reject(()=>IpRange.Parse("192.168.10/24",adapter));
        var vpn=new Adapter{Address=IPAddress.Parse("10.8.0.2"),Mask=IPAddress.Parse("255.255.255.255")};
        Reject(()=>IpRange.Parse("192.168.1.0/24",vpn));
        Check(IpRange.Parse("192.168.1.0/24",vpn,true).Count==254,"Explicit routed-range selection supports /32 VPN adapters");
        Check(IpRange.Prefix(IPAddress.Parse("255.255.255.128"))==25,"Mask prefix");Reject(()=>IpRange.Prefix(IPAddress.Parse("255.0.255.0")));
    }
    static void UdpTests(){
        using(var agent=new Agent(r=>Reply(r,r.Values[0].Oid,Text(r.Values[0].Oid.EndsWith(".5.0")?"Mock printer":"SNMP test device")))){
            var client=new SnmpClient(IPAddress.Loopback,0,agent.Port,300);
            foreach(int version in new[]{0,1})Check(client.Query(IPAddress.Loopback,version,"test-community","1.3.6.1.2.1.1.1.0",false,CancellationToken.None).Values[0].Value.Display()=="SNMP test device","Real UDP v1/v2c exchange");
            Check(client.Query(IPAddress.Loopback,1,"test-community"," .1.03.6.1.2.1.1.1.0 ",false,CancellationToken.None)!=null,"Normalize accepted OID syntax before matching response bindings");
            Check(agent.LastSource.Equals(IPAddress.Loopback),"UDP bound to chosen source IPv4");
            var loop=Adapter.List().FirstOrDefault(a=>a.Address.Equals(IPAddress.Loopback));
            if(loop!=null){var bound=new SnmpClient(loop.Address,loop.Index,agent.Port,300);Check(bound.Query(IPAddress.Loopback,1,"test-community","1.3.6.1.2.1.1.1.0",false,CancellationToken.None)!=null,"Windows IP_UNICAST_IF binding works with actual interface index");}
        }
        using(var agent=new Agent(r=>r.Version==1?null:Reply(r,r.Values[0].Oid,Text(r.Values[0].Oid.EndsWith(".5.0")?"Legacy printer":"SNMP v1 agent")))){
            var options=new ScanOptions{Adapter=Loopback(),Range=IpRange.Parse("127.0.0.1/32",Loopback()),Community="private-test",Port=agent.Port,Timeout=150,Workers=1};var found=new List<DeviceResult>();ScanProgress last=null;
            Scanner.Scan(options,d=>found.Add(d),p=>last=p,CancellationToken.None);
            Check(found.Count==1&&found[0].Version==0&&found[0].Name=="Legacy printer","Automatic v2c to v1 fallback");Check(last.Completed==1&&last.Found==1,"Scan progress and responder counts");Check(agent.Versions.Contains(1)&&agent.Versions.Contains(0),"Auto actually tries both versions");
        }
        foreach(bool wrongId in new[]{true,false})using(var agent=new Agent(r=>Reply(r,r.Values[0].Oid,Text("spoof"),wrongId?1:0,wrongId?null:"wrong-community"))){Check(new SnmpClient(IPAddress.Loopback,0,agent.Port,150).Query(IPAddress.Loopback,1,"expected","1.3.6.1.2.1.1.1.0",false,CancellationToken.None)==null,"Reject mismatched request ID/community");}
        using(var agent=new Agent(r=>null))using(var cancel=new CancellationTokenSource()){
            var watch=Stopwatch.StartNew();cancel.CancelAfter(100);try{new SnmpClient(IPAddress.Loopback,0,agent.Port,5000).Query(IPAddress.Loopback,1,"test","1.3.6.1.2.1.1.1.0",false,cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}Check(watch.ElapsedMilliseconds<1500,"Cancellation interrupts a long receive timeout");
        }
        using(var agent=new Agent(r=>new byte[]{0x30,0x80})){Check(new SnmpClient(IPAddress.Loopback,0,agent.Port,150).Query(IPAddress.Loopback,1,"test","1.3.6.1.2.1.1.1.0",false,CancellationToken.None)==null,"Malformed replies do not crash receiver");}
        using(var cancel=new CancellationTokenSource())using(var agent=new Agent(r=>{if(r.Values[0].Oid.EndsWith(".5.0")){cancel.Cancel();return null;}return Reply(r,r.Values[0].Oid,Text("Confirmed responder"));})){
            var found=new List<DeviceResult>();
            try{Scanner.Scan(new ScanOptions{Adapter=Loopback(),Range=IpRange.Parse("127.0.0.1/32",Loopback()),Community="test",Version=1,Port=agent.Port,Timeout=300,Workers=1},d=>found.Add(d),p=>{},cancel.Token);throw new Exception("Scan did not report cancellation");}catch(OperationCanceledException){}
            Check(found.Count==1&&found[0].Description=="Confirmed responder"&&found[0].Status.Contains("stopped"),"Stopping optional name lookup preserves the confirmed SNMP responder");
        }
    }
    static void WalkTests(){
        string[] oids={"1.3.6.1.2.1.1.1.0","1.3.6.1.2.1.1.5.0","1.3.6.1.2.1.2.1.0"};
        using(var agent=new Agent(r=>{var next=oids.FirstOrDefault(o=>Walker.CompareOid(o,r.Values[0].Oid)>0);return Reply(r,next??r.Values[0].Oid,next==null?new Ber(0x82,new byte[0]):Text("Value for "+next));})){
            var rows=new List<DataRow>();var result=Walker.Read(new SnmpClient(IPAddress.Loopback,0,agent.Port,300),IPAddress.Loopback,1,"read-test","1.3.6.1.2.1.1",20,row=>rows.Add(row),CancellationToken.None);
            Check(rows.Count==2&&result.Status.StartsWith("Read complete"),"Walk stops at exact subtree boundary");
            var capped=Walker.Read(new SnmpClient(IPAddress.Loopback,0,agent.Port,300),IPAddress.Loopback,0,"read-test","1.3.6.1.2.1.1",1,row=>{},CancellationToken.None);Check(capped.Rows.Count==1&&capped.Status.StartsWith("Stopped at 1"),"Walk row cap and v1 support");
        }
        using(var agent=new Agent(r=>Reply(r,r.Values[0].Oid,Text("repeated")))){var result=Walker.Read(new SnmpClient(IPAddress.Loopback,0,agent.Port,300),IPAddress.Loopback,1,"test","1.3.6.1.2.1.1",100,row=>{},CancellationToken.None);Check(result.Rows.Count==0&&result.Status.Contains("out-of-order"),"Stop non-progressing agents");}
        foreach(byte tag in new byte[]{0x80,0x81,0x82})using(var agent=new Agent(r=>Reply(r,r.Values[0].Oid,new Ber(tag,new byte[0])))){
            var result=Walker.Read(new SnmpClient(IPAddress.Loopback,0,agent.Port,300),IPAddress.Loopback,1,"test","1.3.6.1.2.1.1",100,row=>{},CancellationToken.None);
            Check(result.Rows.Count==0&&(tag==0x82?result.Status.StartsWith("Read complete"):result.Status.Contains("partial")),"Only endOfMibView completes a walk; missing objects/instances are reported as partial");
        }
    }
    static int Main(){Embedded.Initialize();return Run();}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(){try{CodecTests();RangeTests();UdpTests();WalkTests();V3Tests.Run();Console.WriteLine("All SNMP codec, subnet, source-binding, UDP discovery, fallback, cancellation, walk and CSV tests passed.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
