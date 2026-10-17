# Next phase: SNMPv3

Requested by the repository owner after the initial v1/v2c scanner specification. SNMPv3 is not implemented in v1.0.

Add a separate SNMPv3 profile, preserving the adapter dropdown, subnet controls and read-only discovery/data workflows:

- Username, context name and security level: noAuthNoPriv, authNoPriv or authPriv.
- Authentication protocol/password and privacy protocol/password shown only when needed.
- Implement agent engine-ID discovery, authenticated message checks, engine boots/time synchronization and time-window errors using a maintained SNMPv3 implementation.
- Verify supported SHA/AES algorithms against that implementation and real target devices before offering them in the UI.
- Do not silently fall back from a v3 profile to community-based versions or weaker authentication/privacy settings.
- Keep secrets in memory by default and out of exports/logs. Add profile persistence only with an explicit protected-storage design.
- Test discovery, reads, authentication/privacy failures, invalid credentials, engine time resynchronization, cancellation and virtual-adapter source selection against local v3 agents; then validate on the owner's devices.

Keep the app portable. Reassess packaging if a maintained protocol library requires a newer self-contained .NET runtime rather than the current framework-only EXE.
