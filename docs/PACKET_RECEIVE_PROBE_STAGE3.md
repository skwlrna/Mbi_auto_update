# Phase 3 — Optional inbound TCP capture volume probe (2026-10-18)

## Verified evidence and limits

User-provided Mobilay v2.0.7 archive includes
`Resources\\packet_settings.json` with `TargetPort: 16000`
and `WebSocket.RelayEnabled: true`. This is **not** a
public API contract. The user's saved character-info dump has one incoming
binary payload of 629155 bytes; its fields were not decoded or authenticated.

The standalone diagnostic tests a **hypothesis**: the game's remote server
may send TCP packets from source port 16000. This has NOT been validated
on the user's PC. A zero result does not establish that the game does not use
packets, and a nonzero result does not establish that captured frames belong
to the game or contain any particular game-state event.

## Added tool

`tools/PacketReceiveProbe/` is a completely **separate, operator-invoked**
.NET 8 console project using SharpPcap 6.3.1. It is NOT a dependency of the
MabiAuto GUI, does not contain a decoder, and cannot create packet
observations. It does not call any traffic-injection API.

- No mode is run when launched without arguments.
- Listing adapters is read-only and never starts capture.
- Explicit consent on **each run**: `--allow-read-only-capture`.
- Requires an explicit adapter index and bounded duration (1..30 seconds).
- Fixed BPF capture filter: `tcp src port 16000` (incoming from that
  source port; may be wrong until independently verified).
- Counts frames and on-wire frame bytes only, with atomic counters.
  Does **not** save raw bytes, payload content, pcap files, IPs, ports,
  credentials, per-peer identifiers, or actor/game events.
- Cancels on Ctrl+C; the capture stops. A missing driver, interface or
  insufficient rights must not create a running gameplay actor.
- It does not communicate with Mobilay or authorize any gameplay action.

## Build and operator-driven validation on Windows

Use only if game policy explicitly permits passive capture. An Npcap driver
compatible with SharpPcap is required, and adapter access may require
administrator rights depending on the driver installation.

```powershell
dotnet build tools/PacketReceiveProbe/PacketReceiveProbe.csproj -c Release
dotnet run --project tools/PacketReceiveProbe/PacketReceiveProbe.csproj -- --list
# Optional, only if authorized and user explicitly agrees:
dotnet run --project tools/PacketReceiveProbe/PacketReceiveProbe.csproj -- --probe --allow-read-only-capture --device-index 0 --seconds 10
```

Do not assume device 0 is the correct adapter. Select the interface from
`--list` (e.g. the actual wired adapter); run one brief, authorized session.
Capture is never started from the GUI or during update/install.

Expected output: `read_only_probe_result matched_frames=N frame_bytes=M
status=observed|not_observed`. The probe now additionally writes
`read_only_probe_window second=S frames=F frame_bytes=B activity=A` once
per completed monotonic second and a final `read_only_probe_activity`
summary containing `active_windows`, `trailing_quiet_windows`, and
`longest_quiet_windows`. These are **network metadata only**, never
parsed packet, character or dungeon state. A quiet second is not proof
of a disconnect; frames observed are not proof of game origin.
The independent counters are intentionally kept in RAM only.

## Automated validation

```powershell
dotnet run --project tests/packet-receive-probe/Regression.csproj -c Release
dotnet build tools/PacketReceiveProbe/PacketReceiveProbe.csproj -c Release
```

CI compiles the tool and exercises option rules **without** opening adapters
or starting capture. Real Windows/Npcap detection remains untested unless a
user chooses to run the tool.

## Keep production safe

The macro's CLI, image recognition, input guards, scheduling and release
version are unchanged. The Phase 1 inbox remains disabled by default and
is **not connected** to this capture-volume probe. No packet data is
exported to GitHub. No new package dependency is added to the Windows
macro application.

**Next gate:** independently verified live source/event semantics and
permission to use that source. Only then design a typed observation
adapter; do not infer packet IDs or offsets from one hex dump.
