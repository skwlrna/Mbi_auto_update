using System.Drawing;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

try
{
    // This assembly is built from the actual application project. Do not link a
    // simplified screen file or replace screen implementation with FakeWorld.
    Assembly production = Assembly.Load("FishingAutomation");
    Type screen = production.GetType("DungeonVisionBot.AlteringScreen", throwOnError: true)!;
    Type directiveType = production.GetType("FishingAutomation.AlteringFacilityEntryDirective", true)!;
    Type entryPolicy = production.GetType("FishingAutomation.AlteringFacilityEntryPolicy", true)!;
    Type receiptPolicy = production.GetType("FishingAutomation.AlteringReceiptPolicy", true)!;
    Type cachePolicy = production.GetType("FishingAutomation.AlteringScreenOnsiteCachePolicy", true)!;
    Type remotePolicy = production.GetType("FishingAutomation.AlteringRemoteProcessGuard", true)!;
    Type identityPolicy = production.GetType("FishingAutomation.AlteringRecipeIdentityPolicy", true)!;
    Type travelPolicy = production.GetType("FishingAutomation.AlteringFacilityTravelConfirmPolicy", true)!;
    const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                             BindingFlags.Instance | BindingFlags.Static;
    object automatic = Enum.Parse(directiveType, "Automatic");
    object fresh = Enum.Parse(directiveType, "FreshMoveRequired");
    object reuse = Enum.Parse(directiveType, "ReuseCoordinatorConfirmedOnsite");

    int checks = 0;
    void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("L2 FAILED: " + label);
        checks++;
        Console.WriteLine("PASS L2: " + label);
    }

    MethodInfo Method(Type owner, string name, int parameters)
    {
        var matches = owner.GetMethods(all).Where(m =>
            m.Name == name && m.GetParameters().Length == parameters).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Missing/ambiguous real method {owner.Name}.{name}/{parameters}: {matches.Length}");
        return matches[0];
    }

    bool CallBoolean(Type owner, string name, params object?[] parameters) =>
        (bool)(Method(owner, name, parameters.Length).Invoke(null, parameters)
            ?? throw new InvalidOperationException("Unexpected null boolean"));

    string? CallCache(string name, params object?[] parameters) =>
        (string?)Method(cachePolicy, name, parameters.Length).Invoke(null, parameters);

    // Inspect compiled IL of the REAL async state machines. Merely recording a
    // manager directive in a fake screen does not prove it reaches the actual
    // fixed-card, medicine, cached recipe, detail or receipt implementations.
    static IReadOnlyList<string> Calls(MethodInfo method)
    {
        MethodInfo bodyMethod = method.GetCustomAttribute<AsyncStateMachineAttribute>()?
            .StateMachineType.GetMethod("MoveNext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? method;
        byte[] il = bodyMethod.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException("No IL for " + method.Name);
        var operations = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!)
            .GroupBy(op => unchecked((ushort)op.Value))
            .ToDictionary(g => g.Key, g => g.First());
        var calls = new List<string>();
        int offset = 0;
        while (offset < il.Length)
        {
            ushort code = il[offset++];
            if (code == 0xfe) code = (ushort)(0xfe00 | il[offset++]);
            if (!operations.TryGetValue(code, out OpCode op))
                throw new InvalidOperationException($"Unknown IL opcode 0x{code:X4}");
            int operand = offset;
            int bytes = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                    OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineBrTarget or OperandType.InlineField or
                    OperandType.InlineI or OperandType.InlineMethod or
                    OperandType.InlineSig or OperandType.InlineString or
                    OperandType.InlineTok or OperandType.InlineType or
                    OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException("Unhandled operand " + op.OperandType)
            };
            if (offset + bytes > il.Length)
                throw new InvalidOperationException("Truncated production IL");
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
            {
                try
                {
                    int token = BitConverter.ToInt32(il, operand);
                    var target = bodyMethod.Module.ResolveMethod(token,
                        bodyMethod.DeclaringType?.GetGenericArguments(), bodyMethod.GetGenericArguments());
                    if (target != null)
                        calls.Add((target.DeclaringType?.Name ?? "") + "." + target.Name);
                }
                catch (ArgumentException)
                {
                    // Generic framework method tokens need not be resolved by
                    // this guard; all required production call targets are non-generic.
                }
            }
            offset += bytes;
        }
        return calls;
    }

    IReadOnlyList<string> ScreenCalls(string name, int parameters) =>
        Calls(Method(screen, name, parameters));

    bool Has(IReadOnlyList<string> calls, string target) =>
        calls.Any(s => s.EndsWith("." + target, StringComparison.Ordinal));
    int Count(IReadOnlyList<string> calls, string target) =>
        calls.Count(s => s.EndsWith("." + target, StringComparison.Ordinal));

    Check(screen.GetInterfaces().Any(t =>
        t.FullName == "FishingAutomation.IAlteringCoordinatorQueueScreen") &&
        screen.GetInterfaces().Any(t =>
            t.FullName == "FishingAutomation.IAlteringCoordinatorReceiptScreen"),
        "real application screen implements both manager-directed interfaces");

    var selection = ScreenCalls("SelectRecipeAsync", 3);
    Check(Has(selection, "TrySelectFixedRecipeAsync") &&
          Has(selection, "TrySelectMedicineRecipeBySearchAsync") &&
          Has(selection, "ShouldVetoRecipeMoveButton"),
        "real recipe dispatcher reaches fixed cards, medicine search, cached visual veto");
    Check(!Has(selection, "TravelToFacilityAsync"),
        "recipe dispatcher cannot autonomously issue facility travel");

    var fixedRecipe = ScreenCalls("TrySelectFixedRecipeAsync", 3);
    Check(Count(fixedRecipe, "ShouldVetoRecipeMoveButton") >= 2 &&
          Has(fixedRecipe, "ShouldRetry") &&
          Has(fixedRecipe, "HasBottomConfirmationModal"),
        "real fixed recipe applies directive at first click and bounded retry");
    Check(!Has(fixedRecipe, "TravelToFacilityAsync"),
        "real fixed-card retry cannot authorize its own facility movement");

    var medicine = ScreenCalls("TrySelectMedicineRecipeBySearchAsync", 3);
    Check(Has(medicine, "ShouldVetoRecipeMoveButton") &&
          Has(medicine, "HasBottomConfirmationModal") &&
          Has(medicine, "IsRecipeDetailStructureAsync"),
        "real medicine search guards directive, modal and detail before accepting selection");
    Check(!Has(medicine, "TravelToFacilityAsync"),
        "real medicine search cannot autonomously issue facility movement");
    Check(Has(medicine, "IsNearMedicineFirstResult"),
        "managed medicine selection checks an exact result at the expected row");

    var queue = ScreenCalls("QueueAsync", 4);
    Check(Has(queue, "SelectRecipeAsync") && Has(queue, "TravelToFacilityAsync") &&
          Has(queue, "MustReportToCoordinator") &&
          Has(queue, "RecoverRemoteDetailToOnsiteAsync"),
        "real queue retains manager entry and typed remote-detail conflict path");
    Check(Has(queue, "RequireManagedIdleAsync") &&
          Has(fixedRecipe, "RequireManagedIdleAsync") &&
          Has(medicine, "RequireManagedIdleAsync") &&
          Has(selection, "RequireManagedIdleAsync"),
        "managed registration paths require a fresh CLI-safe activity guard before input");
    Check(Has(queue, "VerifyTwoFreshObservationsAsync") &&
          Has(queue, "RequireManagedIdleAsync"),
        "managed real QueueAsync wires independent title verification before UI input");
    var n01Calls = queue.ToList();
    int n01FreshTarget = n01Calls.FindLastIndex(x =>
        x.EndsWith(".IsStableFreshFreeActionTarget", StringComparison.Ordinal));
    int n01FinalClick = n01Calls.FindLastIndex(x =>
        x.EndsWith(".ClickFresh", StringComparison.Ordinal));
    Check(n01FreshTarget >= 0 && n01FinalClick > n01FreshTarget &&
          Has(queue, "TryFindFreeProcessButtonVisual") &&
          Has(queue, "FindRecipeAsync") &&
          Has(queue, "DetectRemoteProcessStateAsync"),
        "N01 compiled QueueAsync reacquires final button from fresh frame after recipe/remote checks");
    var f07Title = ScreenCalls("FindRecipeAsync", 3);
    Check(Has(f07Title, "FindAsync"),
        "F07 real recipe title check remains on managed final action path");
    Check(queue.ToList().FindIndex(s => s.EndsWith(".MustReportToCoordinator")) <
          queue.ToList().FindIndex(s => s.EndsWith(".RecoverRemoteDetailToOnsiteAsync")),
        "real queue checks manager conflict BEFORE Automatic-only remote-detail recovery");

    var travel = ScreenCalls("TravelToFacilityAsync", 5);
    Check(Has(travel, "IsManagedFreshArrivalObservation") &&
          Has(travel, "HasVerifiedManagedMoveTransition") &&
          Has(travel, "HasVerifiedManagedInstantArrival") &&
          Has(travel, "HasOnsiteCloseButtonVisual") &&
          Has(travel, "HasFacilityMoveButtonPositiveEvidenceAsync"),
        "F01 real managed Fresh travel tests dual-anchor instant arrival before stable and final proofs");
    Check(CallBoolean(travelPolicy, "HasVerifiedManagedInstantArrival",
            true, true, true, true, false, false, false) &&
          !CallBoolean(travelPolicy, "HasVerifiedManagedInstantArrival",
            true, false, true, true, false, false, false) &&
          !CallBoolean(travelPolicy, "HasVerifiedManagedInstantArrival",
            true, true, true, true, true, false, false) &&
          !CallBoolean(travelPolicy, "HasVerifiedManagedInstantArrival",
            true, true, true, true, false, null, false) &&
          !CallBoolean(travelPolicy, "HasVerifiedManagedInstantArrival",
            true, true, true, true, false, false, true),
        "L2 compiled real policy refuses missing pre-click button, remote UI, unknown CLI or modal");
    Check(!CallBoolean(travelPolicy, "HasVerifiedManagedMoveTransition", true, false, 0) &&
          !CallBoolean(travelPolicy, "HasVerifiedManagedMoveTransition", true, false, 1) &&
          !CallBoolean(travelPolicy, "HasVerifiedManagedMoveTransition", false, true, 2) &&
          CallBoolean(travelPolicy, "HasVerifiedManagedMoveTransition", true, true, 0) &&
          CallBoolean(travelPolicy, "HasVerifiedManagedMoveTransition", true, false, 2),
        "F01 compiled policy rejects uncorrelated OCR title misses, CLI-only loading and missing move input");
    var enter = ScreenCalls("EnterFacilityAsync", 3);
    Check(Has(enter, "RequireManagedIdleAsync"),
        "F03 managed K/Esc/menu selection rechecks safe CLI before navigation input");
    Check(Has(travel, "RequireManagedIdleAsync") &&
          Has(travel, "IsFacilityTravelDialogAsync"),
        "F03/F04 optional travel popup uses positive wording plus managed idle before Space");
    var prompt = ScreenCalls("HasCollectPromptAsync", 4);
    Check(Has(prompt, "ShouldBlockReceiptForMoveButton") &&
          Has(prompt, "HasBottomConfirmationModal") &&
          Has(prompt, "TryAutoTravelingAsync") &&
          Has(prompt, "HasCollectButtonVisual"),
        "real blue receive prompt uses directive, popup, idle CLI and visual button");
    Check(!Has(prompt, "TravelToFacilityAsync"),
        "receive prompt cannot make an independent travel decision");
    // F02 must inspect the COMPILED screen, not only the coordinator's
    // earlier snapshot. The real blue-button predicate must require ALL
    // facility jobs complete, and the bounded receipt implementation must
    // rerun it AFTER the persisted N02 receive boundary and BEFORE Space.
    Check(Has(prompt, "CanCollectManagedFacility"),
        "F02 managed blue prompt checks all facility jobs, not any completed slot");

    // N02 moves the actual managed-receipt async body into a boundary-aware
    // method. Inspect that compiled production body, not the thin old overload.
    var collect = ScreenCalls("CollectAsyncAtBoundary", 4);
    Check(Has(ScreenCalls("CollectAsync", 3), "CollectAsyncAtBoundary") &&
          Count(collect, "WaitForCollectPromptAsync") >= 2 &&
          Has(collect, "TravelToFacilityAsync") &&
          Has(collect, "ConfirmCompletionResultAsync") &&
          Has(collect, "AfterVerifiedFacilityEntry") &&
          Has(collect, "Invoke") && Has(collect, "TapFresh"),
        "real receipt keeps manager travel, blue recheck, completion proof and durable receive-input boundary");
    Check(Has(collect, "HasCollectPromptAsync"),
        "F02 compiled receipt checks fresh entire lane again after durable boundary before Space");
    // The gate must be on the actual async production path in strict order.
    // Merely calling the check somewhere else in the same method is insufficient.
    var f02CompiledCalls = collect.ToList();
    int f02DurableBoundary = f02CompiledCalls.FindLastIndex(x => x.EndsWith(".Invoke", StringComparison.Ordinal));
    int f02FinalGate = f02CompiledCalls.FindLastIndex(x => x.EndsWith(".HasCollectPromptAsync", StringComparison.Ordinal));
    int f02ReceiveSpace = f02CompiledCalls.FindIndex(x => x.EndsWith(".TapFresh", StringComparison.Ordinal));
    Check(f02DurableBoundary >= 0 && f02DurableBoundary < f02FinalGate &&
          f02FinalGate < f02ReceiveSpace,
        "F02 compiled input ordering: durable boundary -> fresh all-work prompt -> receive Space");
    Check(Has(collect, "RequireManagedIdleAsync"),
        "F03 actual blue receive Space has fresh manager idle gate");
    var completion = ScreenCalls("ConfirmCompletionResultAsync", 5);
    Check(Has(completion, "CanConfirmCompletion") &&
          Has(completion, "CloseCompletionResultAndWaitForFacilityAsync"),
        "real completion requires modal approval and guarded close");
    var close = ScreenCalls("CloseCompletionResultAndWaitForFacilityAsync", 6);
    Check(Has(completion, "CanSendCompletionCloseSpace") &&
          Has(close, "CanSendCompletionCloseSpace"),
        "compiled completion and close paths use managed tri-state CLI guard");
    Check(Has(close, "CanConfirmCliReceiptCompletion") &&
          Has(close, "CanRetryCliReceiptCompletionClose") &&
          Has(close, "WaitForReceiptFacilityReturnAsync"),
        "real completion-close path rechecks modal and awaits facility return");
    var completionClose = ScreenCalls("RequireManagedCompletionCloseAsync", 2);
    Check(Has(close, "RequireManagedCompletionCloseAsync") &&
          Has(completionClose, "CanCloseManagedCompletionResult") &&
          Has(completionClose, "RequireManagedIdleAsync") &&
          Has(completionClose, "FindAsync"),
        "F03/F04 compiled close paths require positive result OCR and fresh activity");
    Check(CallBoolean(receiptPolicy, "CanCloseManagedCompletionResult",
              true, true, false, false, false) &&
          !CallBoolean(receiptPolicy, "CanCloseManagedCompletionResult",
              true, false, false, false, false) &&
          !CallBoolean(receiptPolicy, "CanCloseManagedCompletionResult",
              true, true, true, false, false) &&
          !CallBoolean(receiptPolicy, "CanCloseManagedCompletionResult",
              true, true, false, true, false) &&
          !CallBoolean(receiptPolicy, "CanCloseManagedCompletionResult",
              true, true, false, false, true) &&
          !CallBoolean(receiptPolicy, "CanCloseManagedCompletionResult",
              true, true, false, false, null),
        "F04 rejects unrelated green, facility, travel popup, moving or unknown CLI");
    var returning = ScreenCalls("WaitForReceiptFacilityReturnAsync", 4);
    Check(Has(returning, "CanConfirmReceiptFacilityReturn") &&
          Has(returning, "CanRecoverFieldAfterCompletion") &&
          Has(returning, "AfterVerifiedReceiptReturn"),
        "real receipt return distinguishes verified bench from legacy FIELD recovery");

    // Exercise the policy methods used by these exact compiled screen call
    // sites, with the always-visible move button and contradictory UI evidence.
    Check(CallBoolean(entryPolicy, "ShouldVetoRecipeMoveButton", automatic, true) &&
          !CallBoolean(entryPolicy, "ShouldVetoRecipeMoveButton", fresh, true) &&
          !CallBoolean(entryPolicy, "ShouldVetoRecipeMoveButton", reuse, true),
        "real recipe policy preserves manager Fresh/Reuse despite teal move button");
    Check(CallBoolean(travelPolicy, "IsManagedFreshArrivalObservation", true, false, true, true) &&
          !CallBoolean(travelPolicy, "IsManagedFreshArrivalObservation", true, false, true, false) &&
          !CallBoolean(travelPolicy, "IsManagedFreshArrivalObservation", true, null, true, true) &&
          !CallBoolean(travelPolicy, "IsManagedFreshArrivalObservation", true, true, true, true) &&
          !CallBoolean(travelPolicy, "IsManagedFreshArrivalObservation", false, false, true, true),
        "managed Fresh arrival requires title, explicit idle, click and transition");
    Check(!CallBoolean(remotePolicy, "MustReportToCoordinator", automatic, true) &&
          CallBoolean(remotePolicy, "MustReportToCoordinator", fresh, true) &&
          CallBoolean(remotePolicy, "MustReportToCoordinator", reuse, true),
        "real detail conflict reports managed OCR discrepancy without child recovery");
    Check(!CallBoolean(receiptPolicy, "ShouldBlockReceiptForMoveButton",
            fresh, false, true, true) &&
          !CallBoolean(receiptPolicy, "ShouldBlockReceiptForMoveButton",
            reuse, false, true, true) &&
          CallBoolean(receiptPolicy, "ShouldBlockReceiptForMoveButton",
            automatic, false, true, true),
        "real receive move-button veto remains Automatic-only");
    Check(!CallBoolean(receiptPolicy, "CanConfirmReceiptFacilityReturn",
            true, true, false, false) &&
          !CallBoolean(receiptPolicy, "CanConfirmReceiptFacilityReturn",
            true, false, true, false) &&
          !CallBoolean(receiptPolicy, "CanConfirmReceiptFacilityReturn",
            true, false, false, null) &&
          CallBoolean(receiptPolicy, "CanConfirmReceiptFacilityReturn",
            true, false, false, false),
        "real result-return policy rejects popup, travel and unknown CLI");
    Check(CallBoolean(receiptPolicy, "CanSendCompletionCloseSpace", true, false) &&
          !CallBoolean(receiptPolicy, "CanSendCompletionCloseSpace", true, true) &&
          !CallBoolean(receiptPolicy, "CanSendCompletionCloseSpace", true, null) &&
          CallBoolean(receiptPolicy, "CanSendCompletionCloseSpace", false, null) &&
          CallBoolean(receiptPolicy, "CanSendCompletionCloseSpace", false, true),
        "manager blocks unknown/traveling completion Space; single policy is unchanged");
    Check(CallCache("AfterVerifiedFacilityEntry", automatic, "금속 가공 시설") ==
          "금속 가공 시설" &&
          CallCache("AfterVerifiedFacilityEntry", fresh, "금속 가공 시설") == null &&
          CallCache("AfterVerifiedFacilityEntry", reuse, "금속 가공 시설") == null &&
          CallCache("AfterVerifiedReceiptReturn", true, "금속 가공 시설") == null,
        "real screen cache policy cannot store manager-owned onsite proof");


    // F08: invoke the ACTUAL compiled async identity policy, not a duplicate
    // implementation or an IL-only token. Deterministic scripted observations
    // model a correct/wrong/late detail across a real awaited boundary.
    async Task<bool> VerifiedSequenceAsync(
        Func<CancellationToken, Task<bool>> reader,
        Func<TimeSpan, CancellationToken, Task> pause,
        CancellationToken token = default)
    {
        var invoked = Method(identityPolicy, "VerifyTwoFreshObservationsAsync", 3)
            .Invoke(null, new object[] { reader, pause, token });
        return await (Task<bool>)(invoked ?? throw new InvalidOperationException(
            "Real identity-policy task missing"));
    }
    int readCount = 0, waitCount = 0, authorizedInputs = 0;
    bool accepted = await VerifiedSequenceAsync(
        async token => { await Task.Yield(); token.ThrowIfCancellationRequested(); readCount++; return true; },
        (duration, token) => { token.ThrowIfCancellationRequested(); waitCount++; return Task.CompletedTask; });
    if (accepted) authorizedInputs++;
    Check(accepted && readCount == 2 && waitCount == 1 && authorizedInputs == 1,
        "F08 actual async gate grants action only after two correct fresh observations");
    readCount = 0; waitCount = 0; authorizedInputs = 0;
    accepted = await VerifiedSequenceAsync(
        async token => { await Task.Yield(); token.ThrowIfCancellationRequested(); return ++readCount == 1; },
        (duration, token) => { waitCount++; return Task.CompletedTask; });
    if (accepted) authorizedInputs++;
    Check(!accepted && readCount == 2 && waitCount == 1 && authorizedInputs == 0,
        "F08 changed/wrong second detail rejects action after await");
    readCount = 0; waitCount = 0;
    accepted = await VerifiedSequenceAsync(
        token => { readCount++; return Task.FromResult(false); },
        (duration, token) => { waitCount++; return Task.CompletedTask; });
    Check(!accepted && readCount == 1 && waitCount == 0,
        "F08 missing first title prevents redundant capture and input");
    using (var stop = new CancellationTokenSource())
    {
        int observations = 0;
        bool canceledBeforeAction = false;
        try
        {
            await VerifiedSequenceAsync(
                token => { observations++; return Task.FromResult(true); },
                (duration, token) => { stop.Cancel(); return Task.CompletedTask; },
                stop.Token);
        }
        catch (OperationCanceledException) { canceledBeforeAction = true; }
        Check(canceledBeforeAction && observations == 1,
            "F08 F10-style cancel between fresh observations stops before input");
    }
    // This MUST invoke the production compiled policy methods. Qualifier
    // collisions are real (iron recipes share the display output "철괴").
    bool UniqueOutput(string selected, string output, string[] names) =>
        (bool)(Method(identityPolicy, "MayUseBaseOutputTitle", 3).Invoke(
            null, new object[] { selected, output, names }) ?? false);
    Check(!UniqueOutput("철괴(광석)", "철괴",
              new[] { "철괴(광석)", "철괴(철 광석)" }) &&
          UniqueOutput("밀가루(곡물)", "밀가루",
              new[] { "밀가루(곡물)", "목재", "강철괴" }) &&
          !UniqueOutput("철괴", "철괴", new[] { "철괴" }),
        "F07 qualified output-only title never proves which duplicate CLI recipe was clicked");
    bool StableFree(Point earlier, Point latest) =>
        (bool)(Method(identityPolicy, "IsStableFreshFreeActionTarget", 3).Invoke(
            null, new object[] { earlier, latest,
                new Rectangle(180, 895, 470, 95) }) ?? false);
    Check(StableFree(new Point(420, 944), new Point(444, 950)) &&
          !StableFree(new Point(420, 944), new Point(490, 950)) &&
          !StableFree(new Point(420, 944), new Point(440, 980)) &&
          !StableFree(new Point(420, 944), new Point(440, 770)),
        "N01 stale, displaced or off-button target cannot authorize last-frame ClickFresh");
    Check(CallBoolean(identityPolicy, "IsNearMedicineFirstResult", 210, 410, 219, 412) &&
          !CallBoolean(identityPolicy, "IsNearMedicineFirstResult", 210, 580, 219, 412),
        "F07 medicine result must be near exact first-result row");

    // F09: compare the old 3.1.52 title-only return with the new safe
    // guard. Unknown/true activity still blocks onsite ownership in both
    // managed and Automatic modes. A live CLI-staleness reproduction is
    // needed before relaxing that requirement.
    Check(!CallBoolean(receiptPolicy, "CanConfirmReceiptFacilityReturn",
            true, false, false, null) &&
          !CallBoolean(receiptPolicy, "CanConfirmReceiptFacilityReturn",
            true, false, false, true),
        "F09 stale activity stays unresolved; do not silently grant single-mode onsite proof");

    // Exercise private pixel detectors from production's actual screen type.
    // Synthetic 800x1000 bitmaps are intentionally isolated: no game window,
    // driver injection, Space, ClickFresh or paid interaction occurs.
    bool PixelGate(string name, Bitmap image) =>
        (bool)(Method(screen, name, 1).Invoke(null, new object[] { image }) ?? false);
    static Bitmap BlackFrame()
    {
        var bitmap = new Bitmap(800, 1000);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        return bitmap;
    }
    static void Paint(Bitmap frame, Rectangle bounds, Color color)
    {
        using var graphics = Graphics.FromImage(frame);
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, bounds);
    }

    using (var empty = BlackFrame())
    {
        Check(!PixelGate("HasFacilityMoveButtonVisual", empty) &&
              !PixelGate("HasCollectButtonVisual", empty) &&
              !PixelGate("HasBottomConfirmationModal", empty),
            "real visual detectors reject an empty transition frame");
    }
    using (var move = BlackFrame())
    {
        Paint(move, new Rectangle(20, 214, 135, 43), Color.FromArgb(0, 116, 134));
        Check(PixelGate("HasFacilityMoveButtonVisual", move),
            "real move detector recognizes the fixed teal pill and click anchor");
        Check(!PixelGate("HasBottomConfirmationModal", move),
            "real teal move screen is not a completion confirmation popup");
    }
    using (var blue = BlackFrame())
    {
        Paint(blue, new Rectangle(20, 285, 80, 45), Color.FromArgb(28, 166, 230));
        Check(PixelGate("HasCollectButtonVisual", blue),
            "real blue receive detector sees a stabilized receive-button shape");
        Check(!PixelGate("HasBottomConfirmationModal", blue),
            "real blue receive prompt does not masquerade as completion result");
    }
    using (var completionFrame = BlackFrame())
    {
        Paint(completionFrame, new Rectangle(400, 865, 205, 80), Color.FromArgb(0, 185, 25));
        Check(PixelGate("HasBottomConfirmationModal", completionFrame),
            "real completion modal detector sees green result confirmation");
        Check(!PixelGate("HasCollectButtonVisual", completionFrame),
            "real completion modal cannot be mistaken for another blue receipt");
    }

    // V3.1.59: the original capture shows the facility title at (20,50),
    // with a second facility-level label at (20,155). Detect both independent
    // fixed anchors without running Korean OCR or accessing a game window.
    using (var fixedFrame = BlackFrame())
    {
        Check(!PixelGate("HasFixedFacilityHeaderVisual", fixedFrame),
            "empty frame cannot prove the fixed facility UI");
        Paint(fixedFrame, new Rectangle(20, 50, 75, 24), Color.White);
        Check(!PixelGate("HasFixedFacilityHeaderVisual", fixedFrame),
            "one top-left heading alone cannot prove facility UI");
        Paint(fixedFrame, new Rectangle(20, 154, 150, 19), Color.White);
        Check(PixelGate("HasFixedFacilityHeaderVisual", fixedFrame),
            "fixed title and facility-level anchors prove the actual facility layout");

        // No UI runtime/OCR engine is initialized. Any accidental OCR
        // invocation in the repeated path will throw instead of passing.
        object probe = RuntimeHelpers.GetUninitializedObject(screen);
        screen.GetField("_repeatOcrFreeFacilityTitle", all)!
            .SetValue(probe, "목재 가공");
        screen.GetField("_repeatOcrFreeRecipe", all)!
            .SetValue(probe, true);
        async Task<object?> ProbeAsync(string method, params object?[] parameters)
        {
            var task = (Task)Method(screen, method, parameters.Length)
                .Invoke(probe, parameters)!;
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }
        Check(await ProbeAsync("FindFacilityHeaderAsync",
                  fixedFrame, "목재 가공", CancellationToken.None) is not null,
            "verified fixed facility title skips OCR entirely");
        Paint(fixedFrame, new Rectangle(20, 154, 150, 19), Color.Black);
        Check(await ProbeAsync("FindFacilityHeaderAsync",
                  fixedFrame, "목재 가공", CancellationToken.None) is null,
            "repeat visual structure missing must not silently authorize a click");
        Check(await ProbeAsync("IsRecipeDetailStructureAsync",
                  fixedFrame, CancellationToken.None) is bool initialDetail && !initialDetail,
            "repeat detail does not fall back to OCR on an absent free button");
        Paint(fixedFrame, new Rectangle(220, 915, 390, 50), Color.FromArgb(10, 165, 160));
        Check(await ProbeAsync("IsRecipeDetailStructureAsync",
                  fixedFrame, CancellationToken.None) is bool fixedDetail && fixedDetail,
            "verified repeat detail uses fixed free-button shape without OCR");
        screen.GetField("_repeatOcrFreeRecipe", all)!.SetValue(probe, false);
        screen.GetField("_selectedFixedRecipeKey", all)!
            .SetValue(probe, "목재 가공 시설\u001f목재+\u001f1");
        Check(await ProbeAsync("IsRecipeDetailStructureAsync",
                  fixedFrame, CancellationToken.None) is bool firstFixedDetail && firstFixedDetail,
            "first fixed-card detail accepts confirmed free-button geometry without OCR");
        Type planType = production.GetType("FishingAutomation.AlteringPlan", true)!;
        object firstFixedPlan = Activator.CreateInstance(planType,
            new object[] { "목재 가공 시설", "목재+", 100, 3, false, 1 })!;
        Check(await ProbeAsync("FindRecipeAsync",
                  fixedFrame, firstFixedPlan, CancellationToken.None) is not null,
            "first selected fixed recipe identity does not require title OCR");
        Check((await ProbeAsync("DetectRemoteProcessStateAsync",
                  fixedFrame, CancellationToken.None))?.ToString()?.StartsWith("(False") == true,
            "fixed free-button confirmation avoids remote-label OCR and false stops");
    }

    Console.WriteLine($"PASS L2: {checks} real-screen integration checks (no injected input)");
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}
