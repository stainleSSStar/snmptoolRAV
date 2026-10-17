Phase 2: portable SNMPv3 discovery and device-data reading.

- Separate v3 profile with username, optional context and noAuthNoPriv/authNoPriv/authPriv.
- SHA-256, SHA-384, SHA-512 and explicit SHA-1 legacy authentication; AES-128 privacy.
- Engine discovery, authenticated response checks, engine-time validation and bounded resynchronization.
- No fallback to v1/v2c or weaker security; v3 engine/profile errors are shown explicitly.
- Existing physical/virtual/VPN adapter selection, routed CIDR scans, GETNEXT reads and CSV export remain available.
- SharpSNMP is embedded: still one portable EXE with no installer, extra DLL or runtime download.
- Secrets stay in memory and out of CSV/config files. Third-party license is embedded under About and included in the ZIP.

Windows tests exercise v3 security and packaging; independent Net-SNMP tests verify all offered authentication algorithms and AES-128. Physical printer and VPN reachability still require testing on target devices. Requires Windows 10/11 with .NET Framework 4.8.
