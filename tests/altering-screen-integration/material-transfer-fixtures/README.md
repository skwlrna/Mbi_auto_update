# Material transfer incident fixture

`unchecked-20261026-115823.png` is the unmodified 800 x 1000 game capture from
`skwlrna/Mbi_auto_log`, at:

`runtime-errors/2026-10-26/20261026_115823_510_다중가공_오류_81ead6/screen.png`

Source Git blob: `85e34bafeb06b591135fc08994efd5fef582985a`.
The incident was recorded on V3.2.64 at 2026-10-26 11:58:23 KST.

The original grey checkbox is unselected. Tests generate the green and changed
grey states in memory; those are synthetic fixtures, not observed game frames.

Every L2 run executes the production transfer control flow with recorded captures
and a click recorder. The dedicated `Material transfer incident OCR and behavior`
workflow installs Korean Windows OCR and additionally executes the production
prompt recognizer against this image and text-masked negative cases. Missing
Korean OCR fails that workflow rather than falling back to another language.

These tests do not attach to a game window, load an input driver, send clicks,
verify the server's weekly suppression setting, or prove actual in-game completion.
