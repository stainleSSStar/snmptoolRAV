# snmptoolRAV

Portable Windows SNMP discovery and device-data reader. Choose a configured IPv4 adapter, enter a community or SNMPv3 profile, and scan its subnet for SNMP devices such as printers, switches and storage appliances.

## Download and run

Download `snmptoolRAV.exe` from [Releases](https://github.com/stainleSSStar/snmptoolRAV/releases), save it in any folder or USB drive and launch it. No installer, administrator elevation, Windows SNMP service, internet access or third-party runtime download is needed to run the tool. Requires Windows 10/11 with .NET Framework 4.8 enabled. The EXE is unsigned and may trigger Windows reputation warnings.

1. Choose **Network adapter / IPv4 address**. Every configured IPv4 address is listed, including addresses on virtual, VPN, Hyper-V, VMware and other interfaces exposed by Windows. Disconnected entries remain visible; an adapter must be up before scanning. Multiple addresses on an adapter appear separately.
2. Check **IPv4 subnet (CIDR)**. The app starts with the selected address and its prefix, narrowed to `/24` when the configured subnet is broader. The scan is normalized to the actual network boundary. You may narrow the range or scan a larger part of that adapter's configured subnet, up to `/16` (65,534 hosts). For VPNs with `/32` addressing or other routed networks, explicitly enable **Routed / VPN subnet** to scan a CIDR outside the directly configured subnet through the selected adapter; Windows must have a usable route for it. Use `/32` to query one address. IPv6 discovery is not included in this release.
3. Enter **Community**. It is masked by default; **Show** reveals it. The app does not save it in configuration files, CSV exports, logs or release assets. Community-based SNMP v1/v2c transmits this name unencrypted on the selected network.
4. Leave **Version** on **Auto** to try v2c, then v1 only if no matching response arrives, or explicitly choose either version. Select **SNMP v3** for a separate username/authentication/privacy profile. Auto tries only community-based v2c/v1; it never attempts or falls back from v3.
5. Leave **UDP port** at 161 unless your device uses another port. Adjust reply timeout (100–5,000 ms) and retries (0–2 per version during discovery) if needed.
6. Click **Scan subnet**. Requests are sent only after you click; starting the app only reads local adapter configuration. **Stop** cancels outstanding polling and preserves discovered devices.

The app sends read-only SNMP GET/GETNEXT requests, plus SNMPv3 engine discovery. It does not change device settings, use SET requests, select additional subnets automatically, require ping replies, change routes or install services. IPv4 packets are bound to the selected source address and Windows `IP_UNICAST_IF` interface index. A binding failure is shown rather than silently switching to another adapter. Routing/firewall restrictions on a virtual interface may still prevent access to devices.

Discovery is bounded to 24 concurrent workers, with a configurable reply timeout and retry count. In v3 mode a probe can include engine discovery and a profile request. Network/broadcast addresses are skipped for `/16`–`/30`; both addresses are usable for `/31`. Scan progress reports checked addresses, SNMP responders and network/response/profile errors. Only devices that return a matching SNMP response appear in the results.

## SNMPv3 profiles

Choose **SNMP v3** in Version, then **Configure v3...**. The community field is replaced by a profile button. Match your device's settings:

- **Username**: 1–32 UTF-8 bytes. **Context name** is optional (up to 32 UTF-8 bytes); leave it empty for the device's default context.
- **noAuthNoPriv**: username, without message authentication or encryption.
- **authNoPriv**: username and authentication passphrase; values are authenticated but unencrypted.
- **authPriv**: authentication plus AES-128 encryption, using separate authentication and privacy passphrases.
- Authentication choices: **SHA-256** (default), **SHA-384**, **SHA-512**, or explicitly selected **SHA-1 (legacy)**. Privacy is **AES-128**. MD5, DES and AES-192/256 are not offered in this release.

Passphrases need 8–1,024 UTF-8 bytes. Only fields needed for the selected security level are enabled. Apply the profile, then scan/read normally. Passwords remain masked unless Show passphrases is selected. Configured profile fields (including usernames and passwords) are not persisted or exported; profile objects and library key caches exist only in process memory until the app exits.

The app discovers each agent's engine ID and clock before sending profile requests. It validates authentication/decryption, response security flags, username, engine, context, message/request IDs and the engine time window before accepting values. A time/engine report permits one bounded rediscovery/retry using the same profile. It never switches to weaker authentication, removes encryption or falls back to v1/v2c. Valid data replies update the cached engine clock during a scan/read.

A device that answers engine discovery but rejects the requested profile is shown with an explicit v3 report/security/no-profile-response status. Such a row confirms a v3 engine replied, **not** that credentials were accepted or values were read. Check the device's user, algorithms, passphrases, context and access policy. Invalid packets and profile failures never populate the value table.

The embedded dependency is [SharpSNMP 12.5.7](https://www.nuget.org/packages/Lextm.SharpSnmpLib/12.5.7). Its MIT notice is included in the EXE (**About**), the ZIP and [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

## Read device data

Select a discovered device, then click **Read selected device** (or double-click its row). Connection settings collapse automatically in the data tab to give the table more space; use **Show connection settings** at the top to change the community, version or adapter. In **Device data**, choose a subtree and click **Read data**:

| Preset | Numeric subtree | Typical data |
| --- | --- | --- |
| System information | `1.3.6.1.2.1.1` | Description, name, location, contact, uptime and object ID |
| Printer MIB | `1.3.6.1.2.1.43` | Printer name, serial number, counters, supplies and alerts when exposed |
| Host devices | `1.3.6.1.2.1.25.3` | Host-resource device descriptions/status where supported |
| Standard MIB-II | `1.3.6.1.2.1` | Broader standard device information |
| Custom OID | Enter any numeric subtree | Vendor-specific or narrower tables exposed by your device |

The OID field is editable for every preset. Results show numeric OID, a friendly field label where known, the returned ASN.1 type and the raw value. Select a row to inspect its full contents in the text area and use **Copy value**. OIDs sort numerically when you click their column header. Values retain their units/codes: printer supply levels can be negative special values, and counters are not automatically assumed to be percentages or pages. Printer-MIB implementation varies by device and community access policy; fields that an agent does not expose cannot be collected.

Reads use the selected source adapter, current community, selected/auto-discovered version and current UDP port. Each GETNEXT walk stops at the subtree boundary, agent error/end-of-MIB, 512 values, 60 seconds or user cancellation. Non-progressing OIDs stop the walk. Timeout, truncation and partial-data conditions are reported; a capped read is not described as complete. Select a narrower OID to read a particular table. Discovery retries apply only to discovery; data walks stop on a timed-out request and preserve collected values.

An empty discovery result does **not** prove there are no devices: SNMP may be disabled, UDP blocked, the community/version incorrect or the selected adapter unable to reach them. Device Manager and printer drivers are not required for SNMP discovery.

## CSV export

**Export CSV** saves the currently selected tab: discovered devices or collected device values. Choose the path in the save dialog (initially beside the portable EXE). Export is UTF-8 with a BOM, escaped CSV fields and formula neutralization for values that could execute as spreadsheet formulas. Configured community strings, v3 usernames and authentication/privacy passphrases are never added to exports. Returned device MIB values are exported as displayed.

No files are written during scanning or data reads. Results stay in memory until exported or the application closes. Changing the adapter, SNMP version or v3 profile clears results and the value viewer so previous data cannot be mistaken for results from the new connection settings.

## Tests without EXE builds

On Windows with .NET Framework 4.8, run:

```powershell
powershell -STA -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
```

This compiles the protocol tests and application sources as DLLs under `.build/checks`, runs the loopback protocol suite and renders real WinForms previews with UI regression checks. It does not call `build.ps1`, create EXEs or write release assets. The pinned dependency is downloaded and its checksum verified on first use. UI previews are saved under `.build/checks/ui-previews`. Pull requests and ordinary pushes use this test-only path in CI.

The independent Linux Net-SNMP suite runs with `bash tests/interop/run.sh` (requires .NET 8 SDK and `snmpd`). Its managed test DLL runs through `dotnet`, with native apphost generation disabled.

## Build and verify

To explicitly build the portable application on Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

Uses the built-in .NET Framework compiler, standard framework libraries, the existing bounded v1/v2c codec and pinned SharpSNMP 12.5.7 for v3. The build downloads the official NuGet package, checks its pinned SHA-256 and embeds the framework assembly in the EXE. First build needs internet; app use does not. Output is a single AnyCPU `dist/snmptoolRAV.exe`. Development and CI tests communicate only with local loopback test SNMP agents, not other devices on a runner's network.

Checks cover independent BER wire fixtures, signed integers/OIDs/Counter64, malformed/fuzzed packets, subnet boundaries/scope and explicit routed ranges for /32 VPNs, real UDP v1/v2c requests, Windows interface-index binding, auto-version fallback, request-ID/community matching, cancellation, bounded GETNEXT walks and CSV handling. Windows protocol tests load SharpSNMP from an EXE resource with no companion DLL present. V3 tests cover all supported SHA/AES profiles, engine caching, authenticated resync, tampered data, security downgrade rejection, context/time mismatch, cancellation and visible profile-error rows. An independent Linux Net-SNMP agent checks actual v3 discovery/GET/GETNEXT with every offered algorithm, plus wrong authentication/privacy credentials, unknown user and invalid context. All test agents bind only to loopback. CI renders real WinForms discovery/detail/compact and v3-profile previews with **sample** device data. Physical printer/VPN/virtual-adapter reachability and vendor-specific MIB behavior need validation on your devices.

Tagged builds prepare a draft GitHub Release with EXE/ZIP downloads, checksums and UI previews for review before publication. Release assets avoid dependence on Actions artifact-storage quota.

Protocol references: [SNMPv1 / RFC 1157](https://www.rfc-editor.org/rfc/rfc1157), [SNMP operations / RFC 3416](https://www.rfc-editor.org/rfc/rfc3416), [MIB-II / RFC 1213](https://www.rfc-editor.org/rfc/rfc1213), [Printer-MIB / RFC 3805](https://www.rfc-editor.org/rfc/rfc3805), [Windows outgoing interface selection](https://learn.microsoft.com/en-us/windows/win32/winsock/ipproto-ip-socket-options).
