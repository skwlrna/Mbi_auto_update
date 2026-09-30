using System.Text;
using System.Text.Json;

namespace FishingAutomation;

public static class LootStats
{
    public const string HallucinationStone = "hallucination_stone";
    public const string DevouringStone = "devouring_stone";
    public const string AbyssStone = "abyss_stone";
    public const string RuneEngraving10 = "rune_engraving_10";
    public const string RuneEngraving10Plus = "rune_engraving_10_plus";
    public const string RuneBinding10 = "rune_binding_10";
    public const string RuneBinding10Plus = "rune_binding_10_plus";
    public const string MorCorsairCoat = "mor_corsair_coat";
    public const string MorCorsairGloves = "mor_corsair_gloves";
    public const string MorCorsairBoots = "mor_corsair_boots";
    public const string MorCorsairTricorne = "mor_corsair_tricorne";

    private static readonly object Gate = new();

    private static readonly string[] OrderedKeys =
    {
        HallucinationStone,
        DevouringStone,
        AbyssStone,
        RuneEngraving10,
        RuneEngraving10Plus,
        RuneBinding10,
        RuneBinding10Plus,
        MorCorsairCoat,
        MorCorsairGloves,
        MorCorsairBoots,
        MorCorsairTricorne,
    };

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.Ordinal)
    {
        [HallucinationStone] = "허상의 마력석",
        [DevouringStone] = "포식의 마력석",
        [AbyssStone] = "심해의 마력석",
        [RuneEngraving10] = "룬새김 장식(★10)",
        [RuneEngraving10Plus] = "룬새김 장식(★10)+",
        [RuneBinding10] = "룬결속 장식(★10)",
        [RuneBinding10Plus] = "룬결속 장식(★10)+",
        [MorCorsairCoat] = "모르 코르셰어 코트",
        [MorCorsairGloves] = "모르 코르셰어 글러브",
        [MorCorsairBoots] = "모르 코르셰어 부츠",
        [MorCorsairTricorne] = "모르 코르셰어 트리코른",
    };

    private static readonly string StatsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MabiAuto");

    private static readonly string StatsPath = Path.Combine(StatsDirectory, "loot_stats.json");
    private static readonly Dictionary<string, int> Session = CreateZeroCounts();
    private static PersistedStats Data = Load();

    public static string GetDisplayName(string key)
        => DisplayNames.TryGetValue(key, out var name) ? name : key;

    internal static IReadOnlyDictionary<string, string> TrackedItemNames { get; }
        = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(DisplayNames);

    public static void RecordRound(IEnumerable<string> foundKeys)
        => RecordAmounts(foundKeys.Distinct(StringComparer.Ordinal).ToDictionary(k => k, _ => 1));

    public static void RecordAmounts(IReadOnlyDictionary<string, int> amounts)
    {
        lock (Gate)
        {
            bool changed = EnsureToday();
            var valid = amounts.Where(x => DisplayNames.ContainsKey(x.Key) && x.Value > 0).ToArray();
            // Check every increment before changing any counters.
            foreach (var item in valid)
            {
                _ = checked(Session.GetValueOrDefault(item.Key) + item.Value);
                _ = checked(Data.Today.GetValueOrDefault(item.Key) + item.Value);
                _ = checked(Data.Lifetime.GetValueOrDefault(item.Key) + item.Value);
            }
            foreach (var item in valid)
            {
                Session[item.Key] = Session.GetValueOrDefault(item.Key) + item.Value;
                Data.Today[item.Key] = Data.Today.GetValueOrDefault(item.Key) + item.Value;
                Data.Lifetime[item.Key] = Data.Lifetime.GetValueOrDefault(item.Key) + item.Value;
                changed = true;
            }

            if (changed)
                Save();
        }
    }

    public static string GetTelegramReport()
    {
        lock (Gate)
        {
            if (EnsureToday())
                Save();

            var sb = new StringBuilder();
            sb.AppendLine("전리품 획득 현황");
            sb.AppendLine("(이번 실행 / 오늘 / 누적)");
            sb.AppendLine();

            AppendSection(sb, "마력석", new[]
            {
                HallucinationStone, DevouringStone, AbyssStone
            });

            AppendSection(sb, "장식", new[]
            {
                RuneEngraving10, RuneEngraving10Plus,
                RuneBinding10, RuneBinding10Plus
            });

            AppendSection(sb, "모르 코르셰어", new[]
            {
                MorCorsairCoat, MorCorsairGloves, MorCorsairBoots, MorCorsairTricorne
            });

            int sessionTotal = OrderedKeys.Sum(k => Session.GetValueOrDefault(k));
            int todayTotal = OrderedKeys.Sum(k => Data.Today.GetValueOrDefault(k));
            int lifetimeTotal = OrderedKeys.Sum(k => Data.Lifetime.GetValueOrDefault(k));

            sb.AppendLine($"총합: {sessionTotal} / {todayTotal} / {lifetimeTotal}");
            return sb.ToString().TrimEnd();
        }
    }

    public static string ResetLifetime()
    {
        lock (Gate)
        {
            EnsureToday();
            foreach (string key in OrderedKeys)
                Data.Lifetime[key] = 0;
            Save();

            return "전리품 누적 기록을 초기화했습니다.\n이번 실행/오늘 기록은 유지됩니다.\n누적 합계: 0개";
        }
    }

    private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> keys)
    {
        sb.AppendLine($"[{title}]");
        foreach (string key in keys)
        {
            sb.AppendLine(
                $"{DisplayNames[key]}: " +
                $"{Session.GetValueOrDefault(key)} / " +
                $"{Data.Today.GetValueOrDefault(key)} / " +
                $"{Data.Lifetime.GetValueOrDefault(key)}");
        }
        sb.AppendLine();
    }

    private static Dictionary<string, int> CreateZeroCounts()
        => OrderedKeys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);

    private static void FillMissing(Dictionary<string, int> counts)
    {
        foreach (string key in OrderedKeys)
            counts.TryAdd(key, 0);
    }

    private static bool EnsureToday()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (string.Equals(Data.Day, today, StringComparison.Ordinal))
            return false;

        Data.Day = today;
        Data.Today = CreateZeroCounts();
        return true;
    }

    private static PersistedStats Load()
    {
        try
        {
            Directory.CreateDirectory(StatsDirectory);
            if (!File.Exists(StatsPath))
                return NewPersisted();

            var loaded = JsonSerializer.Deserialize<PersistedStats>(
                File.ReadAllText(StatsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (loaded is null)
                return NewPersisted();

            loaded.Today ??= CreateZeroCounts();
            loaded.Lifetime ??= CreateZeroCounts();
            FillMissing(loaded.Today);
            FillMissing(loaded.Lifetime);

            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (!string.Equals(loaded.Day, today, StringComparison.Ordinal))
            {
                loaded.Day = today;
                loaded.Today = CreateZeroCounts();
            }

            return loaded;
        }
        catch
        {
            return NewPersisted();
        }
    }

    private static PersistedStats NewPersisted()
        => new()
        {
            Day = DateTime.Now.ToString("yyyy-MM-dd"),
            Today = CreateZeroCounts(),
            Lifetime = CreateZeroCounts(),
        };

    private static void Save()
    {
        Directory.CreateDirectory(StatsDirectory);
        string temp = StatsPath + ".tmp";
        string json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, json);
        File.Move(temp, StatsPath, true);
    }

    private sealed class PersistedStats
    {
        public string Day { get; set; } = "";
        public Dictionary<string, int> Today { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Lifetime { get; set; } = new(StringComparer.Ordinal);
    }
}
