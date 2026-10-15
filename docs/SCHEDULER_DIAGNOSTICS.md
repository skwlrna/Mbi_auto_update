# MabiAuto scheduler diagnostics — Phase 1

The scheduler is the current development priority. No water-path changes. This
adds three opt-in, local-only diagnostic utilities without changing scheduler
input, F10 stop, checkpoints, AI Autopilot, GitHub upload or release behavior.

## Prerequisites

Python 3.10+ (existing Autopilot installation). Optional vendor executables:
- Microsoft Sysinternals DbgViewCLI v5.02+ (dbgviewcli.exe)
- Microsoft Sysinternals ProcDump (procdump.exe)
- ImageMagick 7 (magick.exe)

These utilities are NOT downloaded, installed, registered as services or
launched automatically. They are located on PATH, or through the absolute
paths in MABIAUTO_DBGVIEWCLI, MABIAUTO_PROCDUMP, MABIAUTO_MAGICK.

All commands below use:
python -B source/FishingAutomation/ai-autopilot/scheduler_diagnostics.py

Append one of these actions:

- doctor
  Reports which utilities can be found; does not capture anything.
- capture --pid 1234 --seconds 60
  Requires MabiAuto to be running and PID to be its own Windows process ID.
  Enables Win32, not kernel capture. PID filtered, max 2000 lines, tail 300,
  and duration bounded to 5-600 seconds. Refuses another active DbgViewCLI.
- dump --pid 1234 --allow-sensitive-dump
  One on-demand mini dump, no persistent crash monitor. ProcDump may
  pause the application. Mini dumps may contain private data and tokens;
  inspect and redact before sharing. The explicit flag is mandatory.
- compare --baseline normal.png --observed failed.png
  Local-only ImageMagick RMSE comparison with difference.png artifact.
  Local images must be PNG/JPG/BMP under 16 MiB each; symlinks rejected.
  ImageMagick exit code 1 is treated as a valid image difference only if
  it created the output file.

To emit MabiAuto scheduler-specific OutputDebugString events for capture, start
MabiAuto with MABIAUTO_SCHEDULER_DEBUG=1 in its environment. The existing
AppLog file/UI behavior is unchanged and this opt-in is OFF by default.

Diagnostic artifacts live at:
%LOCALAPPDATA%\MabiAuto\scheduler-diagnostics\<UTC timestamp>_<tool>
Optional --output "C:\folder" must precede the action subcommand.

No automatic upload, analysis, merge, deployment, game input or restart.
Closing MabiAuto and F10 preserve their current semantics; bounded external
captures are independent, one-shot commands and are not a service.
The operator must manage local evidence retention and review sensitive contents.

Offline tests:
python -m unittest discover -s tests/ai-autopilot -p test_scheduler_diagnostics.py -v

Windows build and scheduler regressions are still required to validate the
actual C# OutputDebugString bridge. Python mocks do not prove live operation.
