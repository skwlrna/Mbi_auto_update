# Stage 4: Windows IPv4 TCP port ownership cross-check

## Evidence vs. hypothesis

On the user's Realtek adapter the independently authorized receive-volume
probe counted 0 frames before game login, then 45 frames / 8888 bytes in
10 seconds after login. A subsequent 10-second session counted 109 frames /
13039 bytes (9 active one-second windows). These are user screenshots,
not authenticated attribution. They justify investigating the local socket
owner, but they do **not** establish packet ownership, data semantics, or a
Mobilay public API.

## Independent diagnostic changes

After a user explicitly consents to the 1–30-second probe, the standalone
Windows executable now reads an IPv4 TCP owner-PID table at start and end
using Windows `GetExtendedTcpTable` with `TCP_TABLE_OWNER_PID_ALL`.
It only considers TCP rows with remote port **16000** (the fixed
capture source port), and maps the OS PIDs to process basenames *in memory*.
No IP addresses, ports other than the fixed filter, PIDs, process paths,
command lines, credentials, raw packet data or account information are saved
or printed. Up to eight short sanitized process basenames are printed to the
local operator window. Data are not submitted to GitHub or MabiAuto.

Outputs:

```text
read_only_tcp_owner_before status=... remote_port_sockets=N owner_names=... ipv4_only=true
read_only_probe_window second=... frames=N frame_bytes=N activity=...
read_only_probe_result matched_frames=N frame_bytes=N status=...
read_only_probe_activity ...
read_only_tcp_owner_after status=... remote_port_sockets=N owner_names=... ipv4_only=true
```

Statuses:
- `port_matched_owner_candidates`: one or more *OS IPv4 TCP
  connections* with remote port 16000 had a process owner.
- `no_ipv4_port_match`: the Windows table contains no active IPv4
  TCP remote-port-16000 rows at the moment.
- `os_tcp_table_unavailable`: platform/API/query could not provide a
  trustworthy snapshot; don't infer zero connections.

**Important limits:** the interface packet capture and OS socket snapshots
are independent observations. They are NOT joined by 5-tuple or IP address;
matching a remote port alone does not prove a captured frame belongs to a
specific socket, process, or game. IPv6 connections are not covered.
Processes can exit between TCP-table and owner-name sampling; process
names are unauthenticated and may be ambiguous. A zero count does not
prove the game disconnected or that packets are not present.

No native library patching, unknown packet ID decoding, outgoing traffic,
game input, automated decisions, CLI actions, or continuous background
monitoring are added. Existing CLI/OCR and abyss safety gates remain
authoritative. CLI status comparison is **not yet implemented**;
its independent semantics and privacy-safe opt-in must be specified
before implementation.

## Validation

`tests/packet-receive-probe` includes synthetic examples with:
multiple owners on the same remote port, repeated owner names,
unrelated remote ports, names requiring sanitization, unavailable
results, and invalid ports. Tests do not query the actual game TCP
table or capture any live traffic.

Build and GUI launcher workflow remains:

```text
RunReceiveProbe.cmd
  -> PacketReceiveProbe.exe --interactive
  -> adapter index / duration / exact YES consent
  -> bounded capture and local diagnostic results
```

Development PR #231 remains draft; no auto-merge or production
release. An actual client-side test with the newly built ZIP is
still required to observe the real process names.
