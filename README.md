# snmptoolRAV

Portable Windows SNMP discovery and device-data reader. Choose a configured IPv4 adapter, enter a community name, and scan its subnet for SNMP devices such as printers, switches and storage appliances.

## Download and run

Download `snmptoolRAV.exe` from [Releases](https://github.com/stainleSSStar/snmptoolRAV/releases), save it in any folder or USB drive and launch it. No installer, administrator elevation, Windows SNMP service, internet access or third-party runtime download is needed to run the tool. Requires Windows 10/11 with .NET Framework 4.8 enabled. The EXE is unsigned and may trigger Windows reputation warnings.

1. Choose **Network adapter / IPv4 address**. Every configured IPv4 address is listed, including addresses on virtual, VPN, Hyper-V, VMware and other interfaces exposed by Windows. Disconnected entries remain visible; an adapter must be up before scanning. Multiple addresses on an adapter appear separately.
2. Check **IPv4 subnet (CIDR)**. The app starts with the selected address and its prefix, narrowed to `/24` when the configured subnet is broader. The scan is normalized to the actual network boundary. You may narrow the range or scan a larger part of that adapter's configured subnet, up to `/16` (65,534 hosts). Use `/32` to query one address. IPv6 discovery is not included in this release.
3. Enter **Community**. It is masked by default; **Show** reveals it. The app does not save it in configuration files, CSV exports, logs or release assets. Community-based SNMP v1/v2c transmits this name unencrypted on the selected network.
4. Leave **Version** on **Auto** to try v2c, then v1 only if no matching response arrives, or explicitly choose either version. SNMPv3 is planned as a separate authentication profile; see [ROADMAP.md](ROADMAP.md).
5. Leave **UDP port** at 161 unless your device uses another port. Adjust reply timeout (100–5,000 ms) and retries (0–2 per version during discovery) if needed.
6. Click **Scan subnet**. Requests are sent only after you click; starting the app only reads local adapter configuration. **Stop** cancels outstanding polling and preserves discovered devices.

The app sends read-only SNMP GET/GETNEXT requests. It does not change device settings, use SET requests, sweep other subnets, require ping replies, change routes or install services. IPv4 packets are bound to the selected source address and Windows `IP_UNICAST_IF` interface index. A binding failure is shown rather than silently switching to another adapter. Routing/firewall restrictions on a virtual interface may still prevent access to devices.

Discovery is bounded to 24 concurrent workers, with a configurable reply timeout and retry count. Network/broadcast addresses are skipped for `/16`–`/30`; both addresses are usable for `/31`. Scan progress reports checked addresses, SNMP responders and network/response errors. Only devices that return a matching SNMP response appear in the results.

## Read device data

Select a discovered device, then click **Read selected device**. In **Device data**, choose a subtree and click **Read data**:

| Preset | Numeric subtree | Typical data |
| --- | --- | --- |
| System information | `1.3.6.1.2.1.1` | Description, name, location, contact, uptime and object ID |
| Printer MIB | `1.3.6.1.2.1.43` | Printer name, serial number, counters, supplies and alerts when exposed |
| Host devices | `1.3.6.1.2.1.25.3` | Host-resource device descriptions/status where supported |
| Standard MIB-II | `1.3.6.1.2.1` | Broader standard device information |
| Custom OID | Enter any numeric subtree | Vendor-specific or narrower tables exposed by your device |

The OID field is editable for every preset. Results show numeric OID, a friendly field label where known, the returned ASN.1 type and the raw value. Select a value to copy its full contents from the text area. Values retain their units/codes: printer supply levels can be negative special values, and counters are not automatically assumed to be percentages or pages. Printer-MIB implementation varies by device and community access policy; fields that an agent does not expose cannot be collected.

Reads use the selected source adapter, current community, selected/auto-discovered version and current UDP port. Each GETNEXT walk stops at the subtree boundary, agent error/end-of-MIB, 512 values, 60 seconds or user cancellation. Non-progressing OIDs stop the walk. Timeout, truncation and partial-data conditions are reported; a capped read is not described as complete. Select a narrower OID to read a particular table. Discovery retries apply only to discovery; data walks stop on a timed-out request and preserve collected values.

An empty discovery result does **not** prove there are no devices: SNMP may be disabled, UDP blocked, the community/version incorrect or the selected adapter unable to reach them. Device Manager and printer drivers are not required for SNMP discovery.

## CSV export

**Export CSV** saves the currently selected tab: discovered devices or collected device values. Choose the path in the save dialog (initially beside the portable EXE). Export is UTF-8 with a BOM, escaped CSV fields and formula neutralization for values that could execute as spreadsheet formulas. Community strings are never exported.

No files are written during scanning or data reads. Results stay in memory until exported or the application closes. Changing the adapter clears results so previous devices are not queried accidentally through a different interface.

## Build and verify

On Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

Uses the built-in .NET Framework compiler, standard framework libraries and a small bounded SNMP/BER implementation; no NuGet dependencies. Output is a single AnyCPU `dist/snmptoolRAV.exe`. Development and CI tests communicate only with local loopback mock SNMP agents, not other devices on a runner's network.

Checks cover independent BER wire fixtures, signed integers/OIDs/Counter64, malformed/fuzzed packets, subnet boundaries/scope, real UDP v1/v2c requests, Windows interface-index binding, auto-version fallback, request-ID/community matching, cancellation, bounded GETNEXT walks and CSV handling. CI renders real WinForms discovery/detail/compact previews with **sample** device data. Physical printer/VPN/virtual-adapter reachability and vendor-specific MIB behavior need validation on your devices.

Tagged builds prepare a draft GitHub Release with EXE/ZIP downloads, checksums and UI previews for review before publication. Release assets avoid dependence on Actions artifact-storage quota.

Protocol references: [SNMPv1 / RFC 1157](https://www.rfc-editor.org/rfc/rfc1157), [SNMP operations / RFC 3416](https://www.rfc-editor.org/rfc/rfc3416), [MIB-II / RFC 1213](https://www.rfc-editor.org/rfc/rfc1213), [Printer-MIB / RFC 3805](https://www.rfc-editor.org/rfc/rfc3805), [Windows outgoing interface selection](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ip-socket-options).
