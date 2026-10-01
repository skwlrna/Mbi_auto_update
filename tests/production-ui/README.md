# Inline production controls regression

Run on Windows: `dotnet run --project tests/production-ui/Regression.csproj -c Release`.
Pass an optional output directory to render native WinForms preview images.

The harness opens the real MainForm with injected CLI responses. No game CLI actions
or external services are invoked. It clicks the actual sidebar and start/stop buttons,
sets the actual dropdowns and NumericUpDown controls, and verifies the resulting plans
and encoded commands. Coverage includes one-click navigation from home, exclusive
pages, retained selections, unavailable tools/recipes, first-row duplicate recipes,
CLI failures/recovery, cancellation, completion and two window sizes.

Existing automation and CLI regression suites remain independent and unchanged.
