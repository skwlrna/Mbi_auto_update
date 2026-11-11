using FishingAutomation;
using System.Text.Json;

internal static class CraftingFoodStockCapacityTests
{
    internal static async Task RunAsync(Action<bool, string> check, AppLog log)
    {
        var messages = new List<string>();
        var cli = new MabinogiMobileCli(log, true, (_, _) => Task.FromResult(new CliProcessOutput(0,
            JsonSerializer.Serialize(new[] {
                new { DisplayName="고기", Location="inventory", Count=78L },
                new { DisplayName="고기", Location="character_storage", Count=20L },
                new { DisplayName="고기", Location="account_storage", Count=8L },
                new { DisplayName="담백한 고기", Location="inventory", Count=999L }
            }), "")));
        var data = new CraftingCliData(cli);
        check(await data.MaterialCountWithLoadingRetryAsync("고기", CancellationToken.None, messages.Add) == 106 &&
              await data.InventoryOnlyCountWithLoadingRetryAsync("고기", CancellationToken.None) == 78 &&
              messages.Any(x => x.Contains("가방=78") && x.Contains("캐릭터 보관함=20") && x.Contains("공용 보관함=8") && x.Contains("합계=106")),
            "food stock sums all three locations by exact name while inventory-only consumers stay unchanged");
        var storageRow = CraftingFoodBatchPolicy.FindGatherRow("감자",
            new[] { new DungeonVisionBot.CraftingQuestDeficit("감자",20,60,0),
                    new DungeonVisionBot.CraftingQuestDeficit("양파",30,60,0) },
            new[] { new DungeonVisionBot.CraftingQuestDeficit("",0,60,711) },
            new HashSet<string>(new[] {"감자","양파"},StringComparer.Ordinal));
        check(storageRow?.Targets.Count==2,
            "bag-only displayed counters do not block a safe all-gatherable group tracked by combined stock");
        CraftingFoodRecipeCatalog.TryGet("카레라이스", out var curry);
        foreach (var test in new (long Meat, long Powder, int Expected)[] { (106L,20L,10), (78L,20L,7), (10L,20L,1), (9L,20L,0), (106L,2L,1), (106L,0L,0) })
        {
            int reads = 0;
            var result = await CraftingFoodBatchPolicy.SelectBatchAsync(curry,10,(names,_) =>
            {
                reads++;
                IReadOnlyDictionary<string,long> counts=names.ToDictionary(x=>x,x=>x switch {
                    "고기"=>test.Meat, "당근"=>30L, "카레 가루"=>test.Powder, _=>0L });
                return Task.FromResult(counts);
            },CancellationToken.None);
            check(result.Crafts==test.Expected && reads==1,
                $"curry capacity uses one stock snapshot and permits one craft: meat={test.Meat}, powder={test.Powder}, crafts={test.Expected}");
        }
        CraftingFoodRecipeCatalog.TryGet("사과 생크림케이크",out var cake);
        var nested = await CraftingFoodBatchPolicy.SelectBatchAsync(cake,10,(names,_) =>
            Task.FromResult<IReadOnlyDictionary<string,long>>(names.ToDictionary(x=>x,x=>x switch {
                "설탕"=>11L, "밀가루"=>4L, _=>0L })),CancellationToken.None);
        check(nested.Crafts==1,"batch scaling reserves direct sugar before cream processing and gathers renewable raw inputs");
        foreach (var scenario in new (long Bag, long Store, int Target, int Made)[] {
            (78,28,10,10), (78,0,30,7), (10,0,30,1), (0,0,30,0) })
        {
            var bag = new Dictionary<string,long> { ["고기"]=scenario.Bag, ["밥"]=300,
                ["당근"]=300,["감자"]=800,["카레 가루"]=200,["카레라이스"]=0 };
            var store = new Dictionary<string,long> { ["고기"]=scenario.Store };
            var screen = new CapacityScreen(bag,store,curry);
            string Read(string cmd) => cmd switch {
                "get_items" => JsonSerializer.Serialize(bag.Select(x=>new {DisplayName=x.Key,Location="inventory",Count=x.Value})
                    .Concat(store.Select(x=>new {DisplayName=x.Key,Location="character_storage",Count=x.Value}))),
                "get_craftable_items"=>JsonSerializer.Serialize(new {items=new[]{new {DisplayName="카레라이스",ProducedPerCraft=1,Craftable=true}}}),
                _=>throw new Exception("Unexpected query/action: "+cmd)
            };
            var liveCli=new MabinogiMobileCli(log,true,(cmd,_)=>Task.FromResult(new CliProcessOutput(0,Read(cmd),"")));
            liveCli.RunFilteredQuery=(args,_)=>Task.FromResult(new CliProcessOutput(0,Read(args[0]),""));
            var engine=new CraftingAutomation(new CraftingCliData(liveCli),new GatheringCliData(liveCli),screen,null!,(_,_)=>Task.CompletedTask);
            bool skipped=false;
            try {await engine.RunAsync(new(CraftingCategory.Food,"카레라이스",scenario.Target,1),CancellationToken.None);}
            catch(CraftingMaterialAcquisitionException){skipped=true;}
            check(engine.CompletedCrafts==scenario.Made && bag["카레라이스"]==scenario.Made &&
                  skipped==(scenario.Made<scenario.Target) &&
                  (scenario.Made==0 ? screen.Batches.Count==0 : screen.Batches.SequenceEqual(new[]{scenario.Made})),
                $"real engine creates and verifies the reduced batch before skipping remainder: bag={scenario.Bag}, storage={scenario.Store}, made={scenario.Made}");
        }
        string path=Path.Combine(Path.GetTempPath(),"food-summary-"+Guid.NewGuid()+".log");
        try
        {
            var runLog=new AppLog(path,200);
            runLog.Write("[스케줄러] 시작 · 음식 4종");
            runLog.Write("[제작][순차] 1/4 시작 · 카레라이스");
            runLog.Write("[제작][건너뛰기 사유] 카레라이스 · 카레 가루 0개");
            for(int i=0;i<100;i++)runLog.WriteBackground("[CLI] poll "+i);
            runLog.Write("[제작][순차] 2/4 완료 확인 · 호박 수프");
            string summary=File.ReadAllText(path+".food-summary.log");
            check(summary.Contains("카레 가루 0개") && summary.Contains("호박 수프") && !summary.Contains("[CLI]"),
                "food outcome summary survives runtime log rotation and long later processing");
        }
        finally {File.Delete(path);File.Delete(path+".food-summary.log");}
    }
    private sealed class CapacityScreen(Dictionary<string,long> bag, Dictionary<string,long> store,
        CraftingFoodRecipe recipe) : DungeonVisionBot.ICraftingScreen
    {
        internal List<int> Batches = new();
        public string InputMode => "test";
        public event Action<string>? Log { add {} remove {} }
        public Task CreateQuestAsync(CraftingPlan p,int count,CancellationToken ct)
        {Batches.Add(count); return Task.CompletedTask;}
        public Task<IReadOnlyList<DungeonVisionBot.CraftingQuestDeficit>> ReadQuestDeficitsAsync(CraftingPlan p,CancellationToken ct)
            =>Task.FromResult<IReadOnlyList<DungeonVisionBot.CraftingQuestDeficit>>(Array.Empty<DungeonVisionBot.CraftingQuestDeficit>());
        public Task GatherQuestDeficitAsync(DungeonVisionBot.CraftingQuestDeficit d,CancellationToken ct)=>throw new Exception("No gathering needed");
        public Task RecoverBatchPreparationAsync(CraftingPlan p,CancellationToken ct)=>Task.CompletedTask;
        public Task CloseOverlayAsync(CancellationToken ct)=>Task.CompletedTask;
        public Task ReturnToStationAndCraftAsync(CraftingPlan p,int count,CancellationToken ct)
        {
            foreach(var input in recipe.ForBatch(count))
            {
                long fromBag=Math.Min(bag[input.Name],input.Amount);
                bag[input.Name]-=fromBag;
                long rest=input.Amount-fromBag;
                if(rest>0)store[input.Name]-=rest;
            }
            bag[p.DisplayName]+=count;
            return Task.CompletedTask;
        }
        public void Dispose() {}
    }

}
