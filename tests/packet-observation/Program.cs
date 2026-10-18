using FishingAutomation.Telemetry;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    checks++;
}

var now = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
PacketObservation Sample(
    PacketObservationKind kind = PacketObservationKind.CombatActivityHint,
    long sequence = 1,
    double? value = null,
    string adapter = "fixture/v1",
    DateTimeOffset? observed = null,
    DateTimeOffset? expires = null)
{
    var time = observed ?? now;
    return new(kind, adapter, sequence, time, expires ?? time.AddSeconds(3), value);
}

var disabled = new PacketObservationInbox(allowedAdapters: new[] { "fixture/v1" });
Check(!disabled.DiagnosticsEnabled, "default must be disabled");
Check(disabled.TryObserve(Sample(), now).Reason == "disabled", "disabled inbox accepted signal");
Check(disabled.ReadFresh(PacketObservationKind.CombatActivityHint, now) is null, "disabled read leaked signal");

var noAdapters = new PacketObservationInbox(diagnosticsEnabled: true);
Check(noAdapters.TryObserve(Sample(), now).Reason == "unapproved_adapter", "adapter allowlist required");

var inbox = new PacketObservationInbox(diagnosticsEnabled: true,
    allowedAdapters: new[] { "fixture/v1", "fixture/other" });
Check(inbox.TryObserve(Sample(), now).Accepted, "first synthetic observation rejected");
Check(inbox.ReadFresh(PacketObservationKind.CombatActivityHint, now)?.Sequence == 1,
    "fresh observation not returned");
Check(inbox.TryObserve(Sample(), now).Reason == "duplicate_or_reordered",
    "duplicate sequence accepted");
Check(inbox.TryObserve(Sample(sequence: 0), now).Reason == "invalid_metadata",
    "zero sequence accepted");
Check(inbox.TryObserve(Sample(sequence: 2, adapter: "unregistered"), now).Reason ==
    "unapproved_adapter", "unapproved source accepted");
Check(inbox.TryObserve(Sample(sequence: 2, observed: now.AddSeconds(-6),
    expires: now.AddSeconds(2)), now).Reason == "stale", "old data accepted");
Check(inbox.TryObserve(Sample(sequence: 2, observed: now.AddSeconds(2),
    expires: now.AddSeconds(5)), now).Reason == "future", "future data accepted");
Check(inbox.TryObserve(Sample(sequence: 2, expires: now), now).Reason ==
    "invalid_validity", "zero lifetime accepted");
Check(inbox.TryObserve(Sample(sequence: 2, expires: now.AddSeconds(11)), now).Reason ==
    "invalid_validity", "excessive lifetime accepted");
Check(inbox.TryObserve(Sample(sequence: 2, expires: now.AddMilliseconds(10)),
    now.AddMilliseconds(11)).Reason == "stale", "expired event accepted");

Check(inbox.TryObserve(Sample(PacketObservationKind.TargetHealthFractionHint, value: 0.42),
    now).Accepted, "health fraction missing");
Check(inbox.ReadFresh(PacketObservationKind.TargetHealthFractionHint, now)?.Value == 0.42,
    "health fraction changed");
foreach (double invalid in new[] { -0.01, 1.01, double.NaN, double.PositiveInfinity })
{
    Check(inbox.TryObserve(Sample(PacketObservationKind.TargetHealthFractionHint,
        sequence: 2, value: invalid), now).Reason == "invalid_value",
        "invalid numeric value accepted");
}
Check(inbox.TryObserve(Sample(PacketObservationKind.TargetHealthFractionHint, 
    sequence: 2), now).Reason == "invalid_value", "missing fraction accepted");
Check(inbox.TryObserve(Sample(sequence: 2, value: 0.5), now).Reason ==
    "unexpected_value", "value allowed for no-value hint");
Check(inbox.TryObserve(Sample((PacketObservationKind)999, sequence: 2), now).Reason ==
    "invalid_metadata", "unknown kind accepted");
Check(inbox.TryObserve(null, now).Reason == "empty", "null observation accepted");

Check(inbox.TryObserve(Sample(PacketObservationKind.CombatActivityHint, 
    sequence: 2, adapter: "fixture/other", observed: now.AddMilliseconds(-1)),
    now).Reason == "older_observation", "older timestamp replaced latest state");
Check(inbox.TryObserve(Sample(sequence: 2, observed: now.AddMilliseconds(100),
    expires: now.AddSeconds(3)), now).Accepted, "in-order event refused");
Check(inbox.ReadFresh(PacketObservationKind.CombatActivityHint, now.AddSeconds(5)) is null,
    "expired event remained readable");
Check(inbox.ReadFresh(PacketObservationKind.TargetHealthFractionHint, now.AddSeconds(5)) is null,
    "health hint remained after expiry");
Check(inbox.TryObserve(Sample(sequence: 3), now).Reason == "older_observation",
    "new sequence with old observed time overrode latest");

inbox.Clear();
Check(inbox.ReadFresh(PacketObservationKind.CombatActivityHint, now) is null,
    "cleared data still accessible");
Check(inbox.TryObserve(Sample(), now).Accepted, "clear did not reset sequence");
Check(inbox.ReadFresh(PacketObservationKind.EncounterEndedHint, now) is null,
    "unobserved hint returned a synthetic value");

Console.WriteLine($"PASS {checks} synthetic telemetry admission regressions (no game, network or packet data).");
