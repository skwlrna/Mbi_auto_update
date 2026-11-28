using System.Text.Json;

namespace FishingAutomation;

internal sealed record AbyssRoundRecord(
    DateTimeOffset EndedAt, string Dungeon, long? FightSeconds, bool RewardConfirmed,
    IReadOnlyDictionary<string, int>? Loot, int Recoveries, string Outcome)
{
    internal string Summary()
    {
        string time = FightSeconds is long seconds ? $"{seconds / 60}분 {seconds % 60:D2}초" : "전투 시간 미확인";
        string loot = Loot is null ? "전리품 미확인" : Loot.Count == 0 ? "추적 전리품 변화 없음" :
            string.Join(", ", Loot.Select(x => $"{LootStats.GetDisplayName(x.Key)} +{x.Value}"));
        return $"{EndedAt.ToLocalTime():MM/dd HH:mm} · {Dungeon} · {time} · " +
            $"{(RewardConfirmed ? "보상 확인" : "보상 미확인")} · {Outcome}" +
            (Recoveries > 0 ? $" · 복구 {Recoveries}회" : "") + $" · {loot}";
    }
}

internal static class AbyssRoundHistory
{
    private const int Capacity = 50;
    private static readonly object Gate = new();
    private static readonly string PathName = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MabiAuto", "abyss-rounds.json");
    private static readonly List<AbyssRoundRecord> Records = Read();

    internal static string[] Latest(int count)
    {
        lock (Gate)
            return Records.TakeLast(count).Reverse().Select(record => record.Summary()).ToArray();
    }

    internal static void Append(AbyssRoundRecord record)
    {
        lock (Gate)
        {
            Records.Add(record);
            if (Records.Count > Capacity) Records.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
                string temporary = PathName + "." + Environment.ProcessId + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(Records));
                File.Move(temporary, PathName, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static List<AbyssRoundRecord> Read()
    {
        try
        {
            if (new FileInfo(PathName) is { Exists: true, Length: <= 262144 })
                return (JsonSerializer.Deserialize<List<AbyssRoundRecord>>(File.ReadAllText(PathName)) ?? [])
                    .Where(record => record is not null).TakeLast(Capacity).ToList();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return [];
    }
}
