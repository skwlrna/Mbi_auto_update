# Stage 9 — Actual Mobilay export compatibility gate (2026-10-18)

## Verified inputs and hard limit

The original user-supplied Mobilay v2.0.7 ZIP has 259 entries, including
`Resources\\packet_settings.json` with `TargetPort: 16000`
and `WebSocket.RelayEnabled: true`. The bundled manifest/settings files do
NOT specify a stable external event stream, authenticated endpoint, or a
JSON schema of live combat events. Static string matches for WebSocket,
named pipes, and HTTP listeners within a .NET single-file binary are **not**
proof of a supported third-party integration endpoint. No user-provided
binary was executed during this analysis.

The [Mobilay official feature overview](https://mobilay.kr/) advertises:

- post-battle snapshot save/load and web upload;
- score/location CSV export and several app-side inventory/crafting aids;
- character-info export from the debugging UI is a raw packet TXT, not a
  decoded live JSON event format.

There is currently no user-supplied battle snapshot JSON or score CSV from
which to confirm a stable schema. Consequently **do not claim a live API,
boss-emblem field, activity event ID, position coordinates or item delta**.

The MabiAuto production repo already includes read-only CLI inventory,
item, activity and altering query code. Reimplementing it from unidentified
network bytes is unnecessary and riskier than keeping existing CLI/OCR
authority.

## Implemented as an independent, offline Windows tool

`tools/MobilayExportInspector` is a .NET 8 console application with
`RunExportCheck.cmd` drag-and-drop launcher. This is **not** linked into
the game, the macro, the previous Npcap probe or any external service.

Flow:
1. A user explicitly saves a JSON/CSV using an allowed Mobilay export
   feature (if available).
2. Drag ONE local JSON/CSV onto `RunExportCheck.cmd`.
3. Type literal `YES` to authorize an offline read of that single file.
4. The app prints only fixed status codes and content-free structure counts:
   objects, arrays, scalars, occurrences of properties, CSV rows/columns,
   nesting depth.
5. It always prints `source_authenticated=false`,
   `event_schema_verified=false`, `real_time_available=false`,
   `gameplay_authorized=false`.

Limits: files up to 2 MiB; JSON depth at most 24 with at most 150,000
nodes/properties; CSV up to 200 columns, max 150,000 rows, valid UTF-8,
consistent column counts, strict quote handling. It rejects raw packet
TXT, unsupported extensions, empty, malformed, oversized, duplicate
JSON property names and unbalanced CSV. No raw file body, dynamic field
names, file path, player/account identifiers, tokens, IPs or game events
appear in stdout. No output or capture files are written. Nothing in the
game, Mobilay, or MabiAuto is modified.

The tool is a **schema feasibility probe**, not a schema identifier:
only a separately reviewed, permission-compatible real export and its
field meaning can justify building a typed reader in the future. A
legitimate offline export does not establish real-time delivery.

## Testing, packaging and release gate

```text
dotnet run --project tests/mobilay-export-inspector/Regression.csproj -c Release
dotnet publish tools/MobilayExportInspector/MobilayExportInspector.csproj -c Release -r win-x64 --self-contained true
```

The separate Windows artifact contains EXE, CMD drag-and-drop launcher
and Korean instructions. The test project uses synthetic JSON/CSV
fixtures only. Existing PacketReceiveProbe and MabiAuto production CI
remain intact. PR #231 stays draft; no deploy, autostart, live game input,
or macro main-branch changes.

### Prioritization for a future decoded source

| Candidate | Existing source | Proposed integration |
|---|---|---|
| Items and bag | Verified MabiAuto read-only CLI | Keep CLI/OCR, do not duplicate |
| Crafting and gathering status | MabiAuto CLI + UI | Fix actionable state-machine bugs first |
| Battle results snapshot | Mobilay post-battle export described on official site | Offline review only until actual JSON schema checked |
| Dungeon gimmicks / boss emblem | Existing UI/OpenCV/OCR | No Mobilay packet mapping verified; keep visual safety checks |
| Live packet event stream | **No verified external API** | Block production integration until documented/observed semantics |

This stage is implementable without additional user network tests. To go
beyond **structure only**, a real approved decoded export file or published
schema is needed. Do not ask the user to repeat packet volume tests to
answer a field-semantic question.
