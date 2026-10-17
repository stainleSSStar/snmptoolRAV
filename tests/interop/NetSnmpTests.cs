using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using SnmptoolRAV;

static class NetSnmpTests {
    static V3Profile Profile(string user,V3Level level,V3Auth auth=V3Auth.Sha256){return new V3Profile{UserName=user,Level=level,Authentication=auth,AuthPassword="test-authentication",PrivacyPassword="test-privacy"};}
    static SnmpClient Client(V3Profile profile){return new SnmpClient(IPAddress.Loopback,0,1161,1000,profile);}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Rejected(V3Profile profile){try{Client(profile).Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.5.0",false,CancellationToken.None);throw new Exception("Bad v3 credentials or context were accepted.");}catch(SnmpSecurityException){}}
    static int Main(){try{
        var profiles=new[]{Profile("noauth",V3Level.NoAuthNoPriv),Profile("authenticated",V3Level.AuthNoPriv),Profile("private",V3Level.AuthPriv),Profile("sha384",V3Level.AuthPriv,V3Auth.Sha384),Profile("sha512",V3Level.AuthPriv,V3Auth.Sha512),Profile("legacysha",V3Level.AuthPriv,V3Auth.Sha1)};
        foreach(var profile in profiles){var reply=Client(profile).Query(IPAddress.Loopback,3,"","1.3.6.1.2.1.1.5.0",false,CancellationToken.None);Check(reply!=null&&reply.Error==0&&reply.Values[0].Value.Display()=="snmptool-agent","Independent Net-SNMP GET failed for "+profile.Level+" / "+profile.Authentication);Console.WriteLine("Net-SNMP interoperability passed: "+profile.Level+" / "+profile.Authentication);}
        var client=Client(Profile("private",V3Level.AuthPriv));var walk=Walker.Read(client,IPAddress.Loopback,3,"","1.3.6.1.2.1.1",64,row=>{},CancellationToken.None);Check(walk.Rows.Count>=6&&walk.Status.StartsWith("Read complete"),"Independent encrypted GETNEXT walk failed.");
        var adapter=new Adapter{Name="Local independent agent",Address=IPAddress.Loopback,Mask=IPAddress.Parse("255.255.255.255"),Up=true};var devices=new List<DeviceResult>();var scan=Scanner.Scan(new ScanOptions{Adapter=adapter,Range=IpRange.Parse("127.0.0.1/32",adapter),Version=3,V3=Profile("private",V3Level.AuthPriv),Port=1161,Timeout=1000,Workers=1},row=>devices.Add(row),p=>{},CancellationToken.None);Check(devices.Count==1&&devices[0].Version==3&&devices[0].Name=="snmptool-agent"&&scan.Errors==0,"Independent v3 subnet discovery failed.");
        var wrongAuth=Profile("private",V3Level.AuthPriv);wrongAuth.AuthPassword="wrong-authentication";Rejected(wrongAuth);
        var wrongPrivacy=Profile("private",V3Level.AuthPriv);wrongPrivacy.PrivacyPassword="wrong-privacy";Rejected(wrongPrivacy);
        Rejected(Profile("unknown-user",V3Level.AuthPriv));
        var wrongContext=Profile("private",V3Level.AuthPriv);wrongContext.Context="does-not-exist";Rejected(wrongContext);
        Console.WriteLine("Independent Net-SNMP v3 discovery, encrypted walk, invalid authentication/privacy credentials, unknown user and context tests passed.");return 0;
    }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
