# Read-only packet observation inbox — phase 1 (2026-10-18)

## Why this exists

The user-provided Mobilay v2.0.7 package shows packet capture-related components and
record-export functionality, but **no supported external real-time event API** has been
verified. The provided character-info TXT was one raw incoming hex dump, not a
documented schema. Its unverified numeric fields must not be interpreted as game state.

MabiAuto already queries inventory, crafting/altering jobs, activity and character
identity using its current CLI. Avoid duplicating these with inferred packet offsets.

## What is implemented

- `source/FishingAutomation/Telemetry/PacketObservationInbox.cs` is included in
  the Windows app build automatically by the .NET SDK project.
- **Disabled by default**, no configured approved adapters, and no networking,
  game hooks, capture, raw packet ingestion, disk logging, or key/mouse input.
- Intended contract for a **future, independently verified and permitted** event
  adapter. The three enum cases are normalized *hints*, **not real packet IDs**:
  `CombatActivityHint`, `TargetHealthFractionHint`, `EncounterEndedHint`.
- In an explicitly enabled diagnostic session, admission checks adapter allowlist,
  finite fractional health value (0..1), sequence ordering, plausible timestamps,
  age, expiry, and maximum 10-second validity. It stores only the most recent
  normalized hint per kind in memory, never raw payloads.
- It does **not** make trust decisions from a JSON string, and the allowlist is
  not network authentication. A real adapter must verify the source and semantic
  interpretation *before* invoking `TryObserve`.
- `ReadFresh` is for **diagnostics only**. No scheduler, abyss clear policy,
  crafting, altering, or input controller consumes it.
- `Clear()` removes diagnostic session state. The existing CLI/OCR path remains
  unchanged regardless of invalid, missing, conflicting, or expired hints.

## Tests

The standalone .NET 8 regression project links the production C# file and uses
synthetic observations only:

```powershell
dotnet run --project tests/packet-observation/Regression.csproj -c Release
```

Tests cover disabled defaults, absent/unknown adapters, null and invalid kind,
bad sequence, duplicate/reordered data, stale/future/expired observations,
validity bounds, finite 0..1 health fractions, old observation rejection,
expiry reads, and session reset. No live game, packets, or credentials needed.

The added PR workflow also builds the Windows production project with .NET 8.

## Not implemented / blockers

1. **No live Mobilay integration**: no supported event feed contract or
   authentication has been validated. Neither target port 16000 nor
   `WebSocket.RelayEnabled` may be treated as an external API.
2. No decoding of the private packet payload, and no attempt to derive
   entity positions, item counters, status flags, or protocol IDs.
3. No change to the user's installed macro or the release branch.
4. Do not use `EncounterEndedHint` or a health fraction as dungeon-complete
   evidence. The existing screen-based two-observation completion gate remains
   authoritative.
5. The session itself does not auto-enable diagnostics. A verified adapter and
   explicit user opt-in will be required before connecting any live source.

## Suggested phase 2 acceptance criteria

- Obtain an official, documented and allowed local export/event interface or
  a safe, user-authorized fixture with a specified schema.
- Implement a dedicated read-only adapter that authenticates the source,
  checks schema/version and verifies semantic mapping; add contract tests.
- Diagnostic UI can display `disabled/valid/stale/disconnected` and
  source labels without raw data or identity fields; safe fallback always.
- Do not route event hints to gameplay decisions until a separate reviewed
  change with replay-based conflict tests and existing safety guards.


## Phase 2: explicit offline fixture replay (2026-10-18)

`PacketObservationFixtureReplay` implements an **offline-only, synthetic fixture**
input contract. It is neither a Mobilay export reader nor a packet decoder.
No Mobilay live API was confirmed from the package or the product's published
features. This work does not subscribe to any host port, WebSocket, or game process.

- Diagnostics and replay must **both** be explicitly enabled by the caller.
  There is no enabled-by-default game integration or background file watch.
- The normalized adapter source is hardcoded to `fixture/offline-v1`.
  The developer-authorized inbox must separately allow this exact source.
  The fixture cannot supply its own source or authenticate a real program.
- Strict JSON Lines (one object per line), version 1, UTC ISO-8601
  timestamps ending in `Z`, positive sequence, known hint kind, optional
  `value` only for health fraction. Unknown or duplicate properties, invalid
  numbers, unknown kinds, malformed JSON or extra metadata are rejected.
- Hard bounds: 128 records, 2,048 characters/line, 65,536 characters total.
  **Entire batch parsing precedes admission**: one malformed record rejects
  the whole fixture. In an otherwise valid file, late/duplicate values are
  rejected individually by the existing inbox.
- Status output includes **only** counts and one of `disabled`,
  `invalid_or_oversized_fixture`, `invalid_fixture_schema`,
  `fixture_unreadable`, `empty_fixture`, or `replayed`.
  No raw payloads or private IDs are logged.
- It accepts a caller-provided `TextReader` rather than scanning folders
  or reaching into Mobilay's private storage.

Minimal **synthetic example** (not a real Mobilay record):

```jsonl
{"schemaVersion":1,"kind":"combat_activity","sequence":1,"observedAtUtc":"2026-10-18T04:00:00.0000000Z","expiresAtUtc":"2026-10-18T04:00:03.0000000Z"}
{"schemaVersion":1,"kind":"target_health_fraction","sequence":1,"observedAtUtc":"2026-10-18T04:00:00.0000000Z","expiresAtUtc":"2026-10-18T04:00:03.0000000Z","value":0.25}
```

A fixture should be replayed with a matching **explicit test clock**, because
the inbox's 5-second staleness rules are intentionally strict. Historical
fixtures are not live gameplay evidence.

Test command (synthetic data, no game):

```powershell
dotnet run --project tests/packet-observation/Regression.csproj -c Release
```

### What still blocks actual real-time integration

A written/permitted external event API contract (or verified exported data
schema) is still missing, so this phase does **not** establish a real-time
data connection to Mobilay. The existing CLI/OCR, F10 stop, input ownership,
2-observation Abyss-clear evidence and recovery policies stay unchanged.
A future real adapter must prove source authentication, field semantics and
game policy compliance, and it must be tested before enabling any runtime
observation, much less game actions.
