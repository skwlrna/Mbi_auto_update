# Stage 11 — MabiAuto cross-feature common read-only observation engine

## What is now implemented (not deployed)

The shared diagnostic library lives in
`source/FishingAutomation/Telemetry/CommonObservationEngine.cs`.
It is compiled by the main Windows project but **never instantiated or
subscribed to any production input/UI/gameplay paths** in this draft.
Existing CLI, OCR, production state machine, and abyss completion checks are
unchanged.

`CommonObservationEngine` is disabled by default, in-memory only, and
requires caller opt-in **plus** an explicit allowlist of source enum values.
It does not open a device, capture packets, query a CLI, subscribe to Mobilay,
or press game keys.

| Source | What it can report | Evidence policy |
|---|---|---|
| `ReadOnlyCli` | explicit typed connection/activity/item, gathering/altering/crafting diagnostic states | requires normalized, separately validated CLI output; **no adapter connected** |
| `VerifiedScreen` | explicit typed visual diagnostic states for supported modes | requires separate screen ground truth; **no adapter connected** |
| `NetworkMetadata` | presence/absence of received TCP frames | **only** `TrafficObserved` / `NoFrames`; cannot assert `Disconnected`, position, completion, reward |
| `MobilayOfflineExport` | `HistoricalSnapshotPresent` for combat | **historical/nonsemantic**; never a real-time vote |
| `LocalScheduler` | local scheduler running/completed/failed diagnostic status | local application status only, not server/game authority |

A bounded map keyed by `(Domain, Source)` stores only the latest typed
observation (max 50 distinct combinations). It preserves per-source and
per-domain ordering watermarks, rejects duplicate/reordered sequences,
unexpected source/domain/state combinations, future events, stale reports,
invalid validity windows and level spoofing, and clears on explicit reset.
Live observations have 5-second maximum age and 10-second maximum TTL.
Historical exports have a bounded, longer retention window but **cannot
participate in live-state comparisons**.

`Evaluate` returns only the following diagnostic codes and counts:
`disabled`, `unknown_domain`, `no_fresh_observation`,
`nonsemantic_only`, `single_source_only`,
`independent_sources_agree`, and `conflicting_sources`.
Independent agreement requires two separately sourced, semantically valid
live claims of **the same** state. Conflict produces **no state**.
Network metadata and offline export count as supporting source records but
**never cast votes**.

Every result hardcodes `CanReplaceExistingAuthority=false` and
`AuthorizesGameplay=false`. These types do not contain action identifiers,
game inputs, packet IDs, private labels, network addresses, player names, or
raw JSON content. This engine is **not** a new live event stream.

## Observed Mobilay DPS v4 shape validator

`MobilayDpsSummaryValidator` in the same Telemetry directory performs a
bounded **offline** UTF-8 JSON shape check reflecting the user's example:
`Version:4`, `SavedAt`, `EventCount`, `RetainedEventCount`,
`DroppedEventCount`, `BattleSeconds`, `TotalDamage`, and the strictly
increasing `CompactData.s[].s` second indices. Input is limited to 2 MiB,
strict JSON, max depth 24, and 24-hour maximum bucket index; malformed,
unsupported, oversized and duplicate root/CompactData fields fail closed.
The result contains only counts and a fixed diagnostic status.

It does **not** treat a count of 824 events as 824 replayable raw events,
inspect skill or buff identifiers, authenticate the export's origin, decode
`CharacterInfo` binary, assume a documented Mobilay API, or expose names,
party membership or unique user identifiers. The offline file's JSON
timestamp is historical; reading the file does not generate a live claim.

## Cross-feature intention, actual integration status

| Feature | Initial safe diagnostic use | Production state change |
|---|---|---|
| AI error diagnosis and scheduler | correlate bounded statuses, report stale/conflict instead of guessing | **not wired** |
| Gathering / multi-gathering | later compare validated `get_activity`/item snapshots with actual screen evidence | **not wired** |
| Altering / multi-altering | later distinguish local stage vs server work state, avoid treating absent traffic as completion | **not wired** |
| Crafting / inventory / loot | later audit CLI quantity baselines without changing verified receipt gates | **not wired** |
| Combat / regular dungeon / Abyss | inspect offline historical DPS summaries separately from visual clear evidence | **not wired** |
| Fishing | potential screen-derived diagnostics only; no verified Mobilay signal currently | **not wired** |

Do not overstate the coverage table: existence of an enum or an optional
normalized source does **not** mean the source has been connected or its
values verified on the user PC.

## Validation

```text
dotnet run --project tests/common-observation/Regression.csproj -c Release
```

Only synthetic fixtures are committed. Tests verify fail-closed default,
source allowlist, impossible provenance/semantic combinations, independent
agreement vs conflict, short TTL, replay guard, historical nonsemantic
records, negative/oversize/duplicate/malformed v4 JSON and privacy-safe
summaries. Existing packet and export safety regressions run independently.

No private user artifacts are uploaded to GitHub. Draft PR #231 only,
no production merge, no live capture, no persistent monitor, no game inputs.
The next work phase after this foundation is **separately validated
read-only source adapters**, starting with available MabiAuto CLI status
envelopes and scheduler diagnostics; adapters must preserve the original
UI/CLI authorities.
