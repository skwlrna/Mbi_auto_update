# Automatic altering verification

Run `dotnet run --project tests/altering/Regression.csproj -c Release` for quantity, seven-slot queue, receipt, cancellation, existing-queue draining, extra-production targets and bounded paid-click checks.

The visual harness takes a user-provided screenshot path, client crop offsets, text and ROI kind. It never opens the game or sends input. Screenshot files must stay outside this repository.

Example: `dotnet run --project tests/altering-visual/Regression.csproj -c Release -- <image> 20 14 "가공하러 가기" button` verifies both the label and adjacent cost 5. ROI kinds: header, cards, popup, button, collect. The supplied steel title is read as 강철과 by Windows OCR; the application accepts this single final-glyph alias only when the live CLI catalog has no conflicting name. Other substitutions are allowed only for selecting a free list-card candidate and never for authorizing a paid button.

Live recipe/queue reads: `dotnet run --project tests/cli-readonly/Regression.csproj -c Release -- --live`. These six reads never launch a CLI action.

Live end-to-end queueing and collection have not been validated. All six facilities use the user-confirmed common screen layout; recipes or duplicate variants that cannot be verified stop without a paid click.

Use visual harness ROI kind `facilities` with a hub screenshot to verify all six large card titles resolve uniquely. It uses the same production recognizer and excludes repeated facility/level badges. The photos remain outside this repository.
