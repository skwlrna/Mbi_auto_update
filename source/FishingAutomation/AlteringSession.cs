using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FishingAutomation;

internal enum MultiAlteringItemState { NotStarted, InProgress, Completed, RecoveryRequired }

internal sealed record AlteringSessionState
{
    public int Version { get; init; } = 1;
    public string? BatchId { get; init; }
    public MultiAlteringItemState MultiState { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string FacilityName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int TargetQuantity { get; init; }
    public int ProducedPerWork { get; init; }
    public int RecipeOrdinal { get; init; }
    public int RequiredWorks { get; init; }
    public int QueuedWorks { get; init; }
    public long BaselineQuantity { get; init; }
    public long LastObservedOutputQuantity { get; init; }
    public long CreditedInternalConsumptionQuantity { get; init; }
    public int InitialExistingWorks { get; init; }
    public bool PendingRegistration { get; init; }
    // F05: ties the pending registration to ONE durable applied transaction ID.
    public string? PendingConsumptionTransactionId { get; init; }
    public int PendingBeforeMatchingCount { get; init; }
    public string Stage { get; init; } = "가공 준비";
    public string? CharacterId { get; init; }
    public string? CharacterName { get; init; }
    public string? AccountCode { get; init; }
    public string? RealmName { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    internal bool MatchesPlan(AlteringPlan plan) =>
        FacilityName == plan.FacilityName &&
        DisplayName == plan.DisplayName &&
        TargetQuantity == plan.TargetQuantity &&
        ProducedPerWork == plan.ProducedPerWork &&
        RecipeOrdinal == plan.RecipeOrdinal &&
        RequiredWorks == plan.RequiredWorks;

    internal bool MatchesIdentity(CliIdentityContext identity)
    {
        static bool Same(string? saved, string? current) =>
            saved is null || current is not null && saved.Equals(current, StringComparison.Ordinal);

        return Same(CharacterId, identity.CharacterId) &&
               Same(CharacterName, identity.CharacterName) &&
               Same(AccountCode, identity.AccountCode) &&
               Same(RealmName, identity.RealmName);
    }

    internal long InitialExistingMinimum => checked((long)InitialExistingWorks * ProducedPerWork);

    internal static AlteringSessionState Create(
        AlteringPlan plan, CliIdentityContext identity, long baseline, int initialExistingWorks) => new()
    {
        FacilityName = plan.FacilityName,
        DisplayName = plan.DisplayName,
        TargetQuantity = plan.TargetQuantity,
        ProducedPerWork = plan.ProducedPerWork,
        RecipeOrdinal = plan.RecipeOrdinal,
        RequiredWorks = plan.RequiredWorks,
        QueuedWorks = 0,
        BaselineQuantity = baseline,
        LastObservedOutputQuantity = baseline,
        InitialExistingWorks = initialExistingWorks,
        CharacterId = identity.CharacterId,
        CharacterName = identity.CharacterName,
        AccountCode = identity.AccountCode,
        RealmName = identity.RealmName,
        Stage = "가공 준비",
        StartedAt = DateTimeOffset.Now,
        UpdatedAt = DateTimeOffset.Now
    };
}

internal class AlteringSessionStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal AlteringSessionStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MabiAuto", "altering-session.json");
    }

    internal string Path => _path;

    internal static string MultiPlanPath(string directory, AlteringPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(plan);

        string identity =
            $"{plan.FacilityName}\u001f{plan.DisplayName}\u001f{plan.RecipeOrdinal}";
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
            .ToLowerInvariant()[..24];
        return System.IO.Path.Combine(directory, $"plan-{hash}.json");
    }

    internal static bool IsLegacyMultiPath(string path)
        => int.TryParse(
            System.IO.Path.GetFileNameWithoutExtension(path),
            out _);

    internal virtual AlteringSessionState? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<AlteringSessionState>(File.ReadAllText(_path), JsonOptions)
                ?? throw new InvalidDataException("자동 가공 이어하기 파일이 비어 있습니다.");
            // Upgrade checkpoints written by the first resume build, before
            // LastObservedOutputQuantity was added.
            if (state.LastObservedOutputQuantity == 0 && state.BaselineQuantity > 0)
                state = state with { LastObservedOutputQuantity = state.BaselineQuantity };
            if (state.Version != 1 || string.IsNullOrWhiteSpace(state.FacilityName) ||
                string.IsNullOrWhiteSpace(state.DisplayName) || state.TargetQuantity <= 0 ||
                state.ProducedPerWork <= 0 || state.RequiredWorks <= 0 ||
                state.QueuedWorks < 0 || state.QueuedWorks > state.RequiredWorks ||
                state.BaselineQuantity < 0 || state.LastObservedOutputQuantity < state.BaselineQuantity ||
                state.CreditedInternalConsumptionQuantity < 0 ||
                state.InitialExistingWorks < 0 || state.PendingBeforeMatchingCount < 0)
                throw new InvalidDataException("자동 가공 이어하기 파일의 값이 올바르지 않습니다.");
            return state;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("자동 가공 이어하기 파일을 읽지 못했습니다.", ex);
        }
    }

    internal virtual void Save(AlteringSessionState state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        string json = JsonSerializer.Serialize(state with { UpdatedAt = DateTimeOffset.Now }, JsonOptions);
        File.WriteAllText(temp, json);
        File.Move(temp, _path, true);
    }

    internal virtual void Delete()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
            string temp = _path + ".tmp";
            if (File.Exists(temp)) File.Delete(temp);
        }
        catch { }
    }
}

internal sealed record AlteringProgress(
    long ConfirmedQuantity,
    int TargetQuantity,
    int QueuedWorks,
    int RequiredWorks,
    int FacilityWorks,
    long? NextCompletionSeconds,
    string MaterialState,
    long? BatchRemainingSeconds = null,
    long? TotalRemainingSeconds = null)
{
    internal string Summary(string displayName)
    {
        string next = NextCompletionSeconds.HasValue ? $"{NextCompletionSeconds.Value}초" : "—";
        return $"{displayName} {ConfirmedQuantity:N0}/{TargetQuantity:N0} · 등록 {QueuedWorks}/{RequiredWorks} · 대기열 {FacilityWorks}/7 · 다음 완료 {next} · {MaterialState}";
    }
}

internal static class AlteringMaterialEstimate
{
    internal static string Describe(AlteringPlan plan, AlteringRecipe recipe)
    {
        if (recipe.MissingIngredients.Count == 0)
            return $"현재 재료로 즉시 가공 가능 · {plan.RequiredWorks}작업 / 최소 {plan.ExpectedQuantity:N0}개 · 전체 재료표는 CLI가 부족 항목만 제공";

        string[] rows = recipe.MissingIngredients.Select(x =>
        {
            long total = checked(x.Required * (long)plan.RequiredWorks);
            long shortage = Math.Max(0, total - x.Owned);
            return $"{x.DisplayName} 예상 {total:N0} · 보유 {x.Owned:N0} · 부족 약 {shortage:N0}";
        }).ToArray();

        return $"재료 예상 · {string.Join(" / ", rows)}";
    }
}
