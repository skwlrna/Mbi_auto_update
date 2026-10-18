# Stage 6 — IPv4 TCP 4-tuple evidence + CLI response classification

## Source evidence (user's completed one-run diagnostic)

- `client_closed`: 0 inbound port-16000 frames; CLI status/activity timed out.
- `character_connected_idle`: 344 inbound frames; Windows port-owner
  candidate `MabinogiMobile`; both CLI queries produced parseable JSON but the
  earlier diagnostic incorrectly required a root `status` string.
- `character_connected_activity`: 279 inbound frames; Windows owner
  candidate `MabinogiMobile`; the same JSON classification limitation.

Frame counts are correlated with the observed game state, but cannot establish
that individual frames belong to the game. Idle and moving states are not
separable from volume alone.

## New phase: read-only in-memory network endpoint correlation

`TcpIpv4FrameParser` extracts only Ethernet II (including up to two VLAN
tags), IPv4 and TCP *headers*. It rejects truncated, fragmented, non-TCP,
non-IPv4 and non-Ethernet frames rather than guessing. It extracts the
**remote/source IP+port** and **local/destination IP+port** from received TCP
headers. It does not touch or store application payloads.

`TcpOsFlowSnapshot` separately samples Windows `GetExtendedTcpTable` IPv4
TCP 4-tuples with remote port 16000 at each stage's start and end. A bounded
`TcpFlowCorrelation` matches these to the observed frame 4-tuples. Only
counts and coarse result codes are shown:

- `ipv4_tcp_4tuple_matched` = at least one captured TCP frame has the same
  remote and local address+port pair as a Windows table snapshot.
- `no_ipv4_tcp_4tuple_match` = parseable frames did not match a sampled OS
  connection; table timing, short-lived connections or link-layer type can
  explain absence.
- `os_flow_table_unavailable` = neither OS snapshot was available.
- `matched_frames`, `unmatched_frames`, `unparsed_frames`,
  `capacity_drops` are counts only, *not* decoded game events.

IP addresses and ephemeral ports exist only as temporary in-memory numeric
fields for set comparison and are never printed, written to disk, sent to
GitHub or added to the macro. Packet contents are never saved. Distinct
flow storage is bounded to 128 records. Only IPv4 Ethernet TCP traffic can be
matched; IPv6, other network interfaces and varying link-layer formats are
explicitly unsupported. Stage-boundary samples cannot attest an OS socket
owner at the exact timestamp of an individual frame. The 4-tuple match is
stronger than matching a remote port but **still not cryptographic or
protocol-level authentication**.

## Existing CLI contract correction

The user screenshot reported `cli_json_without_status` for both status
and get_activity queries, despite parseable JSON. The repository's
`source/FishingAutomation/MabinogiMobileCli.cs` uses more nuanced rules:

- top-level `status` is optional
- nested `body` may be an object or a JSON string
- `pipe` can appear inside `body` or at the root
- `status`, `reason` and `error` can appear at either location
- `status` query expects `pipe=connected`; `get_activity` expects
  an object response body after handling errors.

The diagnostic's `ReadOnlyCliComparison` now matches these semantic
rules without printing the actual JSON, character details, inventory,
authentication material, standard error, or arbitrary labels. Only
read-only `status` and `get_activity` CLI invocations are allowed.

This code correction was validated on synthetic fixture data modeled after
the production parser, **not on a fresh live CLI response**.

## Testing and deployment

- `dotnet run --project tests/packet-receive-probe/Regression.csproj -c Release`
- `dotnet build tools/PacketReceiveProbe/PacketReceiveProbe.csproj -c Release`
- GitHub Actions Windows x64 publish creates the same independent
  `RunAllChecks.cmd` ZIP with a 3-stage consent-guided diagnostic.

No release, deployment, modification of the installed macro, packet-driven
game input, bypass of abyss clear evidence, or use of private packet IDs is
performed. PR #231 remains draft.
