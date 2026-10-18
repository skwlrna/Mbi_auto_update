# Stage 10 — Verified existing CLI coverage vs. missing external event semantics

## User data already examined

The user's Library was searched for actual decoded Mobilay combat JSON/CSV
exports. Search surfaced the static-analysis report
`mobilay_integration_review_20261018.md` and the
`Mobilay_CharacterInfo_...txt` **raw incoming hex packet** described there,
but no actual decoded post-battle JSON or CSV export sample was identified.
This is not proof no export exists on the user's PC or elsewhere in Library.

The reviewed static report records a 259-entry Mobilay v2.0.7 archive,
`Resources\\packet_settings.json` with port 16000 and relay enabled, and
localized `Export JSON` / `Save Character Info` strings. These do **not**
document a public, permitted stream of decoded gameplay events.

## Existing authoritative MabiAuto source (main)

`source/FishingAutomation/MabinogiMobileCli.cs` contains an explicit
read-only allowlist, including:

`status, capabilities, get_my_info, get_currencies, get_items,
get_activity, get_current_environment, get_alterable_items,
get_altering_works, get_gatherable_items, get_craftable_items,
get_inventory`.

`source/FishingAutomation/AlteringCliData.cs` uses
`GetAlterableItemsAsync`, `GetAlteringWorksAsync` and
`GetItemsAsync` for recipes/works/quantity. The main multi-altering form
also requires `get_my_info`, `get_currencies`, `get_gatherable_items`,
`get_activity` and `get_inventory` before multi-altering.

`source/FishingAutomation/dungeon/AbyssClearEvidencePolicy.cs`
requires BOTH clear title and a genuinely detected touch prompt on
**two consecutive** observations. A packet count or export structure
may not replace this.

## Implemented independent coverage policy

`tools/MobilayExportInspector/IntegrationCoveragePolicy.cs`
enumerates 12 fixed, audited data domains with their existing CLI
query or screen authority, plus an explicit third-party-export gate.
The offline tool supports `--coverage`, available in the ZIP as a
double-click `RunCoverageCheck.cmd`. This command opens NO file,
game connection, Npcap device or external app.

- items_inventory / activity / altering / gathering / recipes /
  environment / identity / currencies: existing read-only CLI.
- battle_snapshot: no matching production CLI query; possible
  **offline** export candidate, but a real decoded sample, provenance
  and field semantics are still missing.
- boss_emblem / abyss_room_position: screen recognition remains the
  existing authority; no network event semantics verified.
- abyss_clear: existing two-consecutive-visual-evidence authority.

The evaluator returns `CanReplaceExistingAuthority=false` and
`GameplayAuthorized=false` **for every domain**, including when
a syntactically valid JSON/CSV file has been seen. This is deliberate:
structure is not semantics and is not real-time capability.

Synthetic regressions assert the 12 domains, duplicate-free static
registry, no guessed boss packet IDs, all fail-closed outputs and
identical lack of gameplay authority whether an export passes syntax
validation or not.

## To unblock actual semantic comparison

A user-consented, **decoded** JSON/CSV export from a genuine
Mobilay post-battle snapshot or other supported export function is
required. Keep any account/character identifiers private. If such a
file is available, compare its actual fields and timestamps with
existing CLI capability and separately observed UI ground truth.
No packet ID, offset, event name or real-time interface may be
invented from this missing sample.

The user has already done comprehensive port/CLI/network tests;
do not repeat those tests merely because external export semantics
remain unknown. All changes remain on separate draft PR #231, with
no installation, macro input wiring or production main merge.
