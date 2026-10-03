namespace FishingAutomation;

internal sealed record QuestMaterialNameDecision(
    string? Name,
    string Diagnostic,
    IReadOnlyList<string> AmbiguousCandidates);

internal static class QuestMaterialNameMatcher
{
    internal static QuestMaterialNameDecision Decide(
        string ocrName,
        IEnumerable<string> candidates)
    {
        string needle = Normalize(ocrName);
        if (needle.Length == 0)
            return new(null, "none", Array.Empty<string>());

        var unique = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var exact = unique
            .Where(x => string.Equals(Normalize(x), needle, StringComparison.Ordinal))
            .ToArray();
        if (exact.Length == 1)
            return new(exact[0], "exact", Array.Empty<string>());
        if (exact.Length > 1)
            return new(null, "ambiguous:" + string.Join(", ", exact), exact);

        var scored = unique
            .Select(name =>
            {
                string normalized = Normalize(name);
                int distance = FullEditDistance(needle, normalized);
                bool fullPrefix = normalized.StartsWith(needle, StringComparison.Ordinal);
                int prefix = CommonPrefixLength(needle, normalized);
                int lengthGap = Math.Abs(normalized.Length - needle.Length);
                return new
                {
                    Name = name,
                    Distance = distance,
                    FullPrefix = fullPrefix,
                    Prefix = prefix,
                    LengthGap = lengthGap
                };
            })
            .Where(x => x.Distance <= 1)
            .OrderBy(x => x.Distance)
            .ThenByDescending(x => x.FullPrefix)
            .ThenByDescending(x => x.Prefix)
            .ThenBy(x => x.LengthGap)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        if (scored.Length == 0)
            return new(null, "none", Array.Empty<string>());

        var best = scored[0];
        var tied = scored
            .Where(x =>
                x.Distance == best.Distance &&
                x.FullPrefix == best.FullPrefix &&
                x.Prefix == best.Prefix &&
                x.LengthGap == best.LengthGap)
            .Select(x => x.Name)
            .ToArray();

        if (tied.Length != 1)
            return new(null, "ambiguous:" + string.Join(", ", tied), tied);

        string diagnostic =
            $"거리={best.Distance} · 전체접두={(best.FullPrefix ? "예" : "아니오")} · " +
            $"공통접두={best.Prefix} · 길이차={best.LengthGap}";
        return new(best.Name, diagnostic, Array.Empty<string>());
    }

    private static string Normalize(string value)
        => string.IsNullOrEmpty(value)
            ? ""
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static int CommonPrefixLength(string a, string b)
    {
        int limit = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < limit && a[i] == b[i]) i++;
        return i;
    }

    private static int FullEditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(
                    Math.Min(cur[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }
}
