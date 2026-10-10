namespace FishingAutomation;

// Remote commands accept exact read-only Korean "상태" as an alias of /status.
// All commands that affect automation remain slash-only.
internal static class TelegramCommandParser
{
    internal static string? Parse(string? text)
    {
        string raw = text?.Trim() ?? "";
        if (raw.Equals("상태", StringComparison.Ordinal))
            return "/status";
        if (!raw.StartsWith('/'))
            return null;
        string command = raw.Split(' ', 2)[0].Split('@', 2)[0].ToLowerInvariant();
        return command is "/status" or "/stop" or "/restart" or
            "/help" or "/item" or "/itemreset" ? command : null;
    }
}
