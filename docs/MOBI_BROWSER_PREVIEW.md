# MOBI browser control — isolated preview

Default off unless the existing V3.3.42 MABIAUTO_BROWSER_TOKEN environment variable is explicitly configured. PC Settings > Browser remote control enables the fixed loopback endpoint
`http://127.0.0.1:18765/` and generates a fresh 256-bit key. Enter the key in
the browser; it is not stored in browser storage or placed in a URL. Turning
control off or closing the PC application ends the active session. A configured environment-variable key can be reused on a future PC app launch; remove that variable to disable startup activation.

The preview supports existing status, queue list, total/additional processing
orders, queue start/stop/cancel, global stop, and selecting an allowed Abyss
destination followed by the existing F9 preflight. Abyss uses existing PC
execution behavior (continuous repetition until stopped); repeat-count configuration is not implemented.

For a phone, a separately configured private HTTPS proxy (with access restricted
to the user's own devices, loopback upstream, and host rewritten to
127.0.0.1:18765) is required. Never publicly forward the HTTP port. No proxy,
VPN, firewall change, public URL or automatic service installation is performed
by this change. Windows HTTP URL permission may need local administrator
configuration; the UI reports startup failure rather than silently broadening
the listener.

Validation: command policy tests, real loopback HTTP requests with a fake dispatcher, and static safety contracts.
Actual phone/private HTTPS connection and Windows listener permissions require
an isolated real-device test before production deployment.

## Private phone setup (manual, not installed by MabiAuto)

One candidate is Tailscale Serve. Install Tailscale on Windows and the phone,
join the same private tailnet, restrict access to your own authorized devices,
and enable HTTPS certificates if prompted. Verify there is no public Funnel
or conflicting Serve configuration before proceeding. In a Windows admin
terminal, start a temporary foreground proxy:

```powershell
tailscale serve 18765
```

Open the private HTTPS URL printed by that command on the phone while its
Tailscale connection is active. Enter the separately generated MabiAuto key.
First verify status only; do not send gameplay commands during setup testing.
Stop the foreground proxy with Ctrl+C and disable browser control in MabiAuto
when finished. Do not use Funnel, public port forwarding, or disable access
controls. Actual proxy interoperability is not yet verified on a real PC/phone.

Official references:
- https://tailscale.com/docs/features/tailscale-serve
- https://tailscale.com/docs/reference/tailscale-cli/serve
