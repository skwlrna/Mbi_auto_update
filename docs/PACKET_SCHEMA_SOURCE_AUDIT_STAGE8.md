# Stage 8: Source audit + opt-in redacted result export (2026-10-18)

## What the supplied Mobilay archive **actually** supports

The user-provided `mobilay.zip` contains 259 entries. Direct read-only
inspection of its packaged file list and JSON (not executing code) confirmed:

- `Resources\\packet_settings.json` includes `TargetPort: 16000`
  and `WebSocket.RelayEnabled: true`.
- Of the structured/documentation-style files in the archive there are
  `Resources\\homework_barter_items.json`,
  `Resources\\packet_settings.json`,
  `lay-package.json`, and two MIDI lookup TXT resources.
- There is **no standalone OpenAPI/proto/JSONL event-schema document or
  publicly documented external event endpoint in these packaged files**.
  This DOES NOT prove no private interface exists elsewhere inside the binary.
- The prior `Mobilay_CharacterInfo_...` record is a verified single incoming
  hex-encoded binary dump, **not** a decoded stable JSON event schema.
- Existing read-only CLI/OCR fields are available within MabiAuto and remain
  the authoritative sources for supported inventory/activity decisions.

Therefore a TCP socket matching a process named MabinogiMobile, a frame
count, or the WebSocket relay setting is **not sufficient evidence** to map
unknown bytes to character locations, boss emblems or gimmick events.

**Next real gate** for semantic mapping: obtain an allowed, documented
normalized live event feed, or a real decoded **export file** with its schema
and clear user consent. Do not invent packet IDs or offsets. If an authorized
export is supplied, inspect it in a separate offline reader first, with
strict provenance, source-identity hygiene and reproducible test fixtures.

## Quality-of-life improvement: save exactly the redacted 3-line report

After a completed, explicitly approved `RunAllChecks.cmd` three-stage
session, the tool optionally asks for **literal `SAVE`**. No other
text triggers a file write. If approved, the report is written in the
extracted diagnostic executable directory with basename
`PacketDiagnostic_Summary.txt`, or a non-overwriting `_001`, `_002`
suffix. This is NEVER a raw packet log, a pcap file, a network flow table
export or a CLI JSON transcript.

`SanitizedSummaryReport` constructs the file from typed, fixed-schema
records only. It accepts exactly the three standard stages in the required
order, nonnegative consistent frame counts and fixed allowlists for
flow/CLI/evidence states. It never accepts arbitrary user/process names,
endpoint addresses, account numbers, character IDs, raw CLI text, packet
payloads or stack-trace strings. Unknown statuses and extra or missing stages
cause rejection. Disk writes are not attempted without explicit SAVE; an
existing report is not overwritten.

The on-screen three-line comparison still works without saving anything.
An operator who missed a screenshot may save and share just this redacted
summary without repeating or exposing packet captures. A real user-machine
run of this updated optional output remains unverified.

No main-branch merge, release, macro modification, gameplay inputs or
semantic packet decoder were introduced. PR #231 remains draft.
