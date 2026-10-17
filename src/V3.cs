using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace SnmptoolRAV {
    public enum V3Level { NoAuthNoPriv, AuthNoPriv, AuthPriv }
    public enum V3Auth { Sha256, Sha384, Sha512, Sha1 }
    public sealed class V3Profile {
        public string UserName=""; public string Context=""; public V3Level Level=V3Level.AuthPriv;
        public V3Auth Authentication=V3Auth.Sha256; public string AuthPassword=""; public string PrivacyPassword="";
        public void Validate() {
            if(String.IsNullOrEmpty(UserName)||Encoding.UTF8.GetByteCount(UserName)>32)throw new ArgumentException("SNMPv3 username must contain 1 to 32 UTF-8 bytes.");
            if(Context==null||Encoding.UTF8.GetByteCount(Context)>32)throw new ArgumentException("SNMPv3 context must contain at most 32 UTF-8 bytes (empty is allowed).");
            if(!Enum.IsDefined(typeof(V3Level),Level)||!Enum.IsDefined(typeof(V3Auth),Authentication))throw new ArgumentException("Choose a supported SNMPv3 security profile.");
            if(Level!=V3Level.NoAuthNoPriv&&(AuthPassword==null||Encoding.UTF8.GetByteCount(AuthPassword)<8||Encoding.UTF8.GetByteCount(AuthPassword)>1024))throw new ArgumentException("SNMPv3 authentication passphrase must contain 8 to 1024 UTF-8 bytes.");
            if(Level==V3Level.AuthPriv&&(PrivacyPassword==null||Encoding.UTF8.GetByteCount(PrivacyPassword)<8||Encoding.UTF8.GetByteCount(PrivacyPassword)>1024))throw new ArgumentException("SNMPv3 privacy passphrase must contain 8 to 1024 UTF-8 bytes.");
        }
        public Levels Flags {get{return Level==V3Level.NoAuthNoPriv?0:Level==V3Level.AuthNoPriv?Levels.Authentication:Levels.Authentication|Levels.Privacy;}}
        public IPrivacyProvider Provider() {
            Validate(); IAuthenticationProvider auth=DefaultAuthenticationProvider.Instance;
            if(Level!=V3Level.NoAuthNoPriv) {
                var phrase=new OctetString(Encoding.UTF8.GetBytes(AuthPassword));
                switch(Authentication) {
                    case V3Auth.Sha256:auth=new SHA256AuthenticationProvider(phrase);break;
                    case V3Auth.Sha384:auth=new SHA384AuthenticationProvider(phrase);break;
                    case V3Auth.Sha512:auth=new SHA512AuthenticationProvider(phrase);break;
#pragma warning disable 618
                    // Explicit legacy compatibility option; never selected as a fallback.
                    case V3Auth.Sha1:auth=new SHA1AuthenticationProvider(phrase);break;
#pragma warning restore 618
                }
            }
            return Level==V3Level.AuthPriv?(IPrivacyProvider)new AESPrivacyProvider(new OctetString(Encoding.UTF8.GetBytes(PrivacyPassword)),auth):new DefaultPrivacyProvider(auth);
        }
    }
    public sealed class SnmpSecurityException : Exception {
        public readonly string Status;
        public SnmpSecurityException(string status,string message):base(message){Status=status;}
    }
    sealed class V3Session {
        public OctetString Engine; public int Boots; public int Time; public Stopwatch Clock=Stopwatch.StartNew(); public IPrivacyProvider Provider;
        public int CurrentTime {get{return (int)Math.Min(Int32.MaxValue,(long)Time+(long)Clock.Elapsed.TotalSeconds);}}
        public void Update(ISnmpMessage message){Engine=message.Parameters.EngineId;Boots=message.Parameters.EngineBoots.ToInt32();Time=message.Parameters.EngineTime.ToInt32();Clock.Restart();}
        public ISnmpMessage Anchor() {
            return new ReportMessage(VersionCode.V3,new Header(new Integer32(1),new Integer32(65507),0),new SecurityParameters(Engine,new Integer32(Boots),new Integer32(CurrentTime),OctetString.Empty,OctetString.Empty,OctetString.Empty),new Scope(Engine,OctetString.Empty,new ReportPdu(0,ErrorCode.NoError,0,new List<Variable>())),DefaultPrivacyProvider.DefaultPair,null);
        }
    }
    public sealed partial class SnmpClient {
        readonly V3Profile profile;
        readonly Dictionary<string,V3Session> sessions=new Dictionary<string,V3Session>();
        static int v3Id=Environment.TickCount&0x3fffffff;
        static int NextId(){return Interlocked.Increment(ref v3Id)&Int32.MaxValue;}
        static SnmpSecurityException Rejected(string status,string message){return new SnmpSecurityException(status,message);}
        static void ValidateEnvelope(byte[] bytes) {
            int offset=0;var root=Ber.Read(bytes,ref offset);if(root.Tag!=0x30||offset!=bytes.Length)throw new FormatException("Invalid v3 packet.");
            var pending=new Stack<Tuple<Ber,int>>();pending.Push(Tuple.Create(root,0));int nodes=0;
            while(pending.Count>0){var current=pending.Pop();if(++nodes>4096||current.Item2>16)throw new FormatException("SNMP message is too complex.");if((current.Item1.Tag&32)!=0)foreach(var child in current.Item1.Children(current.Item1.Tag))pending.Push(Tuple.Create(child,current.Item2+1));}
            var outer=root.Children(0x30);if(outer.Count!=4||outer[0].Signed()!=3||outer[2].Tag!=4)throw new FormatException("Not an SNMPv3 message.");
            var header=outer[1].Children(0x30);
            if(header.Count!=4||header[0].Signed()<0||header[1].Signed()<484||header[2].Tag!=4||header[2].Data.Length!=1||header[3].Signed()!=3)throw new FormatException("Invalid v3 header.");
            byte flags=header[2].Data[0];if(flags>7||(flags&2)!=0&&(flags&1)==0||((flags&2)!=0?outer[3].Tag!=4:outer[3].Tag!=0x30))throw new FormatException("Invalid v3 security flags or scope encoding.");
            int p=0;var usm=Ber.Read(outer[2].Data,ref p).Children(0x30);
            if(p!=outer[2].Data.Length||usm.Count!=6||usm[0].Tag!=4||usm[1].Tag!=2||usm[2].Tag!=2||usm[3].Tag!=4||usm[4].Tag!=4||usm[5].Tag!=4||usm[1].Data.Length>4||usm[2].Data.Length>4)throw new FormatException("Invalid v3 security parameters.");
        }
        ISnmpMessage ExchangeV3(IPAddress target,byte[] bytes,int messageId,UserRegistry registry,CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();using(var socket=BoundSocket()) {
                socket.Connect(new IPEndPoint(target,port));socket.Send(bytes);var clock=Stopwatch.StartNew();var buffer=new byte[65535];
                while(clock.ElapsedMilliseconds<timeout) {
                    cancel.ThrowIfCancellationRequested();long remaining=timeout-clock.ElapsedMilliseconds;if(remaining<=0)break;
                    if(!socket.Poll((int)Math.Min(50,remaining)*1000,SelectMode.SelectRead))continue;
                    try {
                        int count=socket.Receive(buffer);var packet=buffer.Take(count).ToArray();ISnmpMessage message;
                        try {ValidateEnvelope(packet);var parsed=MessageFactory.ParseMessages(packet,registry);if(parsed.Count!=1)continue;message=parsed[0];}
                        catch(Exception ex){if(ex is OutOfMemoryException)throw;continue;}
                        if(message.Version!=VersionCode.V3||message.Header.MessageId!=messageId)continue;
                        if(message is MalformedMessage)throw Rejected("V3 decoding failed","The SNMPv3 response could not be authenticated or decrypted. Verify the selected profile.");
                        if(message.Parameters.IsInvalid)throw Rejected("Authentication failed","The SNMPv3 response failed its authentication check. No data was accepted.");
                        if(message.TypeCode()!=SnmpType.ResponsePdu&&message.TypeCode()!=SnmpType.ReportPdu)continue;
                        return message;
                    }catch(SocketException ex){if(ex.SocketErrorCode==SocketError.ConnectionReset||ex.SocketErrorCode==SocketError.ConnectionRefused||ex.SocketErrorCode==SocketError.TimedOut)return null;throw;}
                }
                return null;
            }
        }
        V3Session DiscoverV3(IPAddress target,CancellationToken cancel) {
            int id=NextId();var discovery=new Discovery(id,NextId(),65507);
            var reply=ExchangeV3(target,discovery.ToBytes(),id,new UserRegistry(),cancel);
            if(reply==null)return null;
            if(reply.TypeCode()!=SnmpType.ReportPdu||reply.Parameters.EngineId==null||reply.Parameters.EngineId.GetRaw().Length<5||reply.Parameters.EngineId.GetRaw().Length>32||reply.Parameters.EngineBoots.ToInt32()<0||reply.Parameters.EngineTime.ToInt32()<0)throw Rejected("V3 discovery failed","The agent returned invalid SNMPv3 engine information.");
            var session=new V3Session {Provider=profile.Provider()};session.Update(reply);return session;
        }
        static string ReportReason(ISnmpMessage report) {
            if(report.Scope.Pdu.Variables.Count==0)return "The agent returned an SNMPv3 report without usable values.";
            switch(report.Scope.Pdu.Variables[0].Id.ToString()) {
                case "1.3.6.1.6.3.15.1.1.1.0":return "Agent report: unsupported security level. Verify the v3 security profile.";
                case "1.3.6.1.6.3.15.1.1.2.0":return "Agent report: engine time window mismatch.";
                case "1.3.6.1.6.3.15.1.1.3.0":return "Agent report: unknown SNMPv3 username.";
                case "1.3.6.1.6.3.15.1.1.4.0":return "Agent report: unknown engine ID.";
                case "1.3.6.1.6.3.15.1.1.5.0":return "Agent report: authentication digest mismatch.";
                case "1.3.6.1.6.3.15.1.1.6.0":return "Agent report: privacy/decryption failure.";
                default:return "Agent report: requested SNMPv3 profile or context was not accepted.";
            }
        }
        SnmpMessage QueryV3(IPAddress target,string oid,bool next,CancellationToken cancel) {
            if(profile==null)throw new ArgumentException("Configure an SNMPv3 profile first.");profile.Validate();string key=target.ToString();V3Session session;
            lock(sessions)sessions.TryGetValue(key,out session);
            if(session==null){session=DiscoverV3(target,cancel);if(session==null)return null;lock(sessions)sessions[key]=session;}
            lock(session) {
                for(int attempt=0;attempt<2;attempt++) {
                    cancel.ThrowIfCancellationRequested();int messageId=NextId(),requestId=NextId();var user=new OctetString(Encoding.UTF8.GetBytes(profile.UserName));var context=new OctetString(Encoding.UTF8.GetBytes(profile.Context));
                    var bindings=new List<Variable>{new Variable(new ObjectIdentifier(oid))};ISnmpMessage request;
                    try {
                        request=next?(ISnmpMessage)new GetNextRequestMessage(VersionCode.V3,messageId,requestId,user,context,bindings,session.Provider,65507,session.Anchor()):new GetRequestMessage(VersionCode.V3,messageId,requestId,user,context,bindings,session.Provider,65507,session.Anchor());
                    }catch(Exception ex){if(ex is OutOfMemoryException)throw;throw Rejected("V3 profile unavailable","Could not prepare the selected SNMPv3 authentication/privacy profile on this system.");}
                    var registry=new UserRegistry();registry.Add(user,session.Provider);
                    var reply=ExchangeV3(target,request.ToBytes(),messageId,registry,cancel);
                    if(reply==null)throw Rejected("No profile response","An SNMPv3 engine answered discovery, but no valid profile response arrived. Check credentials, algorithms, context and access policy.");
                    bool report=reply.TypeCode()==SnmpType.ReportPdu;
                    if(report) {
                        string reportOid=reply.Scope.Pdu.Variables.Count==0?"":reply.Scope.Pdu.Variables[0].Id.ToString();
                        if(attempt==0&&(reportOid=="1.3.6.1.6.3.15.1.1.2.0"||reportOid=="1.3.6.1.6.3.15.1.1.4.0")) {
                            // Re-discover once, then retry the same security profile. Never weaken security.
                            var refreshed=DiscoverV3(target,cancel);if(refreshed==null)throw Rejected("V3 resync failed","The agent did not answer engine rediscovery.");
                            session.Engine=refreshed.Engine;session.Boots=refreshed.Boots;session.Time=refreshed.Time;session.Clock.Restart();session.Provider=refreshed.Provider;continue;
                        }
                        throw Rejected("V3 agent report",ReportReason(reply));
                    }
                    if((reply.Header.SecurityLevel&(Levels.Authentication|Levels.Privacy))!=profile.Flags)throw Rejected("Security downgrade rejected","The response did not use the requested SNMPv3 authentication/privacy level. No data was accepted.");
                    if(reply.Parameters.UserName!=user||reply.Parameters.EngineId!=session.Engine||reply.Scope.ContextName!=context||reply.Scope.ContextEngineId!=session.Engine||reply.Scope.Pdu.RequestId.ToInt32()!=requestId)throw Rejected("V3 response mismatch","The response did not match the requested user, context, engine or request. No data was accepted.");
                    if(reply.Parameters.EngineBoots.ToInt32()!=session.Boots||Math.Abs((long)reply.Parameters.EngineTime.ToInt32()-session.CurrentTime)>150)throw Rejected("V3 time check failed","The response was outside the accepted SNMPv3 engine time window. No data was accepted.");
                    int error=reply.Scope.Pdu.ErrorStatus.ToInt32();if(error<0||error>18)throw new FormatException("Invalid SNMPv3 error status.");
                    var result=new SnmpMessage {Version=3,Pdu=0xa2,RequestId=requestId,Error=error,Community=new byte[0],Values=new List<SnmpValue>()};
                    foreach(var binding in reply.Scope.Pdu.Variables)using(var stream=new MemoryStream()){binding.Data.AppendBytesTo(stream);int position=0;var bytes=stream.ToArray();var value=Ber.Read(bytes,ref position);if(position!=bytes.Length)throw new FormatException("Invalid SNMPv3 value.");result.Values.Add(new SnmpValue{Oid=binding.Id.ToString(),Value=value});}
                    if(error==0&&(result.Values.Count!=1||(!next&&result.Values[0].Oid!=oid)))throw Rejected("V3 binding mismatch","The response did not match the requested OID. No data was accepted.");
                    session.Update(reply);return result;
                }
            }
            throw Rejected("V3 resync failed","The agent rejected the time window after a bounded rediscovery attempt.");
        }
    }
}
