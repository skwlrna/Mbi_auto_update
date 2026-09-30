# Local automatic gathering work

Branch: feature/automatic-gathering, based on V0.1.86. Kept separate from the other chat's altering fixes. Do not push, merge, tag, publish a release, or change automatic-update settings until the user requests it.

User requirements:
- Never consume Wings of the Goddess (정령의 날개). Other costs are permitted.
- Navigate normally through an altering recipe -> ingredient -> 구하는 방법 -> first recommended gathering spot -> travel. Gathering starts automatically on arrival.
- Select the gathered ingredient and an additional target quantity. Use free CLI reads to verify actual inventory gains, tool availability, bag capacity and safe activity.

Implemented locally:
- Strict catalog/activity/weight parsers and read-only gateway. Action commands, including execute_gathering, remain blocked at both launch boundaries.
- Additional-quantity loop, target stop verification, cancellation during gathering/travel, tool/full-bag checks, no-progress stop and preservation of the original failure when stop verification also fails.
- Searchable settings dialog with exact names including '+' variants. Fishing-only routes stop as unsupported rather than run indefinitely.
- Console regression tests and live FREE query checks. No live gathering or paid actions have been performed.

Not implemented yet:
- IGatheringScreen concrete UI route and main-window start wiring. Need actual screenshots of ingredient details (구하는 방법), recommendation list and gathering/travel stop controls to identify the exact free route and guarded stop.
- Full game end-to-end validation and integration with newer altering fixes.

Run: dotnet run --project tests/gathering/Regression.csproj -c Release
Free live reads: add -- --live. These checks never perform a CLI action or screen input.
