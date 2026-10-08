using FishingAutomation;
using System.Text.Json;

static class Program
{
    static readonly CliIdentityContext Identity = new("character", "tester", "account", "realm");
    static readonly AlteringPlan Wood = new("목재 가공 시설", "목재", 100, 10, false);
    static readonly AlteringPlan Steel = new("금속 가공 시설", "강철괴", 100, 10, false);
    static readonly AlteringPlan Rice = new("식재료 가공 시설", "불린쌀", 100, 10, false);
    static readonly AlteringPlan[] Plans = { Wood, Steel, Rice };
    static int passed;
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
    static Task Delay(TimeSpan _, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    static async Task Test(string name, Func<Task> test)
    { await test(); passed++; Console.WriteLine("PASS N02 " + name); }
    static async Task Reject(Func<Task> action, string text)
    {
        try { await action(); }
        catch (Exception ex) when (ex.ToString().Contains(text, StringComparison.Ordinal)) { return; }
        throw new Exception("Expected failure: " + text);
    }
    static Task Finish(AlteringPlan plan, MultiAlteringBatchStore batch, World world)
        => new Harness(batch, new[] { plan }, world).Automation(plan).RunAsync(plan, default);
    static Task Open(MultiAlteringBatchStore batch, World world, IReadOnlyList<AlteringPlan>? plans = null,
        CliIdentityContext? identity = null) => batch.OpenAsync(plans ?? Plans, identity ?? Identity, world, default);

    static async Task Main()
    {
        await Test("A/F10 partial completion, new objects and total progress", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using (var batch = new MultiAlteringBatchStore(temp.Path))
            {
                await Open(batch, world);
                await new Harness(batch, Plans, world).RunUntilPartial();
                Check(batch.Session(Wood).MultiState == MultiAlteringItemState.Completed, "Wood completion not committed");
                Check(batch.Session(Steel).QueuedWorks == 4 && batch.Session(Rice).QueuedWorks == 3, "Partial queue counters lost");
            }
            world.Counts[Wood.OutputName] = 100;
            using var restored = new MultiAlteringBatchStore(temp.Path); await Open(restored, world);
            Check(restored.CompletedPlans(Plans).SequenceEqual(new[] { Wood }), "Completed set not restored");
            Check(restored.Session(Steel).QueuedWorks == 4 && restored.Session(Rice).QueuedWorks == 3,
                "Fresh objects lost partial registration checkpoints");
            Check(restored.CompletedPlans(Plans).Sum(p => p.TargetQuantity) == 100, "Restored partial total progress lost");
            int woodRegistrations = world.Registered.Count(x => x == Wood.DisplayName);
            await new Harness(restored, Plans, world).Run();
            Check(world.Registered.Count(x => x == Wood.DisplayName) == woodRegistrations, "Completed wood registered after F10");
            Check(restored.Session(Steel).QueuedWorks == 10 && restored.Session(Rice).QueuedWorks == 10, "Remaining plans failed");
            Check(restored.CompletedPlans(Plans).Sum(p => p.TargetQuantity) == 300, "Total progress not restored");
        });
        await Test("B/abrupt termination immediately after durable item completion", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using (var batch = new MultiAlteringBatchStore(temp.Path))
            { await Open(batch, world); await Finish(Wood, batch, world); }
            world.Registered.Clear();
            using var fresh = new MultiAlteringBatchStore(temp.Path); await Open(fresh, world);
            var progress = new List<AlteringProgress>();
            var auto = new Harness(fresh, Plans, world).Automation(Wood); auto.Progress += progress.Add;
            world.FailInventory = Wood.OutputName; // completion must bypass current inventory
            Check(await auto.RunBatchAsync(Wood, 1, default) == AlteringRunResult.Completed, "Completion not restored");
            Check(world.Registered.Count == 0 && progress.Single().ConfirmedQuantity == 100, "Restart registered or lost progress");
        });
        foreach (string cut in new[] { "before-input", "after-input", "after-receipt-read", "write", "replace" })
            await Test("C/receipt crash boundary " + cut, async () =>
            {
                using var temp = new Temp(); var world = new World(new[] { Wood });
                bool armed = false;
                using (var batch = new MultiAlteringBatchStore(temp.Path, point =>
                    { if (armed && point == cut) throw new IOException("injected " + cut); }))
                {
                    await Open(batch, world, new[] { Wood });
                    var state = batch.Session(Wood) with { QueuedWorks = Wood.RequiredWorks, MultiState = MultiAlteringItemState.InProgress };
                    batch.PlanStore(Wood).Save(state);
                    world.AddCompleted(Wood, 10);
                    if (cut == "before-input") world.BeforeReceipt = () => throw new IOException("injected before-input");
                    if (cut == "after-input") world.AfterReceipt = () => throw new IOException("injected after-input");
                    if (cut == "after-receipt-read") world.AfterReceipt = () => world.FailInventory = Wood.OutputName;
                    if (cut is "write" or "replace") world.AfterReceipt = () => armed = true;
                    await Reject(() => new Harness(batch, new[] { Wood }, world).Run(), cut == "after-receipt-read" ? "inventory read" : "injected");
                    Check(batch.Session(Wood).MultiState == MultiAlteringItemState.RecoveryRequired, "Uncommitted receipt reported complete");
                }
                world.FailInventory = null; world.BeforeReceipt = null; world.AfterReceipt = null;
                int registrations = world.Registered.Count;
                using var restarted = new MultiAlteringBatchStore(temp.Path);
                await Reject(() => Open(restarted, world, new[] { Wood }), "RecoveryRequired");
                Check(world.Registered.Count == registrations && File.Exists(temp.Manifest), "Uncertain item registered or record lost");
            });
        await Test("C/read-only receipt preflight failure preserves resumable state", async () =>
        {
            using var temp = new Temp(); var world = new World(new[] { Wood });
            using (var batch = new MultiAlteringBatchStore(temp.Path))
            {
                await Open(batch, world, new[] { Wood });
                batch.PlanStore(Wood).Save(batch.Session(Wood) with
                { QueuedWorks = Wood.RequiredWorks, MultiState = MultiAlteringItemState.InProgress });
                world.AddCompleted(Wood, Wood.RequiredWorks);
                world.BeforeReceiptPrompt = () => throw new InvalidOperationException("OCR preflight failure");
                await Reject(() => new Harness(batch, new[] { Wood }, world).Run(),
                    "OCR preflight failure");
                Check(batch.Session(Wood).MultiState == MultiAlteringItemState.InProgress,
                    "Pre-input OCR failure poisoned an unstarted receipt");
                Check(world.Receipts == 0, "Pre-input OCR failure transmitted receive input");
            }
            world.BeforeReceiptPrompt = null;
            using var resumed = new MultiAlteringBatchStore(temp.Path);
            await Open(resumed, world, new[] { Wood });
            await new Harness(resumed, new[] { Wood }, world).Run();
            Check(resumed.Session(Wood).MultiState == MultiAlteringItemState.Completed,
                "Verified no-input receipt cannot resume safely");
            Check(world.Registered.Count == 0 && world.Receipts == 1,
                "Safe pre-input resume caused duplicate registration/receipt");
        });
        await Test("D/completed intermediate consumed, no inventory-based reopening", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using (var batch = new MultiAlteringBatchStore(temp.Path))
            { await Open(batch, world); await Finish(Wood, batch, world); }
            world.Counts[Wood.OutputName] = 40; world.Registered.Clear();
            using var restored = new MultiAlteringBatchStore(temp.Path); await Open(restored, world);
            await new Harness(restored, Plans, world).Run();
            Check(world.Registered.All(x => x != Wood.DisplayName), "Consumed completed item reopened");
            Check(restored.Session(Wood).CreditedInternalConsumptionQuantity == 0, "N02 fabricated F05 consumption credit");
        });
        foreach (string operation in new[] { "write", "replace" })
            await Test("E/registration save failure " + operation, async () =>
            {
                using var temp = new Temp(); var world = new World(new[] { Wood }); bool armed = false;
                using var batch = new MultiAlteringBatchStore(temp.Path, p => { if (armed && p == operation) throw new IOException("injected " + operation); });
                await Open(batch, world, new[] { Wood }); string before = File.ReadAllText(temp.Manifest); armed = true;
                await Reject(() => new Harness(batch, new[] { Wood }, world).Run(), "원자 저장 실패");
                Check(world.Registered.Count == 0 && File.ReadAllText(temp.Manifest) == before, "Save failure destroyed old state or input occurred");
                await Reject(() => new Harness(batch, new[] { Wood }, world).Run(), "저장 실패");
            });
        await Test("E/read failure is not file absence", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using (var batch = new MultiAlteringBatchStore(temp.Path)) await Open(batch, world);
            string before = File.ReadAllText(temp.Manifest);
            using var bad = new MultiAlteringBatchStore(temp.Path, p => { if (p == "read") throw new IOException("injected read failure"); });
            await Reject(() => Open(bad, world), "read failure");
            Check(world.Registered.Count == 0 && File.ReadAllText(temp.Manifest) == before, "Read failure reset batch");
        });
        foreach (string content in new[] { "{ damaged", "null", "{\"Version\":17}" })
            await Test("E/corrupt manifest " + content, async () =>
            {
                using var temp = new Temp(); var world = new World(Plans); File.WriteAllText(temp.Manifest, content);
                using var bad = new MultiAlteringBatchStore(temp.Path);
                await Reject(() => Open(bad, world), "다중가공");
                Check(world.Registered.Count == 0 && File.ReadAllText(temp.Manifest) == content, "Corrupt manifest overwritten");
            });
        await Test("F/normal full completion, cleanup, UI acknowledgement and next batch", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans); string firstId;
            using (var batch = new MultiAlteringBatchStore(temp.Path))
            {
                await Open(batch, world); firstId = batch.BatchId;
                await new Harness(batch, Plans, world).Run();
                batch.Complete();
                Check(batch.IsTerminal && batch.CompletedPlans(Plans).Count == 3, "Full completion not committed");
                Directory.CreateDirectory(Path.Combine(temp.Path, "dependencies"));
                File.WriteAllText(Path.Combine(temp.Path, "dependencies", "plan-test.json"), "test");
                batch.Cleanup();
                Check(!Directory.EnumerateFiles(Path.Combine(temp.Path, "dependencies")).Any(), "Dependency cleanup failed");
                // Matches production ordering: clear UI roster, then acknowledge.
                var uiPlans = Plans.ToList(); uiPlans.Clear(); batch.AcknowledgeClearedPlan();
                Check(uiPlans.Count == 0, "UI plans not cleared");
            }
            using var next = new MultiAlteringBatchStore(temp.Path); await Open(next, world);
            Check(next.BatchId != firstId && next.CompletedPlans(Plans).Count == 0, "New batch reused completion tombstone");
        });
        await Test("G/partial batch reorder restores by identity", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using (var batch = new MultiAlteringBatchStore(temp.Path)) { await Open(batch, world); await Finish(Wood, batch, world); }
            var reordered = new[] { Rice, Wood, Steel }; world.Registered.Clear();
            using var restored = new MultiAlteringBatchStore(temp.Path); await Open(restored, world, reordered);
            Check(restored.CompletedPlans(reordered).Single() == Wood, "Index used as item identity");
            await new Harness(restored, reordered, world).Run();
            Check(world.Registered.All(x => x != Wood.DisplayName), "Reordered completion registered again");
        });
        foreach (var changed in new[] { Wood with { RecipeOrdinal = 2 }, Wood with { TargetQuantity = 110 },
            Wood with { ProducedPerWork = 20 }, Wood with { FacilityName = Steel.FacilityName }, Wood with { DisplayName = "목재+" } })
            await Test("H/plan conflict preserves original " + changed, async () =>
            {
                using var temp = new Temp(); var world = new World(Plans);
                using (var batch = new MultiAlteringBatchStore(temp.Path)) { await Open(batch, world); await Finish(Wood, batch, world); }
                string before = File.ReadAllText(temp.Manifest); world.Registered.Clear();
                using var conflict = new MultiAlteringBatchStore(temp.Path);
                await Reject(() => Open(conflict, world, new[] { changed, Steel, Rice }), "저장 충돌");
                Check(File.ReadAllText(temp.Manifest) == before && world.Registered.Count == 0, "Conflict replaced old plan");
            });
        foreach (var changed in new[] { Identity with { CharacterId = "other" }, Identity with { CharacterName = "other" },
            Identity with { AccountCode = "other" }, Identity with { RealmName = "other" } })
            await Test("H/character identity conflict " + changed, async () =>
            {
                using var temp = new Temp(); var world = new World(Plans);
                using (var batch = new MultiAlteringBatchStore(temp.Path)) await Open(batch, world);
                string before = File.ReadAllText(temp.Manifest);
                using var conflict = new MultiAlteringBatchStore(temp.Path);
                await Reject(() => Open(conflict, world, identity: changed), "저장 충돌");
                Check(File.ReadAllText(temp.Manifest) == before && world.Registered.Count == 0, "Identity conflict destroyed batch");
            });
        await Test("H/account+character fallback preserves complete ledger on restart", async () =>
        {
            var accountNamed = new CliIdentityContext(null, "테스트", "account-7", null);
            using var temp = new Temp(); var world = new World(Plans);
            using (var first = new MultiAlteringBatchStore(temp.Path))
            {
                await Open(first, world, identity: accountNamed);
                await Finish(Wood, first, world);
                Check(first.CompletedPlans(Plans).SequenceEqual(new[] { Wood }),
                    "fallback identity lost completed item");
            }
            string saved = File.ReadAllText(temp.Manifest);
            world.Registered.Clear();
            using (var resumed = new MultiAlteringBatchStore(temp.Path))
            {
                await Open(resumed, world, identity: accountNamed);
                Check(resumed.CompletedPlans(Plans).SequenceEqual(new[] { Wood }),
                    "fallback identity reopened finished wood");
                Check(resumed.Session(Wood).MultiState == MultiAlteringItemState.Completed,
                    "fallback identity erased tombstone");
            }
            using var wrongAccount = new MultiAlteringBatchStore(temp.Path);
            await Reject(() => Open(wrongAccount, world, identity:
                accountNamed with { AccountCode = "different" }), "저장 충돌");
            Check(File.ReadAllText(temp.Manifest) == saved && world.Registered.Count == 0,
                "fallback identity conflict mutated durable manifest");
        });
        await Test("H/insufficient identity blocks new batch without any manifest", async () =>
        {
            foreach (var weak in new[]
            {
                new CliIdentityContext(null, "테스트", null, null),
                new CliIdentityContext(null, null, "account-7", null),
                new CliIdentityContext(null, null, null, "서버7")
            })
            {
                using var temp = new Temp(); var world = new World(Plans);
                using var batch = new MultiAlteringBatchStore(temp.Path);
                await Reject(() => Open(batch, world, identity: weak), "식별 정보가 부족");
                Check(!File.Exists(temp.Manifest) && world.Registered.Count == 0,
                    "weak identity silently created an unbound durable batch");
            }
        });
        await Test("H/character+realm without ID remains valid", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using var batch = new MultiAlteringBatchStore(temp.Path);
            await Open(batch, world, identity: new CliIdentityContext(null, "테스트", null, "서버7"));
            Check(File.Exists(temp.Manifest), "old character+realm identity unexpectedly blocked");
        });
        await Test("J/realm-only fresh test begins under isolated manifest", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            var weak = new CliIdentityContext(null, null, null, "server");
            string original = Path.Combine(temp.Path, "plan-obsolete.json");
            File.WriteAllText(original, "{old manifest kept exactly}");
            string existing = File.ReadAllText(original);
            string isolated = Path.Combine(temp.Path, "limited-fresh-test");
            using var batch = new MultiAlteringBatchStore(isolated);
            await batch.OpenNewLimitedTestAsync(Plans, weak, world, default);
            Check(!batch.IsResuming && batch.CompletedPlans(Plans).Count == 0,
                "weak test unexpectedly resumed completed items");
            Check(File.Exists(Path.Combine(isolated, "batch.json")) &&
                !File.Exists(temp.Manifest) && File.ReadAllText(original) == existing,
                "weak test overwrote normal or legacy roster");
            Check(world.Registered.Count == 0, "fresh test registered before scheduler");
        });
        await Test("K/fresh F9 ignores but preserves old unresolved F05 journal", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            var realm = new CliIdentityContext(null, null, null, "server");
            string oldPath = Path.Combine(temp.Path, "limited-fresh-test");
            string oldManifest = Path.Combine(oldPath, "batch.json");
            using (var abandoned = new MultiAlteringBatchStore(oldPath))
            {
                await abandoned.OpenAsync(Plans, realm, world, default, allowSingleCharacter: true);
                abandoned.PrepareConsumption(Steel, Guid.NewGuid().ToString("N"),
                    new Dictionary<string, long> { [Wood.OutputName] = 0 }, default);
            }
            string original = File.ReadAllText(oldManifest);
            await Reject(async () =>
            {
                using var past = new MultiAlteringBatchStore(oldPath);
                await past.OpenAsync(Plans, realm, world, default, allowSingleCharacter: true);
            }, "미확정 소비 거래");
            string freshPath = Path.Combine(temp.Path, "fresh-runs", "f9-first");
            using (var fresh = new MultiAlteringBatchStore(freshPath))
            {
                await fresh.OpenFreshAsync(Plans, realm, world, default);
                Check(!fresh.IsResuming && fresh.Session(Wood).QueuedWorks == 0 &&
                    fresh.Session(Steel).QueuedWorks == 0,
                    "supervisor fresh F9 restored abandoned work");
                await new Harness(fresh, Plans, world).Run();
                Check(fresh.CompletedPlans(Plans).Count == 3,
                    "fresh supervisor did not order every selected item");
            }
            Check(File.ReadAllText(oldManifest) == original &&
                File.Exists(Path.Combine(freshPath, "batch.json")),
                "new F9 overwrote prior F05 history");
        });
        await Test("K/F10 history remains but next explicit F9 creates a new batch", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            var realm = new CliIdentityContext(null, null, null, "server");
            string firstPath = Path.Combine(temp.Path, "fresh-runs", "f9-first");
            string firstJson;
            using (var prior = new MultiAlteringBatchStore(firstPath))
            {
                await prior.OpenFreshAsync(new[] { Wood }, realm, world, default);
                firstJson = File.ReadAllText(Path.Combine(firstPath, "batch.json"));
            }
            string secondPath = Path.Combine(temp.Path, "fresh-runs", "f9-second");
            using (var fresh = new MultiAlteringBatchStore(secondPath))
            {
                await fresh.OpenFreshAsync(new[] { Steel }, realm, world, default);
                Check(!fresh.IsResuming && fresh.CompletedPlans(new[] { Steel }).Count == 0,
                    "new F9 reused prior F10 roster");
                Check(fresh.BatchId != JsonSerializer.Deserialize<MultiAlteringBatch>(firstJson)!.BatchId,
                    "F9 runs shared batch identity");
            }
            Check(File.ReadAllText(Path.Combine(firstPath, "batch.json")) == firstJson,
                "previous run manifest changed");
        });
        await Test("K/fresh F9 starts at zero with an old completed job and orders full target", async () =>
        {
            using var temp = new Temp(); var world = new World(new[] { Wood });
            world.AddCompleted(Wood, 1); // old game queue from a previous run
            string path = Path.Combine(temp.Path, "fresh-runs", "f9-new");
            using var fresh = new MultiAlteringBatchStore(path);
            await fresh.OpenFreshAsync(new[] { Wood },
                new CliIdentityContext(null, null, null, "server"), world, default);
            Check(!fresh.IsResuming && fresh.Session(Wood).QueuedWorks == 0 &&
                fresh.Session(Wood).InitialExistingWorks == 1 &&
                fresh.CompletedPlans(new[] { Wood }).Count == 0,
                "prior job was counted as newly entered F9 target");
            Check(world.Receipts == 0 && world.Registered.Count == 0,
                "fresh F9 preflight must not issue receipt or registration input");
            await new Harness(fresh, new[] { Wood }, world).Run();
            Check(world.Registered.Count == Wood.RequiredWorks &&
                world.Counts[Wood.OutputName] == (Wood.RequiredWorks + 1) * Wood.ProducedPerWork &&
                fresh.CompletedPlans(new[] { Wood }).Count == 1,
                "old completed job contaminated the 100-new-unit target");
        });
        await Test("K/previous F9 one registered then restart orders a NEW full target", async () =>
        {
            using var temp = new Temp(); var world = new World(new[] { Wood });
            var realm = new CliIdentityContext(null, null, null, "server");
            string oldPath = Path.Combine(temp.Path, "fresh-runs", "f9-old");
            string oldJson;
            using (var previous = new MultiAlteringBatchStore(oldPath))
            {
                await previous.OpenFreshAsync(new[] { Wood }, realm, world, default);
                await new Harness(previous, new[] { Wood }, world)
                    .Automation(Wood).RunBatchAsync(Wood, 1, default);
                Check(world.Registered.Count == 1 && previous.Session(Wood).QueuedWorks == 1,
                    "setup did not queue one previous-run job");
                oldJson = File.ReadAllText(Path.Combine(oldPath, "batch.json"));
            }
            string freshPath = Path.Combine(temp.Path, "fresh-runs", "f9-restart");
            using (var restarted = new MultiAlteringBatchStore(freshPath))
            {
                await restarted.OpenFreshAsync(new[] { Wood }, realm, world, default);
                Check(!restarted.IsResuming && restarted.Session(Wood).QueuedWorks == 0 &&
                    restarted.Session(Wood).InitialExistingWorks == 1,
                    "restart carried the old 1/100 progress into fresh target");
                await new Harness(restarted, new[] { Wood }, world).Run();
                Check(world.Registered.Count == Wood.RequiredWorks + 1 &&
                    world.Counts[Wood.OutputName] == (Wood.RequiredWorks + 1) * Wood.ProducedPerWork &&
                    restarted.CompletedPlans(new[] { Wood }).Count == 1,
                    "restart must add ten fresh registrations on top of one old registration");
            }
            Check(File.ReadAllText(Path.Combine(oldPath, "batch.json")) == oldJson,
                "new F9 altered the previous batch's historical evidence");
        });
        await Test("K/existing running work remains untouched while six free slots are registered", async () =>
        {
            using var temp = new Temp();
            var world = new World(new[] { Wood }) { ImmediateCompletion = false };
            world.Works.Add(new AlteringWork(
                Wood.OutputName, Wood.FacilityName, "InProgress", false, 60));
            using var fresh = new MultiAlteringBatchStore(
                Path.Combine(temp.Path, "fresh-runs", "f9-free-slots"));
            await fresh.OpenFreshAsync(new[] { Wood },
                new CliIdentityContext(null, null, null, "server"), world, default);
            Check(!fresh.IsResuming && fresh.Session(Wood).QueuedWorks == 0 &&
                fresh.Session(Wood).InitialExistingWorks == 1 &&
                world.Works.Count == 1 && world.Receipts == 0,
                "fresh F9 touched the old job before registration");
            var result = await new Harness(fresh, new[] { Wood }, world)
                .Automation(Wood).RunBatchAsync(Wood, 6, default);
            Check(result == AlteringRunResult.BatchQueued &&
                world.Registered.Count == 6 && world.Works.Count == 7 &&
                world.Receipts == 0 && world.PartialReceipts == 0 &&
                world.Works[0].RemainingSeconds == 60 &&
                fresh.Session(Wood).QueuedWorks == 6,
                "fresh F9 failed to register in free slots without changing the old job");
        });
        await Test("K/fresh F9 permits only unrelated facility work", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            world.Works.Add(new AlteringWork("외부 품목", "외부 가공 시설", "InProgress", false, 10));
            using var fresh = new MultiAlteringBatchStore(
                Path.Combine(temp.Path, "fresh-runs", "f9-new"));
            await fresh.OpenFreshAsync(new[] { Wood },
                new CliIdentityContext(null, null, null, "server"), world, default);
            Check(world.Works.Count == 1 && world.Registered.Count == 0,
                "unrelated facility work was consumed or registered");
        });
        await Test("K/new F9 fails closed if target run folder contains any history", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            string path = Path.Combine(temp.Path, "fresh-runs", "f9-new");
            using var fresh = new MultiAlteringBatchStore(path);
            string unknownPath = Path.Combine(path, "unresolved.tmp");
            File.WriteAllText(unknownPath, "old data");
            await Reject(() => fresh.OpenFreshAsync(new[] { Wood },
                new CliIdentityContext(null, null, null, "server"), world, default),
                "이전 기록이 존재");
            Check(File.ReadAllText(unknownPath) == "old data" &&
                !File.Exists(Path.Combine(path, "batch.json")),
                "unsafe fresh folder was overwritten");
        });
        await Test("J/one-character isolated batch F10 restarts without redoing finished items", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            var realmOnly = new CliIdentityContext(null, null, null, "server");
            string isolated = Path.Combine(temp.Path, "limited-fresh-test");
            using (var first = new MultiAlteringBatchStore(isolated))
            {
                await first.OpenAsync(Plans, realmOnly, world, default, allowSingleCharacter: true);
                await new Harness(first, Plans, world).RunUntilPartial();
                Check(first.CompletedPlans(Plans).SequenceEqual(new[] { Wood }),
                    "single-character finished item was not checkpointed");
            }
            int woodRegisteredBefore = world.Registered.Count(x => x == Wood.DisplayName);
            using var restarted = new MultiAlteringBatchStore(isolated);
            await restarted.OpenAsync(Plans, realmOnly, world, default, allowSingleCharacter: true);
            Check(restarted.IsResuming &&
                restarted.Session(Wood).MultiState == MultiAlteringItemState.Completed &&
                restarted.Session(Steel).QueuedWorks == 4,
                "single-character restart reset completed/queued records");
            await new Harness(restarted, Plans, world).Run();
            Check(world.Registered.Count(x => x == Wood.DisplayName) == woodRegisteredBefore,
                "finished wood was queued again after F10");
        });
        await Test("J/one-character unrelated facility jobs allowed at fresh start", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            var lonePlan = new[] { Wood };
            world.Works.Add(new AlteringWork("비선택 시설 작업", "외부 가공 시설", "InProgress", false, 10));
            using var batch = new MultiAlteringBatchStore(
                Path.Combine(temp.Path, "limited-fresh-test"));
            await batch.OpenAsync(lonePlan, new CliIdentityContext(null, null, null, "server"),
                world, default, allowSingleCharacter: true);
            Check(batch.CompletedPlans(lonePlan).Count == 0 && world.Works.Count == 1,
                "unrelated facility job was canceled or incorrectly attributed");
        });
        await Test("J/one-character selected facility queue blocks new batch", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            world.Works.Add(new AlteringWork("다른 목재", Wood.FacilityName, "InProgress", false, 10));
            string isolated = Path.Combine(temp.Path, "limited-fresh-test");
            using var batch = new MultiAlteringBatchStore(isolated);
            await Reject(() => batch.OpenAsync(new[] { Wood },
                new CliIdentityContext(null, null, null, "server"), world, default,
                allowSingleCharacter: true), "선택한 시설에 기존 대기 작업");
            Check(!File.Exists(Path.Combine(isolated, "batch.json")) &&
                world.Registered.Count == 0, "foreign job at selected facility allowed registration");
        });
        await Test("J/one-character changed plan cannot overwrite pending F10 batch", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            string isolated = Path.Combine(temp.Path, "limited-fresh-test");
            var realmOnly = new CliIdentityContext(null, null, null, "server");
            using (var first = new MultiAlteringBatchStore(isolated))
                await first.OpenAsync(Plans, realmOnly, world, default, allowSingleCharacter: true);
            string before = File.ReadAllText(Path.Combine(isolated, "batch.json"));
            using var changed = new MultiAlteringBatchStore(isolated);
            await Reject(() => changed.OpenAsync(new[] { Wood }, realmOnly, world,
                default, allowSingleCharacter: true), "저장 충돌");
            Check(before == File.ReadAllText(Path.Combine(isolated, "batch.json")) &&
                world.Registered.Count == 0, "plan change overwrote isolated checkpoint");
        });
        await Test("J/one-character rejects an unjournaled live job on F10 resume", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            string isolated = Path.Combine(temp.Path, "limited-fresh-test");
            var realmOnly = new CliIdentityContext(null, null, null, "server");
            using (var first = new MultiAlteringBatchStore(isolated))
                await first.OpenAsync(Plans, realmOnly, world, default, allowSingleCharacter: true);
            world.Works.Add(new AlteringWork(Wood.OutputName, Wood.FacilityName,
                "InProgress", false, 10));
            using var resume = new MultiAlteringBatchStore(isolated);
            await Reject(() => resume.OpenAsync(Plans, realmOnly, world,
                default, allowSingleCharacter: true), "저장된 등록 기록과 다릅니다");
            Check(world.Registered.Count == 0, "unowned job was automatically re-registered");
        });
        await Test("J/limited test cannot bypass an active verified batch", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using (var batch = new MultiAlteringBatchStore(temp.Path))
                await Open(batch, world);
            await Reject(() =>
            {
                MultiAlteringBatchStore.EnsureNoActiveVerifiedBatch(temp.Path);
                return Task.CompletedTask;
            }, "미완료/미확정");
            using var weak = new MultiAlteringBatchStore(Path.Combine(temp.Path, "limited-fresh-test"));
            Check(!File.Exists(Path.Combine(temp.Path, "limited-fresh-test", "batch.json")) &&
                world.Registered.Count == 0, "active strong ledger allowed implicit weak migration");
        });
        await Test("J/weak identity cannot use durable resume API", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using var batch = new MultiAlteringBatchStore(temp.Path);
            await Reject(() => Open(batch, world, identity:
                new CliIdentityContext(null, null, null, "server")), "식별 정보가 부족");
            Check(!File.Exists(temp.Manifest), "weak identity created normal batch");
        });
        foreach (string filename in new[] { "plan-key.json", "0.json" })
            await Test("I/v1 record readable but missing full-batch evidence fails closed " + filename, async () =>
            {
                using var temp = new Temp(); var world = new World(Plans);
                var old = AlteringSessionState.Create(Steel, Identity, 10, 0) with { QueuedWorks = 4 };
                string path = Path.Combine(temp.Path, filename);
                // Actual historical JSON: remove ALL new N02 fields.
                var json = JsonSerializer.SerializeToNode(old)!.AsObject();
                json.Remove("BatchId"); json.Remove("MultiState"); json.Remove("CompletedAt");
                File.WriteAllText(path, json.ToJsonString()); string before = File.ReadAllText(path);
                var loaded = new AlteringSessionStore(path).Load();
                Check(loaded?.QueuedWorks == 4 && loaded.MatchesPlan(Steel), "Old file unreadable");
                using var batch = new MultiAlteringBatchStore(temp.Path);
                await Reject(() => Open(batch, world), "RecoveryRequired");
                Check(File.ReadAllText(path) == before && !File.Exists(temp.Manifest) && world.Registered.Count == 0, "Legacy file discarded");
            });
        await Test("I/proven zero-work stable v1 records migrate without new work", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            foreach (var plan in Plans)
            {
                var path = AlteringSessionStore.MultiPlanPath(temp.Path, plan);
                new AlteringSessionStore(path).Save(AlteringSessionState.Create(plan, Identity, 0, 0));
            }
            // v1 has no authoritative full roster: even all surviving zero
            // checkpoints cannot prove that a completed peer was not deleted.
            // Only an explicitly selected multi-plan may use strict OpenAsync.
            await Reject(() => {
                _ = MultiAlteringBatchStore.ReadPendingPlans(temp.Path);
                return Task.CompletedTask;
            }, "자동 목록 복원");
            Check(!File.Exists(temp.Manifest), "Legacy UI read unexpectedly started a batch");
            using (var batch = new MultiAlteringBatchStore(temp.Path))
            {
                await Open(batch, world);
                Check(batch.IsResuming == false && batch.CompletedPlans(Plans).Count == 0,
                    "Zero-work migration claimed completion");
                await new Harness(batch, Plans, world).Run();
                batch.Complete(); batch.Cleanup(); batch.AcknowledgeClearedPlan();
                foreach (var plan in Plans)
                {
                    string prior = AlteringSessionStore.MultiPlanPath(temp.Path, plan);
                    Check(!File.Exists(prior), "Migrated legacy record left blocking next new batch");
                    Check(File.Exists(System.IO.Path.Combine(temp.Path, "legacy-archive", batch.BatchId,
                        System.IO.Path.GetFileName(prior))), "Legacy history was deleted instead of archived");
                }
            }
            using var fresh = new MultiAlteringBatchStore(temp.Path); await Open(fresh, world);
            Check(fresh.CompletedPlans(Plans).Count == 0, "Closed migration prevented new batch");
        });
        await Test("I/zero-work legacy still blocks changed inventory and live works", async () =>
        {
            foreach (bool changedInventory in new[] { true, false })
            {
                using var temp = new Temp(); var world = new World(Plans);
                foreach (var plan in Plans)
                    new AlteringSessionStore(AlteringSessionStore.MultiPlanPath(temp.Path, plan))
                        .Save(AlteringSessionState.Create(plan, Identity, 0, 0));
                if (changedInventory) world.Counts[Wood.OutputName] = 10;
                else world.AddCompleted(Wood, 1);
                using var batch = new MultiAlteringBatchStore(temp.Path);
                await Reject(() => Open(batch, world), "RecoveryRequired");
                Check(!File.Exists(temp.Manifest) && world.Registered.Count == 0,
                    "Changed legacy history silently authorized new registration");
            }
        });
        await Test("I/zero-work legacy mismatched character cannot migrate", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            foreach (var plan in Plans)
                new AlteringSessionStore(AlteringSessionStore.MultiPlanPath(temp.Path, plan))
                    .Save(AlteringSessionState.Create(plan, Identity, 0, 0));
            using var batch = new MultiAlteringBatchStore(temp.Path);
            await Reject(() => Open(batch, world, identity: Identity with { CharacterId = "other" }),
                "RecoveryRequired");
            Check(!File.Exists(temp.Manifest) && world.Registered.Count == 0,
                "Cross-character legacy history was treated as a fresh batch");
        });
        await Test("I/incomplete legacy roster never reprocesses deleted completed peer", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            foreach (var plan in new[] { Steel, Rice })
                new AlteringSessionStore(AlteringSessionStore.MultiPlanPath(temp.Path, plan))
                    .Save(AlteringSessionState.Create(plan, Identity, 0, 0));
            using var batch = new MultiAlteringBatchStore(temp.Path);
            await Reject(() => Open(batch, world), "RecoveryRequired");
            // The single-altering entry must not mistake absent batch.json for
            // permission to bypass an incomplete prior multi-plan roster.
            await Reject(() => {
                _ = MultiAlteringBatchStore.ReadPendingPlans(temp.Path);
                return Task.CompletedTask;
            }, "RecoveryRequired");
            Check(!File.Exists(temp.Manifest) && world.Registered.Count == 0,
                "Incomplete legacy roster reset missing completed wood");
        });
        await Test("I/orphan initial temp cannot become a fresh batch", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans); File.WriteAllText(temp.Manifest + ".tmp", "partial");
            using var batch = new MultiAlteringBatchStore(temp.Path);
            await Reject(() => Open(batch, world), "RecoveryRequired"); Check(world.Registered.Count == 0, "Orphan temp authorized input");
        });
        await Test("J/termination during cleanup preserves whole completion", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans); int deletions = 0;
            using (var batch = new MultiAlteringBatchStore(temp.Path, p => { if (p == "cleanup" && ++deletions == 2) throw new IOException("cleanup cut"); }))
            {
                await Open(batch, world); await new Harness(batch, Plans, world).Run(); batch.Complete();
                string deps = Path.Combine(temp.Path, "dependencies"); Directory.CreateDirectory(deps);
                File.WriteAllText(Path.Combine(deps, "one.json"), "1"); File.WriteAllText(Path.Combine(deps, "two.json"), "2");
                await Reject(() => { batch.Cleanup(); return Task.CompletedTask; }, "cleanup cut");
            }
            world.Registered.Clear();
            using var restored = new MultiAlteringBatchStore(temp.Path); await Open(restored, world);
            Check(restored.IsTerminal && restored.CompletedPlans(Plans).Count == 3, "Terminal evidence lost during cleanup");
            await new Harness(restored, Plans, world).Run(); restored.Cleanup();
            Check(world.Registered.Count == 0, "Cleanup crash restarted production");
        });
        await Test("K/new equal item plan has independent batch ID and registrations", async () =>
        {
            using var temp = new Temp(); var world = new World(new[] { Wood }); string oldId;
            using (var batch = new MultiAlteringBatchStore(temp.Path))
            { await Open(batch, world, new[] { Wood }); oldId = batch.BatchId; await Finish(Wood, batch, world); batch.Complete(); batch.Cleanup(); batch.AcknowledgeClearedPlan(); }
            world.Registered.Clear();
            using var next = new MultiAlteringBatchStore(temp.Path); await Open(next, world, new[] { Wood }); await Finish(Wood, next, world);
            Check(next.BatchId != oldId && world.Registered.Count == 10 && world.Counts[Wood.OutputName] == 200, "Normal equal new batch blocked");
        });
        await Test("L/mixed seven slots, round robin, whole-facility receipt and reuse", async () =>
        {
            using var temp = new Temp();
            var a = Steel with { TargetQuantity = 40 }; var b = new AlteringPlan(Steel.FacilityName, "철괴", 70, 10, false);
            var plans = new[] { a, b }; var world = new World(plans) { ImmediateCompletion = false, InjectPartial = true };
            using var batch = new MultiAlteringBatchStore(temp.Path); await Open(batch, world, plans);
            await new Harness(batch, plans, world).Run();
            Check(world.Registered.Take(7).SequenceEqual(new[] { a.DisplayName, b.DisplayName, a.DisplayName, b.DisplayName, a.DisplayName, b.DisplayName, a.DisplayName }), "Round robin changed");
            Check(world.MaxQueue == 7 && world.PartialSeen && world.Receipts == 2 && world.PartialReceipts == 0, "Seven-slot/receipt policy changed");
            Check(world.Registered.Count(x => x == a.DisplayName) == 4 && world.Registered.Count(x => x == b.DisplayName) == 7, "Completed item registered again");
            Check(world.QueueDirectives.Count(x => x == AlteringFacilityEntryDirective.FreshMoveRequired) == 1 &&
                world.QueueDirectives.Skip(1).All(x => x == AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite), "Fresh/Reuse changed");
        });
        await Test("dependency collector persists a completed MAIN peer before child registration", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using var batch = new MultiAlteringBatchStore(temp.Path); await Open(batch, world);
            batch.PlanStore(Wood).Save(batch.Session(Wood) with { QueuedWorks = 10, MultiState = MultiAlteringItemState.InProgress });
            world.AddCompleted(Wood, 10);
            var lane = new FacilityLaneState(await world.WorksAsync(default));
            var scheduler = new MultiAlteringDependencyScheduler(world, world, Identity, temp.Path,
                Delay, 3, laneState: lane, beforeReceipt: batch.BeginReceiptAsync,
                afterReceipt: (p, ct) => batch.ConfirmReceiptAsync(p, world, ct));
            // Existing main work satisfies the child's request without registering.
            await scheduler.RunAsync(Wood, 0, 100, new Resolver(), default);
            Check(batch.Session(Wood).MultiState == MultiAlteringItemState.Completed && world.Registered.Count == 0,
                "Dependency receipt lost main completion");
            world.Counts[Wood.OutputName] = 40;
            Check(await new Harness(batch, Plans, world).Automation(Wood).RunBatchAsync(Wood, 1, default) == AlteringRunResult.Completed,
                "Consumed dependency receipt reopened main");
        });
        await Test("single-altering retains successful checkpoint deletion", async () =>
        {
            using var temp = new Temp(); var world = new World(new[] { Wood });
            var store = new AlteringSessionStore(Path.Combine(temp.Path, "single.json")); var state = AlteringSessionState.Create(Wood, Identity, 0, 0); store.Save(state);
            await new AlteringAutomation(world, world, Delay, 3, sessionStore: store, session: state).RunAsync(Wood, default);
            Check(store.Load() is null && world.Registered.Count == 10, "Single production lifetime changed");
        });
        await Test("cancelled token before receipt cannot perform IO/input", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using var batch = new MultiAlteringBatchStore(temp.Path); await Open(batch, world); string before = File.ReadAllText(temp.Manifest);
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await Reject(() => batch.BeginReceiptAsync(Wood, cts.Token), "OperationCanceledException");
            await Reject(() => new Harness(batch, Plans, world).Automation(Wood).RunBatchAsync(Wood, 1, cts.Token), "OperationCanceledException");
            Check(File.ReadAllText(temp.Manifest) == before && world.Registered.Count == 0 && world.Receipts == 0, "Cancelled run performed input/save");
        });
        await Test("F10 cancellation delivery does not wait for blocked checkpoint IO", async () =>
        {
            using var temp = new Temp(); var world = new World(new[] { Wood });
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            bool armed = false;
            using var batch = new MultiAlteringBatchStore(temp.Path, p =>
            {
                if (armed && p == "write")
                { entered.Set(); Check(release.Wait(TimeSpan.FromSeconds(5)), "Checkpoint wait timed out"); }
            });
            await Open(batch, world, new[] { Wood }); armed = true;
            using var cts = new CancellationTokenSource(); var auto = new Harness(batch, new[] { Wood }, world).Automation(Wood);
            var running = Task.Run(() => auto.RunBatchAsync(Wood, 1, cts.Token));
            Check(entered.Wait(TimeSpan.FromSeconds(5)), "Worker never entered checkpoint IO");
            try
            {
                var cancel = Task.Run(cts.Cancel);
                await cancel.WaitAsync(TimeSpan.FromSeconds(1));
                Check(cts.IsCancellationRequested && world.Registered.Count == 0, "F10 waited for filesystem or sent input");
            }
            finally { release.Set(); }
            await Reject(async () => await running.WaitAsync(TimeSpan.FromSeconds(5)), "OperationCanceledException");
            Check(world.Registered.Count == 0 && world.Receipts == 0, "Post-cancellation input occurred");
        });
        await Test("full completion save failure never authorizes cleanup or a fresh equal plan", async () =>
        {
            using var temp = new Temp(); var world = new World(new[] { Wood }); bool armed = false;
            using (var batch = new MultiAlteringBatchStore(temp.Path, p => { if (armed && p == "replace") throw new IOException("terminal cut"); }))
            {
                await Open(batch, world, new[] { Wood }); await Finish(Wood, batch, world); armed = true;
                await Reject(() => { batch.Complete(); return Task.CompletedTask; }, "terminal cut");
                await Reject(() => { batch.Cleanup(); return Task.CompletedTask; }, "진행 배치 정리 금지");
            }
            world.Registered.Clear();
            using var restored = new MultiAlteringBatchStore(temp.Path); await Open(restored, world, new[] { Wood });
            await Finish(Wood, restored, world); restored.Complete();
            Check(world.Registered.Count == 0 && restored.IsTerminal, "Terminal save cut reopened item");
        });
        await Test("restart UI roster includes completed and pending plans until acknowledged", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            Check(MultiAlteringBatchStore.ReadPendingPlans(temp.Path).Count == 0, "Missing record invented a roster");
            using (var batch = new MultiAlteringBatchStore(temp.Path))
            {
                await Open(batch, world); await Finish(Wood, batch, world);
                Check(MultiAlteringBatchStore.ReadPendingPlans(temp.Path).SequenceEqual(Plans), "Restart roster lost a completed peer");
                await new Harness(batch, Plans, world).Run(); batch.Complete(); batch.Cleanup();
                Check(MultiAlteringBatchStore.ReadPendingPlans(temp.Path).SequenceEqual(Plans), "Unacknowledged completion became a fresh selection");
                batch.AcknowledgeClearedPlan();
                Check(MultiAlteringBatchStore.ReadPendingPlans(temp.Path).Count == 0, "Closed batch blocked new UI selection");
            }
        });
        await Test("mismatched item batch ID is preserved and stops restoration", async () =>
        {
            using var temp = new Temp(); var world = new World(Plans);
            using (var batch = new MultiAlteringBatchStore(temp.Path)) await Open(batch, world);
            var json = JsonSerializer.SerializeToNode(JsonSerializer.Deserialize<MultiAlteringBatch>(File.ReadAllText(temp.Manifest)))!;
            json["Items"]![0]!["BatchId"] = Guid.NewGuid().ToString("N");
            string changed = json.ToJsonString(); File.WriteAllText(temp.Manifest, changed);
            using var bad = new MultiAlteringBatchStore(temp.Path);
            await Reject(() => Open(bad, world), "불일치/손상");
            await Reject(() => { _ = MultiAlteringBatchStore.ReadPendingPlans(temp.Path); return Task.CompletedTask; }, "불일치/손상");
            Check(File.ReadAllText(temp.Manifest) == changed && world.Registered.Count == 0, "Batch-ID conflict reset production");
        });
        await Test("two live instances cannot own the same batch", async () =>
        {
            using var temp = new Temp(); using var first = new MultiAlteringBatchStore(temp.Path);
            await Reject(() => { using var second = new MultiAlteringBatchStore(temp.Path); return Task.CompletedTask; }, "IOException");
        });
        Console.WriteLine($"N02: {passed} deterministic tests passed; temporary files; fake game only.");
    }

    sealed class Temp : IDisposable
    {
        public string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "n02-" + Guid.NewGuid().ToString("N"));
        public string Manifest => System.IO.Path.Combine(Path, "batch.json");
        public Temp() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
    sealed class Harness
    {
        readonly MultiAlteringBatchStore batch; readonly IReadOnlyList<AlteringPlan> plans; readonly World world;
        readonly FacilityLaneState lane; readonly Dictionary<string, AlteringAutomation> automations = new();
        public Harness(MultiAlteringBatchStore batch, IReadOnlyList<AlteringPlan> plans, World world)
        {
            this.batch = batch; this.plans = plans; this.world = world;
            lane = new FacilityLaneState(world.Works.ToArray());
            foreach (var plan in plans)
            {
                var store = batch.PlanStore(plan);
                automations.Add(MultiAlteringBatchStore.Key(plan), new AlteringAutomation(world, world, Delay, 3,
                    sessionStore: store, session: store.Load(), facilityState: lane,
                    onConfirmedReceipt: (p, works) => lane.Observe(p.FacilityName, works, allowShrink: true),
                    beforeReceipt: batch.BeginReceiptAsync,
                    afterReceipt: (p, ct) => batch.ConfirmReceiptAsync(p, world, ct)));
            }
        }
        public AlteringAutomation Automation(AlteringPlan p) => automations[MultiAlteringBatchStore.Key(p)];
        public Task Run() => new MultiAlteringCoordinator(lane, restoreCompleted: p => batch.CompletedPlans(p)).RunAsync(plans,
            async (p, slots, ct) =>
            {
                var auto = Automation(p); int before = auto.ConfirmedRegistrationsThisRun;
                var result = await auto.RunBatchAsync(p, slots, ct);
                lane.NoteRegistration(p, FacilityLaneOwner.Main, auto.ConfirmedRegistrationsThisRun - before);
                return result == AlteringRunResult.Completed;
            }, world.WorksAsync, world.Delay, default);
        public async Task RunUntilPartial()
        {
            // Run real registration paths, with remaining facilities left running.
            await Finish(Wood, batch, world);
            world.ImmediateCompletion = false;
            foreach (var (p, count) in new[] { (Steel, 4), (Rice, 3) })
                for (int i = 0; i < count; i++) await Automation(p).RunBatchAsync(p, 1, default);
            using var f10 = new CancellationTokenSource(); f10.Cancel();
            await Reject(() => Automation(Steel).RunBatchAsync(Steel, 1, f10.Token), "OperationCanceledException");
            world.ImmediateCompletion = true;
        }
    }
    sealed class Resolver : IAlteringSupplyResolver
    { public Task ResolveAsync(AlteringPlan p, AlteringRecipe r, int remaining, CancellationToken ct) => throw new Exception("Unexpected resolver"); }
    sealed class World : IAlteringData, IAlteringScreen, IAlteringCoordinatorQueueScreen, IAlteringCoordinatorReceiptScreen, IAlteringReceiptBoundaryScreen
    {
        readonly IReadOnlyList<AlteringPlan> plans;
        public readonly List<AlteringWork> Works = new();
        public readonly Dictionary<string, long> Counts = new();
        public readonly List<string> Registered = new();
        public readonly List<AlteringFacilityEntryDirective> QueueDirectives = new();
        public bool ImmediateCompletion = true, InjectPartial, PartialSeen;
        public int MaxQueue, Receipts, PartialReceipts;
        public string? FailInventory;
        public Action? BeforeReceipt, AfterReceipt, BeforeReceiptPrompt;
        public World(IReadOnlyList<AlteringPlan> plans) { this.plans = plans; foreach (var p in plans) Counts[p.OutputName] = 0; }
        public Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<AlteringRecipe>>(plans.Select(p => new AlteringRecipe(p.DisplayName, true, p.ProducedPerWork, null, Array.Empty<AlteringIngredient>(), p.FacilityName)).ToArray()); }
        public Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (ImmediateCompletion) CompleteAll();
            if (InjectPartial && !PartialSeen && Works.Count == 7)
            { Works[0] = Works[0] with { State = "Completed", IsCompleted = true, RemainingSeconds = 0 }; PartialSeen = true; }
            return Task.FromResult<IReadOnlyList<AlteringWork>>(Works.ToArray());
        }
        public Task<long> ItemCountAsync(string name, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); if (FailInventory == name) throw new IOException("inventory read failure"); return Task.FromResult(Counts.GetValueOrDefault(name)); }
        public void AddCompleted(AlteringPlan p, int count)
        { for (int i = 0; i < count; i++) Works.Add(new(p.OutputName, p.FacilityName, "Completed", true, 0)); }
        void CompleteAll() { for (int i = 0; i < Works.Count; i++) Works[i] = Works[i] with { State = "Completed", IsCompleted = true, RemainingSeconds = 0 }; }
        public Task Delay(TimeSpan _, CancellationToken ct) { ct.ThrowIfCancellationRequested(); CompleteAll(); return Task.CompletedTask; }
        public Task QueueAsync(AlteringPlan p, Action reserve, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Registered.Add(p.DisplayName); Works.Add(new(p.OutputName, p.FacilityName, "InProgress", false, 5)); MaxQueue = Math.Max(MaxQueue, Works.Count(w => w.FacilityName == p.FacilityName)); return Task.CompletedTask; }
        public Task QueueAsync(AlteringPlan p, AlteringFacilityEntryDirective directive, Action reserve, CancellationToken ct)
        { QueueDirectives.Add(directive); return QueueAsync(p, reserve, ct); }
        public Task<bool> CollectAsync(AlteringPlan p, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); BeforeReceipt?.Invoke(); Receipts++;
            if (Works.Any(w => w.FacilityName == p.FacilityName && !w.IsCompleted)) PartialReceipts++;
            foreach (var work in Works.Where(w => w.FacilityName == p.FacilityName && w.IsCompleted))
            { var source = plans.First(x => x.FacilityName == work.FacilityName && x.OutputName == work.DisplayName); Counts[source.OutputName] += source.ProducedPerWork; }
            Works.RemoveAll(w => w.FacilityName == p.FacilityName && w.IsCompleted); AfterReceipt?.Invoke(); return Task.FromResult(true);
        }
        public Task<bool> CollectAsync(AlteringPlan p, AlteringFacilityEntryDirective d, CancellationToken ct) => CollectAsync(p, ct);
        public async Task<bool> CollectAsyncAtBoundary(
            AlteringPlan p, AlteringFacilityEntryDirective d,
            Func<CancellationToken, Task> beforeReceiveInput, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            BeforeReceiptPrompt?.Invoke(); // Simulate fallible OCR/preflight with no receive input.
            await beforeReceiveInput(ct);
            return await CollectAsync(p, d, ct);
        }
        public Task<bool> CollectAfterTravelAsync(AlteringPlan p, CancellationToken ct) => CollectAsync(p, ct);
        public void Dispose() { }
    }
}
