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
    internal static string DescribeCatalogSchema(MabinogiCliResult response, string? selectedDisplayName = null)
    {
        var root = Data(response);
        var rootFields = root.ValueKind == JsonValueKind.Object
            ? root.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return "root=[" + string.Join(",", rootFields) + "] · items=없음";

        var rows = items.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToArray();
        var allFields = rows
            .SelectMany(x => x.EnumerateObject().Select(p => p.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        JsonElement? selected = null;
        if (!string.IsNullOrWhiteSpace(selectedDisplayName))
        {
            foreach (var row in rows)
            {
                if (row.TryGetProperty("DisplayName", out var name) &&
                    name.ValueKind == JsonValueKind.String &&
                    string.Equals(name.GetString(), selectedDisplayName, StringComparison.Ordinal))
                {
                    selected = row;
                    break;
                }
            }
        }

        string selectedFields = selected is JsonElement item
            ? string.Join(", ", item.EnumerateObject().Select(p =>
                p.Name + ":" + p.Value.ValueKind))
            : "선택 품목 행 없음";

        return $"root=[{string.Join(",", rootFields)}] · items={rows.Length} · itemFields=[{string.Join(",", allFields)}] · selected=[{selectedFields}]";
    }
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
        // Unknown activity data is NEVER interpreted as idle, and neither
        // values nor unfiltered JSON keys may enter public runtime logs.
        var r = Data(response);
        try
        {
            var interaction = ActivityRequired(r, "Interaction", "$");
            var mode = ActivityRequired(r, "Mode", "$");
            var dungeon = ActivityRequired(r, "Dungeon", "$");
            var battlefield = ActivityRequired(r, "Battlefield", "$");
            var tutorial = ActivityRequired(r, "Tutorial", "$");
            var scenario = ActivityRequired(r, "Scenario", "$");
            var performance = ActivityRequired(r, "Performance", "$");
            return new(
                ActivityBool(r, "IsDead", "$"),
                ActivityBool(r, "IsReviving", "$"),
                ActivityBool(r, "IsInCombat", "$"),
                ActivityBool(r, "IsAutoPlaying", "$"),
                ActivityBool(r, "IsAutoTraveling", "$"),
                ActivityBool(r, "IsDialoguePlaying", "$"),
                ActivityBool(r, "IsWaitingForSelection", "$"),
                ActivityText(dungeon, "State", "$.Dungeon"),
                ActivityBool(battlefield, "IsInBattleField", "$.Battlefield"),
                ActivityBool(tutorial, "IsPlaying", "$.Tutorial"),
                ActivityBool(scenario, "IsInScenario", "$.Scenario"),
                ActivityBool(performance, "IsPlaying", "$.Performance"),
                ActivityBool(mode, "IsPlayingMiniGame", "$.Mode"),
                ActivityBool(mode, "IsHousingEditMode", "$.Mode"),
                ActivityText(mode, "MainButtonState", "$.Mode"),
                ActivityBool(interaction, "HasTarget", "$.Interaction"),
                ActivityText(interaction, "AvailableInteractionType", "$.Interaction"),
                ActivityText(interaction, "LastRunningInteractionType", "$.Interaction"));
        }
        catch (InvalidDataException ex) when (
            ex.Message.StartsWith("get_activity ", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                ex.Message + " · get_activity 구조(값 제외): " + DescribeActivitySchema(r),
                ex);
        }
    }

    // Schema-only fingerprint for the 2026-11-05 CLI contract regression.
    // Never log JSON values, account/character identifiers, raw property lists,
    // or unbounded user-supplied keys. Group names are fixed by this source.
    internal static string DescribeActivitySchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return "root=non-object";

        static string Kind(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False => "Boolean",
            JsonValueKind.String => "String",
            JsonValueKind.Number => "Number",
            JsonValueKind.Object => "Object",
            JsonValueKind.Array => "Array",
            JsonValueKind.Null => "Null",
            _ => "Unknown"
        };

        static string Expected(JsonElement item, string[] keys)
            => string.Join(",", keys.Select(key =>
                key + ":" + (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty(key, out var value)
                        ? Kind(value) : "missing")));

        var rootKeys = new[]
        {
            "IsDead", "IsReviving", "IsInCombat", "IsAutoPlaying",
            "IsAutoTraveling", "IsDialoguePlaying", "IsWaitingForSelection",
            "Dungeon", "Battlefield", "Tutorial", "Scenario",
            "Performance", "Mode", "Interaction"
        };
        string groups = string.Join(";", new[]
        {
            ("Dungeon", new[] {"State"}),
            ("Battlefield", new[] {"IsInBattleField"}),
            ("Tutorial", new[] {"IsPlaying"}),
            ("Scenario", new[] {"IsInScenario"}),
            ("Performance", new[] {"IsPlaying"}),
            ("Mode", new[] {"IsPlayingMiniGame", "IsHousingEditMode", "MainButtonState"}),
            ("Interaction", new[] {"HasTarget", "AvailableInteractionType", "LastRunningInteractionType"})
        }.Select(group =>
        {
            root.TryGetProperty(group.Item1, out var nested);
            return group.Item1 + "{" + Expected(nested, group.Item2) + "}";
        }));

        // Only restricted generic state-field names are shown as *candidates*,
        // never as verified aliases for IsReviving.
        var candidates = root.EnumerateObject()
            .Where(p => !rootKeys.Contains(p.Name, StringComparer.Ordinal) &&
                        IsSafeActivitySchemaCandidate(p.Name))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Take(12)
            .Select(p => p.Name + ":" + Kind(p.Value))
            .ToArray();
        return "root{" + Expected(root, rootKeys) + "};" +
               groups + ";추가상태키후보=[" + string.Join(",", candidates) + "]";
    }

    private static bool IsSafeActivitySchemaCandidate(string name)
    {
        if (name.Length is < 2 or > 48 ||
            !char.IsAsciiLetter(name[0]) ||
            name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            return false;
        if (new[]
        {
            "User", "Account", "Character", "Player", "Name", "Guid",
            "Token", "Secret", "Password", "Auth", "Session", "Device",
            "Email", "Phone", "Address", "Location", "Position", "Key",
            "Id", "Cookie", "Profile", "Guild", "Ip"
        }.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            return false;
        return new[]
        {
            "Is", "Has", "Revive", "Resurrect", "Dead", "Alive",
            "Mode", "State", "Status", "Activity", "Interaction",
            "Combat", "Playing", "Travel", "Selection", "Target"
        }.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement ActivityRequired(JsonElement parent, string key, string parentPath)
    {
        string field = parentPath + "." + key;
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(key, out var value))
            throw new InvalidDataException(
                "get_activity 필수 필드 누락: " + field +
                " · CLI 응답 구조 미확정, 게임 입력 차단");
        return value;
    }

    private static bool ActivityBool(JsonElement parent, string key, string parentPath)
    {
        var value = ActivityRequired(parent, key, parentPath);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException(
                "get_activity 필드 유형 불일치: " + parentPath + "." + key +
                " · 필요=Boolean · 실제=" + value.ValueKind +
                " · 게임 입력 차단");
        return value.GetBoolean();
    }

    private static string ActivityText(JsonElement parent, string key, string parentPath)
    {
        var value = ActivityRequired(parent, key, parentPath);
        string field = parentPath + "." + key;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException(
                "get_activity 필드 유형 불일치: " + field +
                " · 필요=String · 실제=" + value.ValueKind +
                " · 게임 입력 차단");
        string? text = value.GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 512 ||
            text.Any(char.IsControl))
            throw new InvalidDataException(
                "get_activity 필드 값 불확실: " + field +
                " · 값 기록 없이 게임 입력 차단");
        return text;
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
