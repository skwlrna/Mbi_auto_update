# Stage 7 — Fail-closed diagnostic evidence gate (2026-10-18)

## Input evidence, not decoded game events

The user has already completed a combined three-scenario read-only diagnostic:
game closed, character idle, and manually moving character. The observations
established differences in inbound traffic and the OS's TCP port-16000 owner
candidate. They did **not** reveal a packet/event schema, authenticated
source, boss/gimmick state, position or inventory counter.

This phase prevents "packets observed" from being confused with "game event
decoded." No user-side rerun is necessary to validate these logic rules;
unit tests use only synthetic diagnostic metadata.

## Added to standalone diagnostic only

`tools/PacketReceiveProbe/TransportEvidenceAssessment.cs` combines:

1. independently collected read-only CLI **status** category
2. per-stage captured TCP frame count and OS 4-tuple frames matching a TCP
   socket owned by an OS process whose *basename* is `MabinogiMobile`
3. whether the Windows socket table was readable and whether correlation
   counters overflowed

These inputs are still unauthenticated and nonsemantic. Classification:

| Result | Meaning / caution |
| --- | --- |
| `independent_sources_agree` | CLI status connected **and** at least one flow from an OS process-name candidate matched. Source corroboration only. |
| `cli_only` | CLI reported connected but no candidate flow matched. Not proof of disconnection. |
| `network_candidate_only` | Named socket/frame candidate seen but CLI couldn't independently confirm connection. |
| `conflicting_signals` | Named socket/frame candidate while CLI said disconnected/game-or-option-off. Investigate timing, process changes, or wrong source. |
| `no_observation` | No frames. This is not proof the game is off. |
| `unattributed_traffic` | Frames seen but not matched to named candidate. |
| `unavailable` | OS socket snapshots unavailable; fail closed. |
| `incomplete_evidence` | Correlation capacity overflow; fail closed. |
| `invalid_evidence` | Impossible/negative counts; fail closed. |

**Hard invariants for every result:**
`PacketMeaningVerified=false` and `AuthorizesGameplay=false`.
The evaluator cannot alter the macro's input controller, image recognition,
CLI query logic or abyss-clear gates. It does not decode packet bodies,
fabricate packet IDs, inject network traffic or write private data.

In guided mode each stage emits a small `stage_evidence` line and appends
`evidence=<category>` to the final three-line comparison for one screenshot.
There are no new prompts, network ports, privileges, dependencies or
automatic captures.

## Gates before any real-game-state integration

1. Obtain a permitted, documented source of normalized events or verify an
   actual export schema from Mobilay. `TargetPort=16000` and a successful
   TCP socket match do **not** constitute such a schema.
2. Map a specific event against at least two separately observed ground-truth
   transitions (CLI/UI), with timestamps and explicit event semantics.
3. Keep event observation read-only, allowlist the adapter, enforce short TTL,
   sequence/source validation, and decline missing/conflicting evidence.
4. Only after semantic validation may the user consider a distinct reviewed
   consumer change. That consumer must preserve existing CLI/OCR authority,
   stop key, input ownership and abyss completion safeguards.

The user should not repeatedly capture or disclose raw packet samples while
the basic semantic source is still unverified.
