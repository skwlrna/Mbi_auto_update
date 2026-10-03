using FishingAutomation;

internal static class BulkGatheringFlowTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (long initial in new long[] { 0, 7 })
        {
            long stock = initial;
            int seeds = 0, hundreds = 0;
            var waits = new List<long>();
            var engine = new InventoryBulkGatheringAutomation(
                _ => Task.FromResult(stock), _ => Task.FromResult(stock),
                _ => { seeds++; stock++; return Task.CompletedTask; },
                _ => { hundreds++; return Task.CompletedTask; },
                (before, minimum, ct) => { waits.Add(minimum); if (minimum == 100) stock += 105; return Task.CompletedTask; });
            await engine.RunAsync(new("통나무", 201), CancellationToken.None);
            check(hundreds == 2 && stock - initial >= 201 && seeds == (initial == 0 ? 1 : 0),
                $"bulk engine repeats 100 quests and accepts overshoot from initial {initial}");
            check(initial != 0 || waits.SequenceEqual(new long[] { 1, 100, 100 }),
                "zero inventory seed naturally stops before switching to 100 quests");
        }
        long lifeStock = 12;
        int lifeCycles = 0;
        var life = new LifeSkillBulkGatheringAutomation(
            _ => Task.FromResult(lifeStock),
            _ => { lifeCycles++; return Task.CompletedTask; },
            (before, targetTotal, _) =>
            {
                lifeStock += lifeCycles == 1 ? 137 : 81;
                return Task.FromResult(lifeStock >= targetTotal);
            });
        await life.RunAsync(new("통나무", 200), CancellationToken.None);
        check(lifeCycles == 2 && lifeStock - 12 == 218,
            "life-skill bulk gathering repeats 100 actions without assuming 100 items per cycle");

        check(LivingSkillGatheringCatalog.TryResolveBulk("물이 든 병", out var water) &&
              water.Category == "일상 채집" && water.TargetName == "우물",
            "water bottle prefers the confirmed well route");
        check(LivingSkillGatheringCatalog.TryResolveBulk("상급 양털+", out var cloudWool) &&
              cloudWool.Category == "양털 깎기" && cloudWool.TargetName == "먹구름 양",
            "wool mapping preserves plus-tier source");
        check(LivingSkillGatheringCatalog.TryResolveBulk("산뜻 버섯 포자", out var herbSpore) &&
              herbSpore.Category == "약초 채집" && herbSpore.TargetName == "산뜻 버섯",
            "herb mushroom spore strips only the spore suffix");
        check(LivingSkillGatheringCatalog.TryResolveBulk("개암 버섯 포자", out var hoeSpore) &&
              hoeSpore.Category == "호미질" && hoeSpore.TargetName == "개암 버섯",
            "hoe mushroom spore stays in hoeing category");
        check(!LivingSkillGatheringCatalog.TryResolveBulk("석양 나비", out _),
            "insect material is not enabled for standalone bulk gathering");
        check(LivingSkillGatheringCatalog.IsQuestOnlyMaterial("석양 나비") &&
              LivingSkillGatheringCatalog.IsQuestOnlyMaterial("유령 반딧불이") &&
              LivingSkillGatheringCatalog.IsQuestOnlyMaterial("황혼잠자리"),
            "confirmed insect materials stay crafting-quest only");
        check(LivingSkillGatheringCatalog.TryResolveBulk("밀", out var wheat) &&
              wheat.Category == "추수" && wheat.TargetName == "밀",
            "harvest material maps to same-name target");
        check(LivingSkillGatheringCatalog.TryResolveBulk("감자", out var potato) &&
              potato.Category == "호미질" && potato.TargetName == "감자",
            "hoeing material maps to same-name target");
        check(LivingSkillGatheringCatalog.TryResolveBulk("최상급 양털", out var darkSheep) &&
              darkSheep.Category == "양털 깎기" && darkSheep.TargetName == "먹구름 양",
            "one sheep source can provide multiple confirmed wool tiers");
        check(AcquisitionMethodPolicy.IsLifeSkill("추천 곤충채집") &&
              AcquisitionMethodPolicy.IsLifeSkill("호미질") &&
              AcquisitionMethodPolicy.IsLifeSkill("양털 깎기"),
            "quest acquisition accepts insect, hoeing and sheep-shearing life-skill rows");

        long missingStock = 0;
        int started = 0;
        var missingSeed = new InventoryBulkGatheringAutomation(
            _ => Task.FromResult(missingStock), _ => Task.FromResult(missingStock),
            _ => Task.CompletedTask, _ => { started++; return Task.CompletedTask; },
            (_, _, _) => Task.CompletedTask);
        try { await missingSeed.RunAsync(new("달걀", 100), CancellationToken.None); throw new Exception("missing seed accepted"); }
        catch (InvalidOperationException) { check(started == 0, "missing initial item blocks inventory search"); }
    }
}
