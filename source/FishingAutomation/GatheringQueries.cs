using System.Text.Json;

namespace FishingAutomation;

internal sealed record GatherableItem(string DisplayName, bool ToolOk);
internal sealed record GatheringActivity(bool IsDead, bool IsReviving, bool IsInCombat,
    bool IsAutoPlaying, bool IsAutoTraveling, bool IsDialoguePlaying, bool IsWaitingForSelection,
    string DungeonState, bool IsInBattlefield, bool IsPlayingTutorial, bool IsInScenario,
    bool IsPlayingPerformance, bool IsPlayingMiniGame, bool IsHousingEditMode,
    string MainButtonState, bool HasTarget, string AvailableInteractionType, string LastRunningInteractionType)
{
    internal bool IsGathering => MainButtonState == "Stop" && LastRunningInteractionType == "Gathering" && !IsAutoTraveling;
    internal bool IsFishing => MainButtonState is "Fishing" or "FishingPull";
    internal bool IsSafeField => !IsDead && !IsReviving && !IsInCombat &&
        !IsDialoguePlaying && !IsWaitingForSelection && DungeonState == "NotInDungeon" &&
        !IsPlayingTutorial && !IsInScenario && !IsPlayingPerformance &&
        !IsPlayingMiniGame && !IsHousingEditMode;
}

internal static class GatheringQueries
{
    internal static IReadOnlyList<GatherableItem> ParseCatalog(MabinogiCliResult response)
    {
        var root = Data(response);
        var items = root.GetProperty("items");
        if (items.ValueKind != JsonValueKind.Array) throw new InvalidDataException("채집 목록 형식이 올바르지 않습니다.");
        var result = new List<GatherableItem>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in items.EnumerateArray())
        {
            string name = Text(item, "DisplayName");
            if (!names.Add(name)) throw new InvalidDataException("채집 목록에 중복 품목이 있습니다.");
            result.Add(new(name, Bool(item, "ToolOk")));
        }
        return result;
    }

    internal static GatheringActivity ParseActivity(MabinogiCliResult response)
    {
        var r = Data(response);
        var interaction = r.GetProperty("Interaction");
        var mode = r.GetProperty("Mode");
        return new(Bool(r,"IsDead"), Bool(r,"IsReviving"), Bool(r,"IsInCombat"),
            Bool(r,"IsAutoPlaying"), Bool(r,"IsAutoTraveling"), Bool(r,"IsDialoguePlaying"), Bool(r,"IsWaitingForSelection"),
            Text(r.GetProperty("Dungeon"),"State"), Bool(r.GetProperty("Battlefield"),"IsInBattleField"),
            Bool(r.GetProperty("Tutorial"),"IsPlaying"), Bool(r.GetProperty("Scenario"),"IsInScenario"),
            Bool(r.GetProperty("Performance"),"IsPlaying"), Bool(mode,"IsPlayingMiniGame"), Bool(mode,"IsHousingEditMode"),
            Text(mode,"MainButtonState"), Bool(interaction,"HasTarget"),
            Text(interaction,"AvailableInteractionType"), Text(interaction,"LastRunningInteractionType"));
    }

    internal static (decimal Current, decimal Maximum) ParseWeight(MabinogiCliResult response)
    {
        var r = Data(response);
        decimal current = r.GetProperty("CurrentInventoryWeightAsDecimal").GetDecimal();
        decimal maximum = r.GetProperty("MaxInventoryWeightAsDecimal").GetDecimal();
        if(current < 0 || maximum <= 0) throw new InvalidDataException("가방 무게 조회 결과가 올바르지 않습니다.");
        return (current, maximum);
    }

    private static JsonElement Data(MabinogiCliResult response)
    {
        if (!response.Success || response.Data is not JsonElement r || r.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("무료 채집 상태 조회에 실패했습니다.");
        if(r.TryGetProperty("body", out var body)) r = body;
        if(r.ValueKind != JsonValueKind.Object) throw new InvalidDataException("채집 상태 형식이 올바르지 않습니다.");
        return r;
    }
    private static bool Bool(JsonElement r, string name) => r.GetProperty(name).GetBoolean();
    private static string Text(JsonElement r, string name)
    {
        string? text = r.GetProperty(name).GetString();
        if(string.IsNullOrWhiteSpace(text) || text.Length > 512 || text.Any(char.IsControl))
            throw new InvalidDataException("채집 상태 이름이 올바르지 않습니다.");
        return text;
    }
}
