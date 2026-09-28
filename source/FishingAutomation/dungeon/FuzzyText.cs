namespace DungeonVisionBot;

internal static class FuzzyText
{
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    public static bool ContainsApprox(string haystack, string needle, int maxDistance)
    {
        var h = Normalize(haystack);
        var n = Normalize(needle);
        if (n.Length == 0) return false;
        if (h.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;

        int minLen = Math.Max(1, n.Length - maxDistance);
        int maxLen = Math.Min(h.Length, n.Length + maxDistance);
        for (int len = minLen; len <= maxLen; len++)
        {
            for (int i = 0; i + len <= h.Length; i++)
            {
                if (Levenshtein(h.AsSpan(i, len), n.AsSpan()) <= maxDistance)
                    return true;
            }
        }
        return false;
    }

    private static int Levenshtein(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
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
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
