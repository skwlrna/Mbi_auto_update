# MOBI browser control — isolated preview

Default off. PC Settings > Browser remote control enables the fixed loopback endpoint
`http://127.0.0.1:18765/` and generates a fresh 256-bit key. Enter the key in
the browser; it is not stored in browser storage or placed in a URL. Turning
control off or closing the PC application revokes it.

The preview supports existing status, queue list, total/additional processing
orders, queue start/stop/cancel, global stop, and selecting an allowed Abyss
destination followed by the existing F9 preflight. Abyss uses existing PC
settings; separate repeat-count configuration is not implemented.

For a phone, a separately configured private HTTPS proxy (with access restricted
to the user's own devices, loopback upstream, and host rewritten to
127.0.0.1:18765) is required. Never publicly forward the HTTP port. No proxy,
VPN, firewall change, public URL or automatic service installation is performed
by this change. Windows HTTP URL permission may need local administrator
configuration; the UI reports startup failure rather than silently broadening
the listener.

Validation: command policy tests with fake CLI and static safety contracts.
Actual phone/private HTTPS connection and Windows listener permissions require
an isolated real-device test before production deployment.
