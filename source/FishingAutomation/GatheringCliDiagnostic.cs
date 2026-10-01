using System.Text.Json;

namespace FishingAutomation;

// Deliberately receives only the two read operations; no automation or screen dependency.
internal static class GatheringCliDiagnostic
{
    internal static async Task RunAsync(string name,
        Func<CancellationToken, Task<MabinogiCliResult>> capabilities,
        Func<string, CancellationToken, Task<MabinogiCliResult>> gatherable,
        Action<string> log, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl))
            throw new ArgumentException("검사할 품목을 선택하세요.");
        name = name.Trim();
        log("[CLI 검사] query=" + JsonSerializer.Serialize(name) + " · 조회 전용 시작");
        try
        {
            var caps = await capabilities(token);
            LogResult("capabilities", caps, log);
            if (caps.Data is JsonElement c && c.ValueKind == JsonValueKind.Object &&
                c.TryGetProperty("commands", out var commands) && commands.ValueKind == JsonValueKind.Array)
            {
                // One row per command: never truncate the full command list.
                foreach (var row in commands.EnumerateArray())
                {
                    JsonElement command = row;
                    if (row.ValueKind == JsonValueKind.Object)
                    {
                        if (row.TryGetProperty("Command", out var upper)) command = upper;
                        else if (row.TryGetProperty("command", out var lower)) command = lower;
                    }
                    log("[CLI 검사][전체 명령] " + command.GetRawText());
                    if (command.ValueKind == JsonValueKind.String &&
                        command.GetString() is "get_gatherable_items" or "execute_gathering" &&
                        row.ValueKind == JsonValueKind.Object)
                    {
                        var details = new Dictionary<string, JsonElement?>();
                        foreach (var field in new[] { "Description", "BodyExample", "OutputExample", "Note", "Metadata" })
                            details[field] = row.TryGetProperty(field, out var value) ? value : null;
                        log("[CLI 검사][명령 상세][" + command.GetString() + "] " + JsonSerializer.Serialize(details));
                    }
                }
            }
            var result = await gatherable(name, token);
            LogResult("get_gatherable_items", result, log);
            var fields = new SortedSet<string>(StringComparer.Ordinal);
            int? count = null;
            int exact = 0;
            if (result.Data is JsonElement root && root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                count = items.GetArrayLength();
                int index = 0;
                foreach (var row in items.EnumerateArray())
                {
                    index++;
                    var item = new Dictionary<string, JsonElement?>();
                    foreach (var field in new[] { "DisplayName", "ToolOk" })
                        item[field] = row.ValueKind == JsonValueKind.Object && row.TryGetProperty(field, out var value) ? value : null;
                    log($"[CLI 검사][품목 {index}] " + JsonSerializer.Serialize(item));
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    foreach (var p in row.EnumerateObject()) fields.Add(p.Name);
                    if (row.TryGetProperty("DisplayName", out var display) &&
                        display.ValueKind == JsonValueKind.String && display.GetString() == name) exact++;
                }
            }
            log($"[CLI 검사] items={count?.ToString() ?? "unknown"} · itemFields={JsonSerializer.Serialize(fields)} · exact={exact}");
        }
        finally { log("[CLI 검사] 종료 · 채집 실행 없음"); }
    }

    private static void LogResult(string label, MabinogiCliResult result, Action<string> log)
    {
        log($"[CLI 검사][{label}] success={result.Success} · state={JsonSerializer.Serialize(result.State)} · error={JsonSerializer.Serialize(result.Error)}");
        // Structure only: preserves every field and array count while withholding arbitrary
        // string values, account identifiers, coordinates and other private CLI payloads.
        if (result.Data is JsonElement data)
            log("[CLI 검사][" + label + "][구조] " + JsonSerializer.Serialize(Structure(data, 0)));
    }

    private static object Structure(JsonElement value, int depth)
    {
        if (depth >= 8) return value.ValueKind.ToString();
        if (value.ValueKind == JsonValueKind.Object)
            return value.EnumerateObject().GroupBy(p => p.Name).ToDictionary(g => g.Key, g => Structure(g.Last().Value, depth + 1));
        if (value.ValueKind == JsonValueKind.Array)
            return new { count = value.GetArrayLength(), rows = value.EnumerateArray().Take(10).Select(v => Structure(v, depth + 1)).ToArray() };
        return value.ValueKind.ToString();
    }
}
