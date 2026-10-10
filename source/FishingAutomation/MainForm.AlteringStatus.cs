namespace FishingAutomation;

public sealed partial class MainForm
{
    private readonly Dictionary<string, AlteringStatusItem> _alteringStatusItems =
        new(StringComparer.Ordinal);
    private readonly List<string> _alteringStatusOrder = new();
    private readonly Dictionary<string, MultiAlteringTelegramItem> _multiTelegramItems =
        new(StringComparer.Ordinal);
    private string _multiTelegramLastAction = "다중가공 준비";
    private DateTimeOffset _multiTelegramLastActionAt;

    private static string AlteringStatusKey(AlteringPlan plan)
        => $"{plan.FacilityName}\u001f{plan.DisplayName}\u001f{plan.RecipeOrdinal}";

    private void ResetAlteringStatus(IEnumerable<AlteringPlan> plans)
    {
        _alteringStatusItems.Clear();
        _alteringStatusOrder.Clear();
        _multiTelegramItems.Clear();
        _multiTelegramLastAction = "F9 다중가공 시작 / 계획 준비";
        _multiTelegramLastActionAt = DateTimeOffset.Now;
        var now = DateTimeOffset.Now;

        foreach (var plan in plans)
        {
            string key = AlteringStatusKey(plan);
            if (_alteringStatusItems.ContainsKey(key))
                continue;

            _alteringStatusOrder.Add(key);
            _alteringStatusItems[key] = new(
                key, plan.DisplayName, 0, plan.TargetQuantity, null, now);
            _multiTelegramItems[key] = new(
                plan.FacilityName, plan.DisplayName, 0, plan.TargetQuantity,
                0, plan.RequiredWorks, "준비", null);
        }
    }

    private void SeedAlteringStatus(
        AlteringPlan plan,
        long confirmedQuantity,
        long? remainingSeconds = null)
    {
        string key = AlteringStatusKey(plan);
        if (!_alteringStatusItems.ContainsKey(key))
            _alteringStatusOrder.Add(key);

        _alteringStatusItems[key] = new(
            key,
            plan.DisplayName,
            Math.Clamp(confirmedQuantity, 0, plan.TargetQuantity),
            plan.TargetQuantity,
            remainingSeconds,
            DateTimeOffset.Now);
        if (_multiTelegramItems.TryGetValue(key, out var item))
            _multiTelegramItems[key] = item with
            {
                ConfirmedQuantity = Math.Clamp(confirmedQuantity, 0, plan.TargetQuantity),
                EstimatedRemainingSeconds = remainingSeconds
            };
    }

    private void UpdateAlteringStatus(AlteringPlan plan, AlteringProgress progress)
    {
        string key = AlteringStatusKey(plan);
        if (!_alteringStatusItems.ContainsKey(key))
            _alteringStatusOrder.Add(key);

        _alteringStatusItems[key] = new(
            key,
            plan.DisplayName,
            Math.Clamp(progress.ConfirmedQuantity, 0, progress.TargetQuantity),
            progress.TargetQuantity,
            progress.TotalRemainingSeconds ??
                progress.BatchRemainingSeconds ??
                progress.NextCompletionSeconds,
            DateTimeOffset.Now);
        if (_multiTelegramItems.ContainsKey(key))
            _multiTelegramItems[key] = new(
                plan.FacilityName, plan.DisplayName,
                Math.Clamp(progress.ConfirmedQuantity, 0, progress.TargetQuantity),
                progress.TargetQuantity,
                progress.QueuedWorks,
                progress.RequiredWorks,
                progress.MaterialState,
                progress.TotalRemainingSeconds ??
                    progress.BatchRemainingSeconds ??
                    progress.NextCompletionSeconds);
    }

    private void SeedMultiAlteringTelegramItem(AlteringPlan plan,
        int queuedWorks, string stage)
    {
        string key = AlteringStatusKey(plan);
        if (_multiTelegramItems.TryGetValue(key, out var item))
            _multiTelegramItems[key] = item with
            {
                RegisteredWorks = queuedWorks,
                MaterialState = stage
            };
    }

    private void NoteMultiAlteringAction(string action)
    {
        if (!_multiAlteringRunning || string.IsNullOrWhiteSpace(action))
            return;
        _multiTelegramLastAction = action;
        _multiTelegramLastActionAt = DateTimeOffset.Now;
    }

    private MultiAlteringTelegramSnapshot? CaptureMultiAlteringTelegramSnapshot()
    {
        if (InvokeRequired)
            return (MultiAlteringTelegramSnapshot?)Invoke(
                new Func<MultiAlteringTelegramSnapshot?>(
                    CaptureMultiAlteringTelegramSnapshot));
        if (!_multiAlteringRunning)
            return null;

        var items = _alteringStatusOrder
            .Where(_multiTelegramItems.ContainsKey)
            .Select(key => _multiTelegramItems[key])
            .ToArray();
        return new MultiAlteringTelegramSnapshot(
            items, _multiTelegramLastAction, _multiTelegramLastActionAt);
    }

    private async Task<string> GetMultiAlteringTelegramDetailsAsync()
    {
        // Capture mutable UI status synchronously on its own UI thread,
        // then query the CLI asynchronously on the Telegram poll thread.
        // Never block the UI or invoke a game input from /status.
        var snapshot = CaptureMultiAlteringTelegramSnapshot();
        if (snapshot is null)
            return "";

        IReadOnlyList<AlteringWork>? works = null;
        string? issue = null;
        DateTimeOffset observedAt = DateTimeOffset.Now;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var response = await _cli.GetAlteringWorksAsync(timeout.Token)
                .ConfigureAwait(false);
            observedAt = DateTimeOffset.Now;
            if (response.Success)
                works = AlteringQueries.ParseWorks(response);
            else
                issue = "CLI 응답 실패 / 연결 상태 확인 필요";
        }
        catch (OperationCanceledException)
        {
            issue = "CLI 응답 시간 초과";
        }
        catch (Exception)
        {
            issue = "CLI 응답 파싱 또는 조회 실패";
        }
        return MultiAlteringTelegramStatus.Format(snapshot, works, observedAt, issue);
    }

    private string GetAlteringRemoteStatus()
    {
        var ordered = _alteringStatusOrder
            .Where(_alteringStatusItems.ContainsKey)
            .Select(key => _alteringStatusItems[key])
            .ToArray();

        return AlteringStatusFormatter.Format(
            ordered,
            _productionCurrentQuantity,
            _productionTargetQuantity,
            DateTimeOffset.Now);
    }
}
