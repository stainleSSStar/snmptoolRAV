# Phase 2: SNMPv3

Implemented in the v1.1 release:

- Separate in-memory v3 profile: username, context, noAuthNoPriv/authNoPriv/authPriv.
- SHA-256 (default), SHA-384, SHA-512 and explicit SHA-1 legacy authentication.
- AES-128 privacy with a separate passphrase.
- Maintained SharpSNMP library, pinned to stable 12.5.7 and embedded in the portable EXE.
- Engine-ID discovery, digest/decryption validation, context/request matching, engine time checks and one bounded rediscovery/resynchronization attempt.
- No fallback to community protocols or weaker security settings.
- Windows mock-agent tests and independent Net-SNMP interoperability checks on loopback.

Remaining device validation:

- Check the owner's physical printers and other v3 agents, including VPN/virtual-adapter routes.
- Confirm each device's configured authentication/privacy algorithms and context before testing.

Possible future work (not part of this release): additional privacy protocols if required by target hardware, protected profile persistence, IPv6 discovery and vendor MIB imports.
