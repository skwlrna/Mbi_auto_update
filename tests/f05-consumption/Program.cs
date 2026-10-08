using FishingAutomation;
using System.Text.Json.Nodes;

static class Program
{
    static readonly CliIdentityContext Identity = new("character", "tester", "account", "realm");
    static readonly AlteringPlan Wood = new("목재 가공 시설", "목재", 100, 10, false);
    static readonly AlteringPlan Steel = new("금속 가공 시설", "강철괴", 100, 10, false);
    static readonly AlteringPlan Rice = new("식재료 가공 시설", "불린쌀", 100, 10, false);
    static readonly AlteringPlan[] Plans = { Wood, Steel, Rice };
    static int passed;
    static void Assert(bool condition, string label)
    { if (!condition) throw new Exception(label); }
    static async Task Reject(Func<Task> action, string contains)
    {
        try { await action(); }
        catch (Exception ex) when (ex.ToString().Contains(contains, StringComparison.Ordinal)) { return; }
        throw new Exception("Expected error containing " + contains);
    }
    static async Task Test(string name, Func<Task> action)
    { await action(); ++passed; Console.WriteLine("PASS F05 " + name); }
    static Task Open(MultiAlteringBatchStore store, World world)
        => store.OpenAsync(Plans, Identity, world, default);
    static MultiAlteringConsumptionLedger Ledger(MultiAlteringBatchStore store, World world,
        params AlteringPlan[] producers)
    {
        var ledger = new MultiAlteringConsumptionLedger(world.ReadCounts, store);
        foreach (var p in producers) ledger.RegisterProducer(p, (_, _) => { });
        return ledger;
    }
    static async Task Main()
    {
        await Test("one registration persists producer debit and transaction ID", async () =>
        {
            using var temp = new Temp();
            var world = new World(); using var batch = new MultiAlteringBatchStore(temp.Dir);
            await Open(batch, world);
            var ledger = Ledger(batch, world, Wood);
            var before = await ledger.CaptureBeforeRegistrationAsync(Steel, default);
            Assert(before.TransactionId is not null && File.ReadAllText(temp.Manifest).Contains("PreparedConsumption"),
                "Before-image not saved before irreversible action");
            world.Set(Wood.OutputName, 97);
            await ledger.CommitAfterRegistrationAsync(Steel, before, default);
            Assert(batch.Session(Wood).CreditedInternalConsumptionQuantity == 3,
                "Confirmed producer debit not saved");
            var json = File.ReadAllText(temp.Manifest);
            Assert(json.Contains(before.TransactionId!) && json.Contains("AppliedConsumption") &&
                json.Contains("\"PreparedConsumption\": null"), "Atomic applied journal missing");
        });
        await Test("same transaction replay is exactly once", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using var batch = new MultiAlteringBatchStore(temp.Dir); await Open(batch, world);
            var ledger = Ledger(batch, world, Wood);
            var before = await ledger.CaptureBeforeRegistrationAsync(Steel, default);
            world.Set(Wood.OutputName, 95);
            await ledger.CommitAfterRegistrationAsync(Steel, before, default);
            world.Set(Wood.OutputName, 92);
            await ledger.CommitAfterRegistrationAsync(Steel, before, default);
            Assert(batch.Session(Wood).CreditedInternalConsumptionQuantity == 5,
                "Replay double credited producer");
        });
        await Test("multiple producer decrements atomically in one manifest", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using var batch = new MultiAlteringBatchStore(temp.Dir); await Open(batch, world);
            var ledger = Ledger(batch, world, Wood, Rice);
            var before = await ledger.CaptureBeforeRegistrationAsync(Steel, default);
            world.Set(Wood.OutputName, 96); world.Set(Rice.OutputName, 98);
            await ledger.CommitAfterRegistrationAsync(Steel, before, default);
            Assert(batch.Session(Wood).CreditedInternalConsumptionQuantity == 4 &&
                batch.Session(Rice).CreditedInternalConsumptionQuantity == 2,
                "Mixed producer consumption lost");
            Assert(File.ReadAllText(temp.Manifest).Contains(before.TransactionId!),
                "Mixed transaction not journaled");
        });
        await Test("stale producer stage save cannot roll back durable consumption", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using var batch = new MultiAlteringBatchStore(temp.Dir); await Open(batch, world);
            var stale = batch.Session(Wood);
            var ledger = Ledger(batch, world, Wood);
            var before = await ledger.CaptureBeforeRegistrationAsync(Steel, default);
            world.Set(Wood.OutputName, 93);
            await ledger.CommitAfterRegistrationAsync(Steel, before, default);
            batch.PlanStore(Wood).Save(stale with { Stage = "old stage snapshot" });
            Assert(batch.Session(Wood).CreditedInternalConsumptionQuantity == 7,
                "Stale checkpoint erased credit");
        });
        await Test("crash with prepared debit fails closed after process restart", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using (var batch = new MultiAlteringBatchStore(temp.Dir))
            {
                await Open(batch, world);
                _ = await Ledger(batch, world, Wood).CaptureBeforeRegistrationAsync(Steel, default);
                world.Set(Wood.OutputName, 98);
            }
            using var restarted = new MultiAlteringBatchStore(temp.Dir);
            await Reject(() => Open(restarted, world), "미확정 소비 거래");
            Assert(File.ReadAllText(temp.Manifest).Contains("PreparedConsumption"),
                "Prepared transaction lost on crash");
        });
        await Test("write failure after input preserves prepared record and refuses resume", async () =>
        {
            using var temp = new Temp(); var world = new World(); bool fail = false;
            using (var batch = new MultiAlteringBatchStore(temp.Dir,
                stage => { if (fail && stage == "replace") throw new IOException("injected replace"); }))
            {
                await Open(batch, world);
                var ledger = Ledger(batch, world, Wood);
                var before = await ledger.CaptureBeforeRegistrationAsync(Steel, default);
                world.Set(Wood.OutputName, 96);
                fail = true;
                await Reject(() => ledger.CommitAfterRegistrationAsync(Steel, before, default), "injected replace");
                Assert(batch.Session(Wood).CreditedInternalConsumptionQuantity == 0,
                    "Failed atomic replace published uncommitted credit");
            }
            using var restarted = new MultiAlteringBatchStore(temp.Dir);
            await Reject(() => Open(restarted, world), "미확정 소비 거래");
        });
        await Test("unresolved debit cannot be replaced with new prepare or receipt", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using var batch = new MultiAlteringBatchStore(temp.Dir); await Open(batch, world);
            var ledger = Ledger(batch, world, Wood);
            _ = await ledger.CaptureBeforeRegistrationAsync(Steel, default);
            await Reject(() => ledger.CaptureBeforeRegistrationAsync(Steel, default), "미확정 거래");
            await Reject(() => batch.BeginReceiptAsync(Steel, default), "미확정 등록 소비 거래");
        });
        await Test("confirmed durable credits survive clean process restart", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using (var batch = new MultiAlteringBatchStore(temp.Dir))
            {
                await Open(batch, world);
                var ledger = Ledger(batch, world, Wood);
                var before = await ledger.CaptureBeforeRegistrationAsync(Steel, default);
                world.Set(Wood.OutputName, 94);
                await ledger.CommitAfterRegistrationAsync(Steel, before, default);
            }
            using var fresh = new MultiAlteringBatchStore(temp.Dir); await Open(fresh, world);
            Assert(fresh.Session(Wood).CreditedInternalConsumptionQuantity == 6,
                "Durable credit missing after new instance opened");
        });
        await Test("applied journal and pending MAIN registration halt ambiguous restart", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using (var batch = new MultiAlteringBatchStore(temp.Dir))
            {
                await Open(batch, world);
                batch.PlanStore(Steel).Save(batch.Session(Steel) with {
                    PendingRegistration = true, PendingBeforeMatchingCount = 0 });
                var ledger = Ledger(batch, world, Wood);
                var before = await ledger.CaptureBeforeRegistrationAsync(Steel, default);
                batch.PlanStore(Steel).Save(batch.Session(Steel) with {
                    PendingConsumptionTransactionId = before.TransactionId
                });
                world.Set(Wood.OutputName, 99);
                await ledger.CommitAfterRegistrationAsync(Steel, before, default);
            }
            using var fresh = new MultiAlteringBatchStore(temp.Dir);
            await Reject(() => Open(fresh, world), "등록 확정 전 중단");
        });
        await Test("applied journal and pending recursive child prevent duplicate registration", async () =>
        {
            using var temp = new Temp(); var world = new World();
            var child = new AlteringPlan("기타 가공 시설", "중간재료", 10, 10, false);
            using (var batch = new MultiAlteringBatchStore(temp.Dir))
            {
                await Open(batch, world);
                var ledger = Ledger(batch, world, Wood);
                var before = await ledger.CaptureBeforeRegistrationAsync(child, default);
                world.Set(Wood.OutputName, 99);
                await ledger.CommitAfterRegistrationAsync(child, before, default);
            }
            var dep = System.IO.Path.Combine(temp.Dir, "dependencies");
            Directory.CreateDirectory(dep);
            new AlteringSessionStore(AlteringSessionStore.MultiPlanPath(dep, child)).Save(
                AlteringSessionState.Create(child, Identity, 0, 0) with {
                    PendingRegistration = true,
                    PendingConsumptionTransactionId =
                        JsonNode.Parse(File.ReadAllText(temp.Manifest))!["AppliedConsumption"]![0]!["TransactionId"]!.GetValue<string>()
                });
            using var reopened = new MultiAlteringBatchStore(temp.Dir);
            await Reject(() => Open(reopened, world), "중간재료 등록 확정 전 종료");
        });
        await Test("old applied transaction cannot poison unrelated later pending registration", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using (var batch = new MultiAlteringBatchStore(temp.Dir))
            {
                await Open(batch, world);
                var ledger = Ledger(batch, world, Wood);
                var old = await ledger.CaptureBeforeRegistrationAsync(Steel, default);
                world.Set(Wood.OutputName, 97);
                await ledger.CommitAfterRegistrationAsync(Steel, old, default);
                batch.PlanStore(Steel).Save(batch.Session(Steel) with {
                    QueuedWorks = 1, PendingRegistration = true,
                    PendingConsumptionTransactionId = null });
            }
            using var next = new MultiAlteringBatchStore(temp.Dir);
            await Open(next, world);
            Assert(next.Session(Steel).PendingRegistration,
                "Independent later pending registration was incorrectly treated as applied replay");
        });
        await Test("v1 partially registered manifest cannot bypass unjournaled history", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using (var batch = new MultiAlteringBatchStore(temp.Dir))
            {
                await Open(batch, world);
                batch.PlanStore(Steel).Save(batch.Session(Steel) with { QueuedWorks = 2 });
            }
            var node = JsonNode.Parse(File.ReadAllText(temp.Manifest))!;
            node["Version"] = 1;
            File.WriteAllText(temp.Manifest, node.ToJsonString());
            using var reopened = new MultiAlteringBatchStore(temp.Dir);
            await Reject(() => Open(reopened, world), "F05 이전 배치");
        });
        await Test("v1 provable zero-work batch upgrades without input", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using (var batch = new MultiAlteringBatchStore(temp.Dir)) await Open(batch, world);
            var node = JsonNode.Parse(File.ReadAllText(temp.Manifest))!;
            node["Version"] = 1;
            File.WriteAllText(temp.Manifest, node.ToJsonString());
            using var reopened = new MultiAlteringBatchStore(temp.Dir);
            await Open(reopened, world);
            Assert(reopened.IsResuming && JsonNode.Parse(File.ReadAllText(temp.Manifest))!["Version"]!.GetValue<int>() == 2,
                "Unregistered legacy transaction journal not upgraded");
        });
        await Test("cancelled capture cannot authorize input or create a debit", async () =>
        {
            using var temp = new Temp(); var world = new World();
            using var batch = new MultiAlteringBatchStore(temp.Dir); await Open(batch, world);
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await Reject(() => Ledger(batch, world, Wood).CaptureBeforeRegistrationAsync(Steel, cts.Token),
                "OperationCanceledException");
            Assert(!File.ReadAllText(temp.Manifest).Contains("\"PreparedConsumption\": {"),
                "Cancelled capture created a prepared transaction");
        });
        Console.WriteLine($"F05: {passed} deterministic journal tests passed; fake inventory/file IO only.");
    }

    sealed class Temp : IDisposable
    {
        internal readonly string Dir = Path.Combine(Path.GetTempPath(), "f05-" + Guid.NewGuid().ToString("N"));
        internal string Manifest => Path.Combine(Dir, "batch.json");
        internal Temp() => Directory.CreateDirectory(Dir);
        public void Dispose() { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); }
    }
    sealed class World : IAlteringData
    {
        readonly Dictionary<string,long> counts = new(StringComparer.Ordinal)
        {
            [Wood.OutputName] = 100, [Steel.OutputName] = 0, [Rice.OutputName] = 100
        };
        internal void Set(string name,long count) => counts[name] = count;
        internal Task<IReadOnlyDictionary<string,long>> ReadCounts(
            IReadOnlyList<string> names,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyDictionary<string,long>>(
                names.ToDictionary(n=>n,n=>counts[n],StringComparer.Ordinal));
        }
        public Task<long> ItemCountAsync(string name,CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(counts.GetValueOrDefault(name)); }
        public Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<AlteringWork>>([]); }
        public Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<AlteringRecipe>>([]); }
    }
}
