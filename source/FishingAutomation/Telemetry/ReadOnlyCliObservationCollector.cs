namespace FishingAutomation.Telemetry;

// Only these enumerated *queries* can be called. No CLI action command or
// arbitrary user-provided arguments can be constructed by this collector.
internal enum CliDiagnosticQuery
{
    Status = 1,
    Activity = 2,
    AlteringWorks = 3,
    Items = 4,
    AlteringCatalog = 5
}

internal sealed record CliDiagnosticReadResult(
    string Status, CliDiagnosticProjection Projection,
    CommonObservationAdmission? Admission,
    CommonObservationAssessment? Assessment,
    bool AuthorizesGameplay = false);

/// <summary>
/// Opt-in read-only diagnostic pull adapter. A provided delegate invokes the
/// EXISTING validated query-only CLI methods; synthetic tests replace it with
/// a no-network delegate. The adapter never issues actions, retries or inputs.
/// No polling loop, file writer, saved CLI JSON or background hooks exist.
/// </summary>
internal sealed class ReadOnlyCliObservationCollector
{
    private readonly CommonObservationEngine _engine;
    private readonly Func<CliDiagnosticQuery, CancellationToken,
        Task<CliDiagnosticProjection>> _read;
    private readonly Func<DateTimeOffset> _now;
    private long _sequence;

    internal bool Enabled { get; }

    internal ReadOnlyCliObservationCollector(
        CommonObservationEngine engine,
        Func<CliDiagnosticQuery, CancellationToken,
            Task<CliDiagnosticProjection>> read,
        bool explicitlyEnabled = false,
        Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(read);
        _engine = engine;
        _read = read;
        Enabled = explicitlyEnabled && engine.DiagnosticsEnabled;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    internal async Task<CliDiagnosticReadResult> ReadOnceAsync(
        CliDiagnosticQuery query, CancellationToken token = default)
    {
        if (!Enabled)
            return new("disabled", new("disabled"), null, null);
        if (!Enum.IsDefined(query))
            return new("query_not_allowed", new("query_not_allowed"), null, null);

        token.ThrowIfCancellationRequested();
        CliDiagnosticProjection projection;
        try
        {
            projection = await _read(query, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or TimeoutException)
        {
            return new("cli_query_unavailable", new("cli_query_unavailable"), null, null);
        }

        // A syntactically valid inventory/catalog/empty-work snapshot has
        // no gameplay-state vote. Its counts are returned only as diagnostics.
        if (projection.Domain is null || projection.State is null)
            return new(projection.Status, projection, null, null);
        DateTimeOffset observed = _now().ToUniversalTime();
        var claim = new CommonObservation(
            projection.Domain.Value, CommonObservationSource.ReadOnlyCli,
            projection.State.Value, CommonEvidenceLevel.ValidatedReadOnlyCli,
            Interlocked.Increment(ref _sequence), observed,
            observed.AddSeconds(3));
        var admission = _engine.TryObserve(claim, observed);
        var assessment = _engine.Evaluate(claim.Domain, observed);
        return new(admission.Accepted ? projection.Status : admission.Status,
            projection, admission, assessment);
    }

    internal CommonObservationAdmission ObserveSchedulerRunning()
    {
        if (!Enabled)
            return new(false, "disabled");
        // This method only reports an EXISTING local active run. It does not
        // query a server or imply processing is complete.
        DateTimeOffset at = _now().ToUniversalTime();
        return _engine.TryObserve(new CommonObservation(
            CommonObservationDomain.Scheduler,
            CommonObservationSource.LocalScheduler,
            CommonObservationState.InProgress,
            CommonEvidenceLevel.LocalRuntimeDiagnostic,
            Interlocked.Increment(ref _sequence), at, at.AddSeconds(3)), at);
    }

    internal MultiAlteringObservationAudit.Result CompareMultiAltering(
        CommonObservationState? localStage,
        CliDiagnosticReadResult? observedWorks)
    {
        if (!Enabled || observedWorks is null ||
            observedWorks.Admission?.Accepted != true ||
            observedWorks.Projection.Domain != CommonObservationDomain.Altering)
            return new("insufficient_cross_evidence", true);
        return MultiAlteringObservationAudit.Compare(
            localStage, observedWorks.Projection);
    }
}
