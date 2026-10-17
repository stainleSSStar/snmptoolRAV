using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using SnmptoolRAV;

sealed class V3Agent : IDisposable {
    readonly Socket socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
    readonly Task loop;volatile bool stopping;readonly V3Profile profile;readonly IPrivacyProvider privacy;
    readonly OctetString engine=new OctetString(new byte[]{128,0,31,136,4,1,2,3,4,5});int time=1000;bool resynced;
    public readonly int Port;public string Mode="";public int Discoveries;public int LegacyRequests;
    public V3Agent(V3Profile profile){
        this.profile=profile;privacy=profile.Provider();socket.Bind(new IPEndPoint(IPAddress.Loopback,0));Port=((IPEndPoint)socket.LocalEndPoint).Port;
        loop=Task.Factory.StartNew(()=>{var registry=new UserRegistry();registry.Add(new OctetString(Encoding.UTF8.GetBytes(profile.UserName)),privacy);var buffer=new byte[65535];
            while(!stopping){try{EndPoint peer=new IPEndPoint(IPAddress.Any,0);int count=socket.ReceiveFrom(buffer,ref peer);var requests=MessageFactory.ParseMessages(buffer,0,count,registry);foreach(var request in requests){if(request.Version!=VersionCode.V3){LegacyRequests++;continue;}if(request is MalformedMessage||request.Parameters.IsInvalid)continue;byte[] response=Respond(request);if(response!=null)socket.SendTo(response,peer);}}catch(SocketException){if(!stopping)throw;}catch(ObjectDisposedException){break;}}
        },TaskCreationOptions.LongRunning);
    }
    SecurityParameters Parameters(OctetString user,IPrivacyProvider provider,int engineTime){return new SecurityParameters(engine,new Integer32(1),new Integer32(engineTime),user,provider.AuthenticationProvider.CleanDigest,provider.Salt);}
    byte[] Report(ISnmpMessage request,string oid,bool authenticated){var provider=authenticated?new DefaultPrivacyProvider(privacy.AuthenticationProvider):DefaultPrivacyProvider.DefaultPair;return new ReportMessage(VersionCode.V3,new Header(new Integer32(request.Header.MessageId),new Integer32(65507),authenticated?Levels.Authentication:0),Parameters(authenticated?request.Parameters.UserName:OctetString.Empty,provider,time),new Scope(engine,OctetString.Empty,new ReportPdu(request.Scope.Pdu.RequestId.ToInt32(),ErrorCode.NoError,0,new List<Variable>{new Variable(new ObjectIdentifier(oid),new Counter32(1U))})),provider,null).ToBytes();}
    byte[] Respond(ISnmpMessage request){
        if(request.Parameters.EngineId==null||request.Parameters.EngineId.GetRaw().Length==0){Discoveries++;return Report(request,"1.3.6.1.6.3.15.1.1.4.0",false);}
        if(Mode=="silent")return null;
        if(Mode=="unknown-user")return Report(request,"1.3.6.1.6.3.15.1.1.3.0",false);
        if((Mode=="resync"&&!resynced)||Mode=="resync-loop"){resynced=true;time=4000;return Report(request,"1.3.6.1.6.3.15.1.1.2.0",profile.Level!=V3Level.NoAuthNoPriv);}
        var oid=request.Scope.Pdu.Variables[0].Id.ToString();
        if(request.TypeCode()==SnmpType.GetNextRequestPdu){string[] tree={"1.3.6.1.2.1.1.1.0","1.3.6.1.2.1.1.5.0","1.3.6.1.2.1.2.1.0"};oid=tree.First(o=>Walker.CompareOid(o,oid)>0);}
        var variables=new List<Variable>{new Variable(new ObjectIdentifier(oid),new OctetString(oid.EndsWith(".5.0")?"V3 printer":"Authenticated test data"))};
        var provider=Mode=="downgrade"?(IPrivacyProvider)new DefaultPrivacyProvider(privacy.AuthenticationProvider):privacy;
        Levels flags=Mode=="downgrade"?Levels.Authentication:profile.Flags;
        var scope=new Scope(engine,Mode=="wrong-context"?new OctetString("wrong"):request.Scope.ContextName,new ResponsePdu(request.Scope.Pdu.RequestId.ToInt32(),ErrorCode.NoError,0,variables));
        var result=new ResponseMessage(VersionCode.V3,new Header(new Integer32(request.Header.MessageId),new Integer32(65507),flags),Parameters(request.Parameters.UserName,provider,Mode=="wrong-time"?time+500:time),scope,provider,true,null).ToBytes();
        if(Mode=="tamper")result[result.Length-1]^=1;return result;
    }
    public void Dispose(){stopping=true;socket.Dispose();if(!loop.Wait(3000))throw new Exception("V3 test agent failed to stop.");}
}
static class V3Tests {
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static V3Profile Profile(V3Level level=V3Level.AuthPriv,V3Auth auth=V3Auth.Sha256){return new V3Profile{UserName="operator",Level=level,Authentication=auth,AuthPassword="test-authentication",PrivacyPassword="test-privacy"};}
    static void Rejected(Action action){try{action();throw new Exception("Invalid v3 input or response was accepted.");}catch(ArgumentException){}catch(SnmpSecurityException){}}
    public static void Run(){
        Check(typeof(MessageFactory).Assembly.Location=="","SNMP library is loaded from the EXE resource, without a sidecar DLL");
        Rejected(()=>new V3Profile().Validate());Rejected(()=>new V3Profile{UserName="u",Level=V3Level.AuthNoPriv,AuthPassword="short"}.Validate());
        var none=new V3Profile{UserName="u",Level=V3Level.NoAuthNoPriv};none.Validate();Check(none.Flags==0,"noAuth profile ignores unused secrets");
        foreach(var algorithm in new[]{V3Auth.Sha256,V3Auth.Sha384,V3Auth.Sha512,V3Auth.Sha1})using(var agent=new V3Agent(Profile(V3Level.AuthPriv,algorithm))){var client=new SnmpClient(IPAddress.Loopback,0,agent.Port,500,Profile(V3Level.AuthPriv,algorithm));var reply=client.Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.1.0",false,CancellationToken.None);Check(reply.Version==3&&reply.Values[0].Value.Display()=="Authenticated test data","Authenticated/encrypted v3 GET");client.Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.5.0",false,CancellationToken.None);Check(agent.Discoveries==1&&agent.LegacyRequests==0,"Engine cache and no legacy fallback");}
        foreach(var level in new[]{V3Level.NoAuthNoPriv,V3Level.AuthNoPriv})using(var agent=new V3Agent(Profile(level))){var reply=new SnmpClient(IPAddress.Loopback,0,agent.Port,500,Profile(level)).Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.1.0",false,CancellationToken.None);Check(reply!=null,"Explicit v3 security levels");}
        foreach(string mode in new[]{"tamper","downgrade","wrong-context","wrong-time","unknown-user","silent"})using(var agent=new V3Agent(Profile())){agent.Mode=mode;Rejected(()=>new SnmpClient(IPAddress.Loopback,0,agent.Port,150,Profile()).Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.1.0",false,CancellationToken.None));Check(agent.LegacyRequests==0,"Security failure does not fall back to v1/v2c");}
        using(var agent=new V3Agent(Profile())){agent.Mode="resync";var client=new SnmpClient(IPAddress.Loopback,0,agent.Port,500,Profile());Check(client.Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.1.0",false,CancellationToken.None)!=null&&agent.Discoveries==2,"One bounded engine-time rediscovery and retry");}
        using(var agent=new V3Agent(Profile())){agent.Mode="resync-loop";Rejected(()=>new SnmpClient(IPAddress.Loopback,0,agent.Port,500,Profile()).Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.1.0",false,CancellationToken.None));Check(agent.Discoveries==2,"Repeated time reports cannot cause unbounded rediscovery");}
        var contextual=Profile();contextual.Context="lab";using(var agent=new V3Agent(contextual)){Check(new SnmpClient(IPAddress.Loopback,0,agent.Port,500,contextual).Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.1.0",false,CancellationToken.None)!=null,"Non-default v3 context roundtrip");}
        using(var agent=new V3Agent(Profile())){var result=Walker.Read(new SnmpClient(IPAddress.Loopback,0,agent.Port,500,Profile()),IPAddress.Loopback,3,"","1.3.6.1.2.1.1",10,row=>{},CancellationToken.None);Check(result.Rows.Count==2&&result.Status.StartsWith("Read complete"),"SNMPv3 GETNEXT walk");}
        using(var agent=new V3Agent(Profile())){agent.Mode="unknown-user";var adapter=new Adapter{Name="Local test",Address=IPAddress.Loopback,Mask=IPAddress.Parse("255.255.255.255"),Up=true};var rows=new List<DeviceResult>();var summary=Scanner.Scan(new ScanOptions{Adapter=adapter,Range=IpRange.Parse("127.0.0.1/32",adapter),Version=3,V3=Profile(),Port=agent.Port,Timeout=200,Workers=1},row=>rows.Add(row),p=>{},CancellationToken.None);Check(rows.Count==1&&rows[0].Version==3&&rows[0].Status!="Responding"&&summary.Errors==1,"V3 discovery reports credential errors without pretending data was read");}
        using(var agent=new V3Agent(Profile()))using(var cancel=new CancellationTokenSource()){agent.Mode="silent";cancel.CancelAfter(100);try{new SnmpClient(IPAddress.Loopback,0,agent.Port,5000,Profile()).Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.1.0",false,cancel.Token);throw new Exception("V3 cancellation ignored.");}catch(OperationCanceledException){}}
        Console.WriteLine("All SNMPv3 profile, SHA/AES, engine discovery, authentication, downgrade rejection, resync, error reporting, walk and cancellation tests passed.");
    }
}
