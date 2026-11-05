# Herb row incident regression

Run from repository root:

`dotnet run --project tests/herb-row/Regression.csproj -c Release`

Requires Windows Korean OCR. Does not capture the desktop or send game input.
CI runs this after installing the Korean OCR capability.

Private local incident input: `skwlrna/Mbi_auto_log` main,
`runtime-errors/2026-11-05/20261105_211837_017_다중가공_오류_34d758/screen.png`.
Set `MBI_HERB_INCIDENT_IMAGE` to a local copy to reproduce the incident.
The private original screenshot is not republished in this repository.
Without that environment variable, CI uses a generated synthetic list and
runs the same positive/negative checks for its bottom row.

On the local Korean OCR engine the original full-list dim-text matcher returns
zero exact matches. Native-color 2x OCR reads 산뜻 버섯 at (265,929,75,18).
The isolated row exact matcher returns (265,922,74,21). The original icon
predicate also passes this row: the reproduced failure occurs before the icon
test. The narrower icon sampling is a safety improvement, not a claim that the
incident's icon failed.

Tests exercise compiled production recognition, the visible icons, two reads,
missing same-row icon with neighbor intact, missing name with icon intact,
suffix mismatch, duplicate exact names and cancellation. The existing L2
integration suite retains the two-capture OCR/icon/stable-geometry click gate.

No version, release workflow, update manifest, or release authorization changes.
