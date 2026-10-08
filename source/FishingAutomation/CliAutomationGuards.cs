using System.Globalization;
using System.Text.Json;

namespace FishingAutomation;

internal sealed record CliCommandCapability(string Command, bool RequiresConfirm, string? Description = null, string? Metadata = null, string? Note = null);

internal sealed record CliIdentityContext(
    string? CharacterId,
    string? CharacterName,
    string? AccountCode,
    string? RealmName)
{
    internal int ComparableFields =>
        (CharacterId is null ? 0 : 1) +
        (CharacterName is null ? 0 : 1) +
        (AccountCode is null ? 0 : 1) +
        (RealmName is null ? 0 : 1);

    internal string Strength =>
        CharacterId is not null || AccountCode is not null ? "강함" :
        CharacterName is not null && RealmName is not null ? "보통" : "기본";

    // A name by itself is not sufficient to bind durable N02 batch checkpoints
    // across restarts. Account+name is an allowed legacy CLI alternative when
    // the API omits realm, provided both fields remain stable across queries.
    internal bool HasDurableMultiIdentity =>
        !string.IsNullOrWhiteSpace(CharacterId) ||
        !string.IsNullOrWhiteSpace(CharacterName) &&
        (!string.IsNullOrWhiteSpace(RealmName) || !string.IsNullOrWhiteSpace(AccountCode));

    internal string PresentFields =>
        string.Join(", ", new[]
        {
            CharacterId is null ? null : "CharacterId",
            CharacterName is null ? null : "CharacterName",
            AccountCode is null ? null : "AccountCode",
            RealmName is null ? null : "RealmName"
        }.Where(x => x is not null));

    internal bool ConflictsWith(CliIdentityContext other)
    {
        static bool Different(string? a, string? b) =>
            a is not null && b is not null && !string.Equals(a, b, StringComparison.Ordinal);
        return Different(CharacterId, other.CharacterId) ||
               Different(CharacterName, other.CharacterName) ||
               Different(AccountCode, other.AccountCode) ||
               Different(RealmName, other.RealmName);
    }

    internal bool Matches(CliIdentityContext current)
    {
        static bool Same(string? baseline, string? now) =>
            baseline is null || now is not null && baseline.Equals(now, StringComparison.Ordinal);

        return Same(CharacterId, current.CharacterId) &&
               Same(CharacterName, current.CharacterName) &&
               Same(AccountCode, current.AccountCode) &&
               Same(RealmName, current.RealmName);
    }
}

internal sealed class CliIdentityGuard
{
    private readonly MabinogiMobileCli _cli;
    private readonly CliIdentityContext _baseline;

    private CliIdentityGuard(MabinogiMobileCli cli, CliIdentityContext baseline)
    {
        _cli = cli;
        _baseline = baseline;
    }

    internal CliIdentityContext Baseline => _baseline;

    internal string Description =>
        $"get_my_info 기준 {_baseline.ComparableFields}개 필드 · 강도={_baseline.Strength}";

    internal static async Task<CliIdentityGuard> CaptureAsync(
        MabinogiMobileCli cli, CancellationToken ct = default)
    {
        var baseline = CliAutomationGuards.ParseIdentity(await cli.GetMyInfoAsync(ct).ConfigureAwait(false));
        return new(cli, baseline);
    }

    // Require two matching complete snapshots: a transient/partially populated
    // get_my_info must never create or resume a durable multi-altering batch.
    // Unlike VerifyWithLoadingRetryAsync this preflight is bounded.
    internal static async Task<CliIdentityGuard> CaptureForMultiAlteringAsync(
        MabinogiMobileCli cli, CancellationToken ct)
    {
        const int maxAttempts = 5;
        CliIdentityContext? previous = null;
        CliIdentityContext? confirmedCandidate = null;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var response = await cli.GetMyInfoAsync(ct).ConfigureAwait(false);
            if (!response.Success && CliAutomationGuards.IsTransientLoadingRejection(response))
            {
                confirmedCandidate = null;
            }
            else
            {
                var identity = CliAutomationGuards.ParseIdentity(response);
                if (previous is not null && previous.ConflictsWith(identity))
                    throw new InvalidOperationException(
                        "다중가공 CLI 캐릭터 식별 결과가 재조회 중 바뀌었습니다 · 기록 보존 · 입력 차단");
                // Retain every non-null field seen so far. A weak middle
                // response must not hide a change between two strong frames.
                previous = previous is null ? identity : new CliIdentityContext(
                    previous.CharacterId ?? identity.CharacterId,
                    previous.CharacterName ?? identity.CharacterName,
                    previous.AccountCode ?? identity.AccountCode,
                    previous.RealmName ?? identity.RealmName);
                if (identity.HasDurableMultiIdentity)
                {
                    if (identity == confirmedCandidate)
                        return new CliIdentityGuard(cli, identity);
                    confirmedCandidate = identity;
                }
                else confirmedCandidate = null;
            }
            if (attempt + 1 < maxAttempts)
                await Task.Delay(350, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "다중가공 캐릭터 식별 정보가 부족합니다 · get_my_info 유효 필드: " +
            (previous?.PresentFields is { Length: > 0 } fields ? fields : "없음") +
            " · 고유 ID 또는 캐릭터 이름+서버/계정이 필요합니다 · 기존 작업 기록 보존 · 입력 없음");
    }

    internal async Task VerifyAsync(CancellationToken ct)
    {
        var current = CliAutomationGuards.ParseIdentity(await _cli.GetMyInfoAsync(ct).ConfigureAwait(false));
        if (!_baseline.Matches(current))
            throw new InvalidOperationException("get_my_info 기준 캐릭터/계정/서버 문맥이 바뀌어 CLI 실행을 중단합니다.");
    }

    internal async Task VerifyWithLoadingRetryAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await _cli.GetMyInfoAsync(ct).ConfigureAwait(false);
            if (response.Success)
            {
                var current = CliAutomationGuards.ParseIdentity(response);
                if (!_baseline.Matches(current))
                    throw new InvalidOperationException("get_my_info 기준 캐릭터/계정/서버 문맥이 바뀌어 CLI 실행을 중단합니다.");
                return;
            }

            if (!CliAutomationGuards.IsTransientLoadingRejection(response))
                _ = CliAutomationGuards.ParseIdentity(response); // throws with the existing diagnostic

            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }
}

internal static class CliAutomationGuards
{
    internal static bool IsTransientLoadingRejection(MabinogiCliResult response)
        => !response.Success && response.Error == "cli_rejected";

    internal static async Task<IReadOnlyDictionary<string, CliCommandCapability>> EnsureCapabilitiesAsync(
        MabinogiMobileCli cli, IEnumerable<string> required, CancellationToken ct = default)
    {
        var status = await cli.StatusAsync(ct).ConfigureAwait(false);
        if (!status.Success)
            throw new InvalidOperationException($"게임 CLI 연결 확인 실패: {status.State}/{status.Error ?? "unknown"}");

        var result = await cli.CapabilitiesAsync(ct).ConfigureAwait(false);
        if (!result.Success || result.Data is not JsonElement root || root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("CLI capabilities 조회에 실패했습니다.");

        if (TryProperty(root, "loading") is JsonElement loading &&
            loading.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException("게임 CLI 명령 목록을 준비 중입니다. 잠시 후 다시 시작하세요.");

        var commandsNode = TryProperty(root, "commands");
        if (commandsNode is null || commandsNode.Value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("CLI capabilities 명령 목록 형식이 올바르지 않습니다.");

        var commands = new Dictionary<string, CliCommandCapability>(StringComparer.Ordinal);
        foreach (var row in commandsNode.Value.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            string? command = Text(row, "Command") ?? Text(row, "command");
            if (string.IsNullOrWhiteSpace(command)) continue;
            bool requiresConfirm = false;
            if (TryProperty(row, "Metadata") is JsonElement metadata &&
                metadata.ValueKind == JsonValueKind.Object &&
                TryProperty(metadata, "requiresConfirm") is JsonElement confirm)
            {
                requiresConfirm = confirm.ValueKind == JsonValueKind.True ||
                    confirm.ValueKind == JsonValueKind.String &&
                    bool.TryParse(confirm.GetString(), out bool parsed) && parsed;
            }
            commands[command] = new(command, requiresConfirm,
                Text(row, "Description") ?? Text(row, "description"),
                TryProperty(row, "Metadata")?.GetRawText(),
                Text(row, "Note") ?? Text(row, "note"));
        }

        string[] missing = required.Where(x => !commands.ContainsKey(x)).Distinct(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("현재 게임 CLI가 필요한 명령을 제공하지 않습니다: " + string.Join(", ", missing));

        return commands;
    }

    internal static CliIdentityContext ParseIdentity(MabinogiCliResult response)
    {
        if (!response.Success || response.Data is not JsonElement root || root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("get_my_info 조회 결과가 올바르지 않습니다.");

        // Some CLI releases put details under Character/Account/Server instead of
        // top-level fields. Never interpret a generic Account.Id as CharacterId.
        var character = FirstObject(root, "Character", "CharacterInfo", "CurrentCharacter", "Player", "PlayerInfo");
        var account = FirstObject(root, "Account", "AccountInfo");
        var server = FirstObject(root, "Realm", "RealmInfo", "Server", "ServerInfo");
        string? characterId = Consistent(
            FirstText(root, "CharacterId", "CharacterID", "CharacterCode", "CharacterEntityId", "CharId"),
            character is JsonElement ch ? FirstText(ch, "CharacterId", "CharacterCode", "CharacterEntityId", "CharId", "Id", "UID") : null);
        string? characterName = Consistent(
            FirstText(root, "CharacterName", "Name", "DisplayName", "Nickname", "NickName"),
            character is JsonElement chName ? FirstText(chName, "CharacterName", "Name", "DisplayName", "Nickname", "NickName") : null);
        string? accountCode = Consistent(
            FirstText(root, "AccountCode", "AccountId", "AccountID"),
            account is JsonElement acc ? FirstText(acc, "AccountCode", "AccountId", "AccountID", "Id", "Code") : null);
        string? realm = Consistent(
            FirstText(root, "RealmName", "ServerName", "WorldName") ??
                (TryProperty(root, "Realm") is JsonElement rn && rn.ValueKind == JsonValueKind.String ? rn.GetString() : null) ??
                (TryProperty(root, "Server") is JsonElement sn && sn.ValueKind == JsonValueKind.String ? sn.GetString() : null),
            server is JsonElement srv ? FirstText(srv, "RealmName", "ServerName", "WorldName", "Name") : null);

        var identity = new CliIdentityContext(characterId, characterName, accountCode, realm);
        if (identity.ComparableFields == 0)
            throw new InvalidDataException("get_my_info에서 비교 가능한 캐릭터 문맥 필드를 찾지 못했습니다.");
        return identity;
    }

    internal static IReadOnlyDictionary<string, decimal> ParseCurrencies(MabinogiCliResult response)
    {
        if (!response.Success || response.Data is not JsonElement root || root.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("get_currencies 조회 결과가 올바르지 않습니다.");

        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in root.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            string? name = Text(row, "DisplayName") ?? Text(row, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var amountNode = TryProperty(row, "Amount");
            if (amountNode is null || amountNode.Value.ValueKind == JsonValueKind.Null) continue;

            decimal amount;
            if (amountNode.Value.ValueKind == JsonValueKind.Number)
            {
                if (!amountNode.Value.TryGetDecimal(out amount))
                    throw new InvalidDataException("get_currencies 수량 범위가 올바르지 않습니다.");
            }
            else if (amountNode.Value.ValueKind == JsonValueKind.String &&
                     decimal.TryParse(amountNode.Value.GetString(), NumberStyles.Number,
                         CultureInfo.InvariantCulture, out amount))
            {
            }
            else throw new InvalidDataException("get_currencies 수량 형식이 올바르지 않습니다.");

            if (!result.TryAdd(name, amount))
                throw new InvalidDataException("get_currencies에 중복 재화 이름이 있습니다: " + name);
        }
        return result;
    }

    internal static async Task<IReadOnlyDictionary<string, decimal>> CurrencySnapshotAsync(
        MabinogiMobileCli cli, CancellationToken ct)
        => ParseCurrencies(await cli.GetCurrenciesAsync(ct).ConfigureAwait(false));

    internal static async Task<IReadOnlyDictionary<string, decimal>> CurrencySnapshotWithLoadingRetryAsync(
        MabinogiMobileCli cli, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await cli.GetCurrenciesAsync(ct).ConfigureAwait(false);
            if (response.Success)
                return ParseCurrencies(response);

            if (!IsTransientLoadingRejection(response))
                return ParseCurrencies(response); // throws with the existing diagnostic

            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }

    internal static string[] CurrencyChanges(
        IReadOnlyDictionary<string, decimal> before,
        IReadOnlyDictionary<string, decimal> after)
    {
        var names = before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
        var changes = new List<string>();
        foreach (string name in names)
        {
            bool hadBefore = before.TryGetValue(name, out decimal oldValue);
            bool hasAfter = after.TryGetValue(name, out decimal newValue);
            if (!hadBefore || !hasAfter || oldValue == newValue) continue;
            decimal delta = newValue - oldValue;
            changes.Add($"{name} {oldValue}→{newValue} ({(delta > 0 ? "+" : "")}{delta})");
        }
        return changes.ToArray();
    }

    private static JsonElement? FirstObject(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (TryProperty(root, name) is JsonElement value && value.ValueKind == JsonValueKind.Object)
                return value;
        return null;
    }

    private static string? Consistent(string? outer, string? inner)
    {
        if (outer is not null && inner is not null &&
            !string.Equals(outer, inner, StringComparison.Ordinal))
            throw new InvalidDataException(
                "get_my_info 캐릭터/계정/서버 식별 정보가 서로 충돌합니다 · 입력 차단");
        return outer ?? inner;
    }

    private static string? FirstText(JsonElement root, params string[] names)
    {
        foreach (string name in names)
        {
            string? value = Text(root, name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static string? Text(JsonElement root, string name)
    {
        var value = TryProperty(root, name);
        if (value is null || value.Value.ValueKind == JsonValueKind.Null) return null;
        return value.Value.ValueKind == JsonValueKind.String
            ? value.Value.GetString()
            : value.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? value.Value.GetRawText()
                : null;
    }

    private static JsonElement? TryProperty(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in root.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return null;
    }
}
