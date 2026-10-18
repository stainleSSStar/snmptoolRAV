using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SnmptoolRAV {
    public sealed class Ber {
        public byte Tag; public byte[] Data;
        public Ber(byte tag, byte[] data) { Tag = tag; Data = data; }
        public static byte[] Pack(byte tag, params byte[][] parts) {
            var data = parts.SelectMany(p => p).ToArray();
            using (var stream = new MemoryStream()) {
                stream.WriteByte(tag);
                if (data.Length < 128) stream.WriteByte((byte)data.Length);
                else { var length = new List<byte>(); int n = data.Length; while (n > 0) { length.Insert(0,(byte)(n & 255)); n >>= 8; } stream.WriteByte((byte)(128 | length.Count)); stream.Write(length.ToArray(),0,length.Count); }
                stream.Write(data,0,data.Length); return stream.ToArray();
            }
        }
        public static byte[] Integer(int value) {
            byte[] bytes = BitConverter.GetBytes(value); if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            int start = 0; while (start < 3 && ((bytes[start] == 0 && (bytes[start+1] & 128) == 0) || (bytes[start] == 255 && (bytes[start+1] & 128) != 0))) start++;
            return Pack(2,bytes.Skip(start).ToArray());
        }
        public long Signed() {
            if (Tag != 2 || Data.Length == 0 || Data.Length > 8) throw new FormatException("Invalid SNMP integer.");
            long value = (Data[0] & 128) != 0 ? -1 : 0; foreach (byte b in Data) value = (value << 8) | b; return value;
        }
        public ulong Unsigned() {
            if (Data.Length == 0 || Data.Length > 9 || (Data.Length == 9 && Data[0] != 0)) throw new FormatException("Invalid SNMP counter.");
            ulong value = 0; foreach (byte b in Data.Skip(Data.Length == 9 ? 1 : 0)) value = (value << 8) | b; return value;
        }
        public static Ber Read(byte[] data, ref int offset) {
            if (offset >= data.Length) throw new FormatException("Truncated BER tag.");
            byte tag = data[offset++];
            if (offset >= data.Length) throw new FormatException("Truncated BER length.");
            int length = data[offset++];
            if ((length & 128) != 0) {
                int count = length & 127; if (count == 0 || count > 3 || count > data.Length - offset) throw new FormatException("Invalid BER length.");
                length = 0; for (int i = 0;i<count;i++) length = (length << 8) | data[offset++];
            }
            if (length > data.Length - offset) throw new FormatException("Truncated BER value.");
            byte[] value = new byte[length]; Buffer.BlockCopy(data,offset,value,0,length); offset += length; return new Ber(tag,value);
        }
        public List<Ber> Children(byte expected) {
            if (Tag != expected) throw new FormatException("Unexpected BER container.");
            var list = new List<Ber>(); int position = 0;
            while (position < Data.Length) { if (list.Count >= 2048) throw new FormatException("Too many SNMP values."); list.Add(Read(Data,ref position)); }
            return list;
        }
        static void Base128(List<byte> data, ulong value) {
            var bytes = new List<byte>(); bytes.Add((byte)(value & 127)); value >>= 7;
            while (value > 0) { bytes.Insert(0,(byte)((value & 127) | 128)); value >>= 7; } data.AddRange(bytes);
        }
        public static ulong[] Arcs(string oid) {
            string[] parts = (oid ?? "").Trim().TrimStart('.').Split('.');
            if (parts.Length < 2 || parts.Length > 128) throw new FormatException("Enter a numeric OID such as 1.3.6.1.2.1.43.");
            var values = new List<ulong>(); foreach (var part in parts) {
                ulong value; if (part.Length == 0 || part.Any(c => c < '0' || c > '9') || !UInt64.TryParse(part,out value) || value > UInt32.MaxValue) throw new FormatException("Invalid numeric OID."); values.Add(value);
            }
            if (values[0] > 2 || (values[0] < 2 && values[1] > 39)) throw new FormatException("Invalid OID prefix."); return values.ToArray();
        }
        public static string NormalizeOid(string oid) { return String.Join(".",Arcs(oid).Select(a => a.ToString(CultureInfo.InvariantCulture))); }
        public static byte[] Oid(string oid) {
            var arcs = Arcs(oid); var data = new List<byte>(); Base128(data,arcs[0] * 40 + arcs[1]); foreach (ulong arc in arcs.Skip(2)) Base128(data,arc); return Pack(6,data.ToArray());
        }
        public string OidText() {
            if (Tag != 6 || Data.Length == 0) throw new FormatException("Invalid OID value.");
            var encoded = new List<ulong>(); ulong value = 0; int bytes = 0;
            foreach (byte b in Data) {
                if (++bytes > 6 || value > (UInt64.MaxValue >> 7)) throw new FormatException("OID arc overflow.");
                value = (value << 7) | (uint)(b & 127);
                if ((b & 128) == 0) { encoded.Add(value); value = 0; bytes = 0; }
            }
            if (bytes != 0 || encoded.Count == 0) throw new FormatException("Truncated OID.");
            ulong first = encoded[0]; var arcs = new List<ulong> { first < 40 ? 0UL : first < 80 ? 1UL : 2UL, first < 40 ? first : first < 80 ? first-40 : first-80 }; arcs.AddRange(encoded.Skip(1));
            return NormalizeOid(String.Join(".",arcs));
        }
        public string Display() {
            if (Tag == 2) return Signed().ToString(CultureInfo.InvariantCulture);
            if (Tag == 6) return OidText();
            if (Tag == 0x40 && Data.Length == 4) return new IPAddress(Data).ToString();
            if (Tag == 0x41 || Tag == 0x42 || Tag == 0x43 || Tag == 0x46) {
                ulong value = Unsigned();
                if (Tag != 0x46 && value > UInt32.MaxValue) throw new FormatException("SNMP 32-bit counter overflow.");
                if (Tag == 0x43) return value.ToString(CultureInfo.InvariantCulture) + " ticks (" + TimeSpan.FromMilliseconds(value * 10.0).ToString() + ")";
                return value.ToString(CultureInfo.InvariantCulture);
            }
            if (Tag == 5) return "NULL";
            if (Tag == 0x80) return "No such object"; if (Tag == 0x81) return "No such instance"; if (Tag == 0x82) return "End of MIB";
            if (Tag == 4) {
                string text;
                try { text = new UTF8Encoding(false,true).GetString(Data).TrimEnd('\0'); } catch (DecoderFallbackException) { return "hex: " + BitConverter.ToString(Data).Replace("-"," "); }
                if (!text.Any(c => Char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')) return text;
            }
            return "hex: " + BitConverter.ToString(Data).Replace("-"," ");
        }
        public string TypeName { get { switch (Tag) { case 2:return "Integer"; case 4:return "String"; case 5:return "NULL"; case 6:return "OID"; case 0x40:return "IP address"; case 0x41:return "Counter32"; case 0x42:return "Gauge32"; case 0x43:return "TimeTicks"; case 0x46:return "Counter64"; case 0x80:case 0x81:case 0x82:return "Exception"; default:return "0x" + Tag.ToString("X2"); } } }
    }
    public sealed class SnmpValue { public string Oid; public Ber Value; }
    public sealed class SnmpMessage {
        public int Version; public byte[] Community; public byte Pdu; public int RequestId; public int Error; public List<SnmpValue> Values;
        public static byte[] Encode(int version, string community, int request, byte pdu, IEnumerable<SnmpValue> values, int error) {
            if (version != 0 && version != 1) throw new ArgumentException("Only SNMP v1 and v2c are supported.");
            if (pdu != 0xa0 && pdu != 0xa1 && pdu != 0xa2) throw new ArgumentException("This tool supports only GET, GETNEXT and their responses.");
            byte[][] bindings = values.Select(v => Ber.Pack(0x30,Ber.Oid(v.Oid),Ber.Pack(v.Value.Tag,v.Value.Data))).ToArray();
            return Ber.Pack(0x30,Ber.Integer(version),Ber.Pack(4,Encoding.UTF8.GetBytes(community)),Ber.Pack(pdu,Ber.Integer(request),Ber.Integer(error),Ber.Integer(0),Ber.Pack(0x30,bindings)));
        }
        public static SnmpMessage Decode(byte[] packet) {
            if (packet == null || packet.Length > 65507) throw new FormatException("Invalid datagram size.");
            int offset = 0; var message = Ber.Read(packet,ref offset).Children(0x30);
            if (offset != packet.Length || message.Count != 3 || message[1].Tag != 4) throw new FormatException("Invalid SNMP message.");
            int version = checked((int)message[0].Signed()); if (version != 0 && version != 1) throw new FormatException("Unsupported SNMP version.");
            byte type = message[2].Tag; if (type != 0xa0 && type != 0xa1 && type != 0xa2) throw new FormatException("Unexpected SNMP PDU.");
            var pdu = message[2].Children(type); if (pdu.Count != 4) throw new FormatException("Invalid SNMP PDU.");
            var values = new List<SnmpValue>();
            foreach (var binding in pdu[3].Children(0x30)) { var pair = binding.Children(0x30); if (pair.Count != 2) throw new FormatException("Invalid varbind."); values.Add(new SnmpValue { Oid = pair[0].OidText(), Value = pair[1] }); }
            long error = pdu[1].Signed(); long errorIndex = pdu[2].Signed();
            if (error < 0 || error > (version == 0 ? 5 : 18) || errorIndex < 0) throw new FormatException("Invalid SNMP error fields.");
            return new SnmpMessage { Version = version, Community = message[1].Data, Pdu = type, RequestId = checked((int)pdu[0].Signed()), Error = checked((int)pdu[1].Signed()), Values = values };
        }
    }
    public sealed partial class SnmpClient {
        static int nextRequest = Environment.TickCount & 0x3fffffff;
        readonly IPAddress source; readonly int index; readonly int port; readonly int timeout;
        public SnmpClient(IPAddress local, int interfaceIndex, int targetPort, int timeoutMs, V3Profile v3 = null) { source = local; index = interfaceIndex; port = targetPort; timeout = timeoutMs; profile = v3; }
        public void CheckBinding() { using (var socket = BoundSocket()) { } }
        Socket BoundSocket() {
            var socket = new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
            try {
                socket.Bind(new IPEndPoint(source,0));
                // Windows IP_UNICAST_IF: outgoing interface index in NETWORK byte order.
                if (Environment.OSVersion.Platform == PlatformID.Win32NT && index > 0) socket.SetSocketOption(SocketOptionLevel.IP,(SocketOptionName)31,IPAddress.HostToNetworkOrder(index));
                return socket;
            } catch { socket.Dispose(); throw; }
        }
        public SnmpMessage Query(IPAddress target, int version, string community, string oid, bool next, CancellationToken cancel) {
            oid = Ber.NormalizeOid(oid);
            if(version==3)return QueryV3(target,oid,next,cancel);
            cancel.ThrowIfCancellationRequested(); int request = Interlocked.Increment(ref nextRequest) & Int32.MaxValue;
            var expectedCommunity = Encoding.UTF8.GetBytes(community);
            var packet = SnmpMessage.Encode(version,community,request,next ? (byte)0xa1 : (byte)0xa0,new[] { new SnmpValue { Oid = oid, Value = new Ber(5,new byte[0]) } },0);
            using (var socket = BoundSocket()) {
                socket.Connect(new IPEndPoint(target,port)); socket.Send(packet);
                var timer = Stopwatch.StartNew(); var buffer = new byte[65535];
                while (timer.ElapsedMilliseconds < timeout) {
                    cancel.ThrowIfCancellationRequested();
                    long remaining = timeout-timer.ElapsedMilliseconds; if (remaining <= 0) break;
                    if (!socket.Poll((int)Math.Min(50,remaining)*1000,SelectMode.SelectRead)) continue;
                    try {
                        int length = socket.Receive(buffer); SnmpMessage reply;
                        try { reply = SnmpMessage.Decode(buffer.Take(length).ToArray()); } catch (FormatException) { continue; } catch (OverflowException) { continue; }
                        if (reply.Pdu != 0xa2 || reply.RequestId != request || reply.Version != version || !reply.Community.SequenceEqual(expectedCommunity)) continue;
                        if (reply.Error == 0 && (reply.Values.Count != 1 || (!next && reply.Values[0].Oid != oid))) continue;
                        return reply;
                    } catch (SocketException ex) { if (ex.SocketErrorCode == SocketError.ConnectionReset || ex.SocketErrorCode == SocketError.ConnectionRefused || ex.SocketErrorCode == SocketError.TimedOut) return null; throw; }
                }
                return null;
            }
        }
    }
}
