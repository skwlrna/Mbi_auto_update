using System.Globalization;
using System.Text.RegularExpressions;

namespace FishingAutomation;

// Remote commands accept exact read-only Korean "상태" as an alias of /status.
// Korean production commands are routed through the private-chat check.
internal static class TelegramCommandParser
{
    internal static string? Parse(string? text)
    {
        string raw = text?.Trim() ?? "";
        if (raw.Equals("상태", StringComparison.Ordinal))
            return "/status";
        if (raw.StartsWith("가공 ", StringComparison.Ordinal))
            return raw;
        if (!raw.StartsWith('/'))
            return null;
        string command = raw.Split(' ', 2)[0].Split('@', 2)[0].ToLowerInvariant();
        return command is "/status" or "/stop" or "/restart" or
            "/help" or "/item" or "/itemreset" ? command : null;
    }

    internal const string AlteringHelp =
        "가공 가죽+ 200 - 총 200개까지 채우기\n" +
        "가공 가죽+ 추가 100 - 100개 이상 새로 만들기\n" +
        "가공 목록 / 가공 취소 번호 / 가공 시작 / 가공 정지";

    private static readonly Regex LegacyAlteringOrder = new(
        @"\A가공 추가 (?<name>[^\r\n]{1,120}?) (?<mode>총|추가) (?<count>[0-9]{1,7})\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex ShortAlteringOrder = new(
        @"\A가공 (?<name>[^\r\n]{1,120}?)(?: (?<mode>총|추가))? (?<count>[0-9]{1,7})\z",
        RegexOptions.CultureInvariant);

    internal static (string Name, bool Total, int Quantity)? ParseAlteringOrder(string command)
    {
        var match = LegacyAlteringOrder.Match(command);
        if (!match.Success) match = ShortAlteringOrder.Match(command);
        if (!match.Success ||
            !int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int quantity) ||
            quantity is < 1 or > 1000000)
            return null;

        string name = match.Groups["name"].Value.Trim();
        if (name is "" or "추가" or "목록" or "상태" or "취소" or "시작" or "정지")
            return null;
        return (name, match.Groups["mode"].Value != "추가", quantity);
    }
}
