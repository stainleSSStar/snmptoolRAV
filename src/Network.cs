using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SnmptoolRAV {
    public sealed class Adapter {
        public string Name; public string Description; public IPAddress Address; public IPAddress Mask; public int Index; public bool Up;
        public override string ToString() { return Name + "  |  " + Address + "  |  " + (Up ? "Up" : "Not connected"); }
        public static List<Adapter> List() {
            var results = new List<Adapter>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                try {
                    var properties = nic.GetIPProperties(); var ipv4 = properties.GetIPv4Properties(); if (ipv4 == null) continue;
                    foreach (var address in properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.Equals(IPAddress.Any))) {
                        results.Add(new Adapter { Name = nic.Name, Description = nic.Description, Address = address.Address, Mask = address.IPv4Mask, Index = ipv4.Index, Up = nic.OperationalStatus == OperationalStatus.Up });
                    }
                } catch (NetworkInformationException) { }
            }
            return results.OrderByDescending(a => a.Up).ThenBy(a => IPAddress.IsLoopback(a.Address)).ThenBy(a => a.Name).ToList();
        }
        public string DefaultRange() { int prefix = IpRange.Prefix(Mask); return Address + "/" + Math.Max(24,prefix).ToString(CultureInfo.InvariantCulture); }
    }
    public sealed class IpRange {
        public uint Network; public uint Last; public uint FirstHost; public int Count; public int PrefixLength;
        public static uint Number(IPAddress address) { var b = address.GetAddressBytes(); if (b.Length != 4) throw new ArgumentException("IPv4 is required."); return ((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3]; }
        public static IPAddress Address(uint value) { return new IPAddress(new[] {(byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value}); }
        public static int Prefix(IPAddress mask) {
            uint n = Number(mask); bool zero = false; int prefix = 0;
            for (int i=31;i>=0;i--) { bool one = ((n>>i)&1) != 0; if (one && zero) throw new ArgumentException("The adapter has a non-contiguous IPv4 mask."); if (one) prefix++; else zero = true; } return prefix;
        }
        public static IpRange Parse(string text, Adapter adapter, bool allowRouted = false) {
            if (adapter == null) throw new ArgumentException("Choose a network adapter.");
            string[] parts = text.Trim().Split('/'); int prefix;
            if (parts.Length != 2 || !Int32.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out prefix) || prefix < 16 || prefix > 32) throw new ArgumentException("Use an IPv4 subnet from /16 to /32 (at most 65,534 hosts), e.g. 192.168.1.0/24.");
            string[] octets = parts[0].Split('.'); byte octet; if (octets.Length != 4 || octets.Any(p => !Byte.TryParse(p,NumberStyles.None,CultureInfo.InvariantCulture,out octet))) throw new ArgumentException("Enter a valid IPv4 address and prefix.");
            uint ip = Number(new IPAddress(octets.Select(p=>Byte.Parse(p,CultureInfo.InvariantCulture)).ToArray())); uint mask = UInt32.MaxValue << (32-prefix); uint first = ip & mask; uint last = first | ~mask;
            Prefix(adapter.Mask); uint localMask = Number(adapter.Mask); if (!allowRouted && ((first & localMask) != (Number(adapter.Address)&localMask) || (last & localMask) != (Number(adapter.Address)&localMask))) throw new ArgumentException("This range is outside the adapter's configured subnet. For a routed network or /32 VPN, explicitly enable Routed / VPN subnet.");
            if ((first>>24) == 0 || last >= 0xe0000000) throw new ArgumentException("Choose a unicast IPv4 subnet.");
            return new IpRange { Network = first, Last = last, FirstHost = prefix < 31 ? first+1 : first, Count = checked((int)((ulong)last-first+1-(prefix < 31 ? 2UL : 0UL))), PrefixLength = prefix };
        }
        public override string ToString() { return Address(Network) + "/" + PrefixLength.ToString(CultureInfo.InvariantCulture); }
    }
    public sealed class ScanOptions {
        public Adapter Adapter; public IpRange Range; public string Community; public int Version = -1; public int Port = 161; public int Timeout = 600; public int Retries; public int Workers = 24;
        public void Validate() {
            if (Adapter == null || Range == null) throw new ArgumentException("Choose an adapter and subnet.");
            if (String.IsNullOrEmpty(Community) || Encoding.UTF8.GetByteCount(Community) > 255) throw new ArgumentException("Enter a community name (1 to 255 UTF-8 bytes).");
            if (Version < -1 || Version > 1 || Port < 1 || Port > 65535 || Timeout < 100 || Timeout > 5000 || Retries < 0 || Retries > 2 || Workers < 1 || Workers > 32) throw new ArgumentException("Invalid scan settings.");
        }
    }
    public sealed class DeviceResult {
        public string IP; public string Name; public string Description; public int Version; public string Status;
        public string VersionText { get { return Version == 0 ? "v1" : "v2c"; } }
    }
    public sealed class ScanProgress { public int Completed; public int Total; public int Found; public int Errors; }
    public static class Scanner {
        const string DescriptionOid = "1.3.6.1.2.1.1.1.0";
        public static ScanProgress Scan(ScanOptions options, Action<DeviceResult> found, Action<ScanProgress> progress, CancellationToken cancel) {
            options.Validate(); var client = new SnmpClient(options.Adapter.Address,options.Adapter.Index,options.Port,options.Timeout); client.CheckBinding();
            int cursor=-1,completed=0,responders=0,errors=0;
            var tasks = new List<Task>();
            for (int worker=0;worker<Math.Min(options.Workers,options.Range.Count);worker++) {
                tasks.Add(Task.Factory.StartNew(() => {
                    while (!cancel.IsCancellationRequested) {
                        int index = Interlocked.Increment(ref cursor); if (index >= options.Range.Count) break;
                        var ip = IpRange.Address(options.Range.FirstHost+(uint)index);
                        try {
                            int[] versions = options.Version < 0 ? new[] {1,0} : new[] {options.Version};
                            foreach (int version in versions) {
                                SnmpMessage reply = null;
                                for (int attempt=0;attempt<=options.Retries && reply==null;attempt++) reply = client.Query(ip,version,options.Community,DescriptionOid,false,cancel);
                                if (reply == null) continue;
                                var result = new DeviceResult { IP = ip.ToString(), Name = "", Description = reply.Error == 0 ? reply.Values[0].Value.Display() : "SNMP error " + reply.Error, Version = version, Status = reply.Error == 0 ? "Responding" : "SNMP error " + reply.Error };
                                try {
                                    var name = client.Query(ip,version,options.Community,"1.3.6.1.2.1.1.5.0",false,cancel);
                                    if (name != null && name.Error == 0 && name.Values[0].Value.Tag == 4) result.Name = name.Values[0].Value.Display();
                                } catch (SocketException) { result.Status = "Responding; name unavailable"; }
                                Interlocked.Increment(ref responders); found(result); break;
                            }
                        } catch (OperationCanceledException) { break; }
                        catch (SocketException) { Interlocked.Increment(ref errors); }
                        catch (FormatException) { Interlocked.Increment(ref errors); }
                        finally {
                            int done = Interlocked.Increment(ref completed);
                            if (done % 8 == 0 || done == options.Range.Count) progress(new ScanProgress { Completed=done,Total=options.Range.Count,Found=Volatile.Read(ref responders),Errors=Volatile.Read(ref errors) });
                        }
                    }
                },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default));
            }
            Task.WaitAll(tasks.ToArray());
            var summary = new ScanProgress { Completed=completed,Total=options.Range.Count,Found=responders,Errors=errors };
            progress(summary); cancel.ThrowIfCancellationRequested(); return summary;
        }
    }
    public sealed class DataRow { public string Oid; public string Name; public string Type; public string Value; }
    public sealed class WalkResult { public readonly List<DataRow> Rows = new List<DataRow>(); public string Status; }
    public static class Walker {
        public static int CompareOid(string a, string b) {
            var x=Ber.Arcs(a); var y=Ber.Arcs(b); for (int i=0;i<Math.Min(x.Length,y.Length);i++) { int cmp=x[i].CompareTo(y[i]); if (cmp!=0) return cmp; } return x.Length.CompareTo(y.Length);
        }
        public static WalkResult Read(SnmpClient client, IPAddress target, int version, string community, string root, int maxRows, Action<DataRow> onRow, CancellationToken cancel) {
            root=Ber.NormalizeOid(root); string cursor=root; var result=new WalkResult(); var timer=System.Diagnostics.Stopwatch.StartNew();
            while (result.Rows.Count < maxRows) {
                cancel.ThrowIfCancellationRequested();
                if (timer.Elapsed.TotalSeconds > 60) { result.Status="Stopped at the 60-second time limit; collected data is partial."; return result; }
                var reply=client.Query(target,version,community,cursor,true,cancel);
                if (reply==null) { result.Status="Device did not reply; collected data is partial."; return result; }
                if (reply.Error!=0) { result.Status=version==0 && reply.Error==2 ? "End of accessible MIB (SNMPv1)." : "SNMP error " + reply.Error + "; collected data may be partial."; return result; }
                var binding=reply.Values[0];
                if (binding.Value.Tag==0x80 || binding.Value.Tag==0x81 || binding.Value.Tag==0x82 || !(binding.Oid==root || binding.Oid.StartsWith(root+".",StringComparison.Ordinal))) { result.Status="Read complete. End of accessible subtree."; return result; }
                if (CompareOid(binding.Oid,cursor)<=0) { result.Status="Stopped: device returned a repeated or out-of-order OID. Data is partial."; return result; }
                var row=new DataRow { Oid=binding.Oid, Name=Mib.Name(binding.Oid), Type=binding.Value.TypeName, Value=binding.Value.Display() };
                result.Rows.Add(row); onRow(row); cursor=binding.Oid;
            }
            result.Status="Stopped at " + maxRows + " values; narrow the OID subtree to read more."; return result;
        }
    }
    public static class Mib {
        static readonly Dictionary<string,string> names = new Dictionary<string,string> {
            {"1.3.6.1.2.1.1.1","System description"},{"1.3.6.1.2.1.1.2","System object ID"},{"1.3.6.1.2.1.1.3","Uptime"},{"1.3.6.1.2.1.1.4","Contact"},{"1.3.6.1.2.1.1.5","System name"},{"1.3.6.1.2.1.1.6","Location"},{"1.3.6.1.2.1.1.7","Services"},
            {"1.3.6.1.2.1.25.3.2.1.3","Device description"},{"1.3.6.1.2.1.25.3.5.1.1","Printer status"},{"1.3.6.1.2.1.43.5.1.1.16","Printer name"},{"1.3.6.1.2.1.43.5.1.1.17","Printer serial number"},
            {"1.3.6.1.2.1.43.10.2.1.3","Counter unit"},{"1.3.6.1.2.1.43.10.2.1.4","Marker life count"},{"1.3.6.1.2.1.43.11.1.1.6","Supply description"},{"1.3.6.1.2.1.43.11.1.1.7","Supply unit"},{"1.3.6.1.2.1.43.11.1.1.8","Supply maximum capacity"},{"1.3.6.1.2.1.43.11.1.1.9","Supply level"},{"1.3.6.1.2.1.43.18.1.1.8","Printer alert"}
        };
        public static string Name(string oid) { foreach (var item in names.OrderByDescending(p=>p.Key.Length)) if (oid==item.Key || oid.StartsWith(item.Key+".",StringComparison.Ordinal)) return item.Value + (oid.Length>item.Key.Length ? " ["+oid.Substring(item.Key.Length+1)+"]" : ""); return ""; }
    }
    public static class Csv {
        public static string Cell(string text) {
            text=text??""; string trimmed=text.TrimStart(); if (trimmed.Length>0 && "=+-@".IndexOf(trimmed[0])>=0) text="'"+text;
            return "\""+text.Replace("\"","\"\"")+"\"";
        }
        public static void Write(string path, IEnumerable<string[]> rows) { File.WriteAllLines(path,rows.Select(row=>String.Join(",",row.Select(Cell))),new UTF8Encoding(true)); }
    }
}
