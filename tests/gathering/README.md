# Local automatic gathering

V0.1.89 integrates GitHub V0.1.88 altering fixes and gathering. Publication authorized by the user.

Implemented: free CLI reads, strict activity/weight/catalog validation, additional inventory quantity target, tool/capacity checks, safe cancellation and confirmed stop. All CLI actions remain blocked, including execute_gathering and stop_action.

UI route: optional free recipe opening -> selected ingredient -> 구하는 방법 -> FIRST location row -> normal travel. No recommendation-label matching or Wings command. The default entry requires the user to open the selected material detail/list. The stop control combines current CLI activity with a white square in a green circle before sending Space. Fishing routes are unsupported.

Local bundle: build/publish with -p:GatheringLocalPreview=true to disable application update installation in this preview only. Normal builds retain the original updater.

Validation: 27 offline gathering checks; supplied photos verify method link, first-row selection, material identity, green stop control, and rejection of a material detail as a location list. Existing altering tests pass. Free live reads were checked without action commands. Full game UI execution and integration of newer altering fixes remain unverified.

Run: dotnet run --project tests/gathering/Regression.csproj -c Release
Free live query checks: append -- --live.
