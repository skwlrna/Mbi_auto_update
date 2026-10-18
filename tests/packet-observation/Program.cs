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


string Timestamp(DateTimeOffset time) =>
    time.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

string FixtureRow(
    string kind = "combat_activity",
    long sequence = 1,
    double? value = null,
    DateTimeOffset? at = null,
    DateTimeOffset? until = null)
{
    var values = new Dictionary<string, object>
    {
        ["schemaVersion"] = 1,
        ["kind"] = kind,
        ["sequence"] = sequence,
        ["observedAtUtc"] = Timestamp(at ?? now),
        ["expiresAtUtc"] = Timestamp(until ?? now.AddSeconds(3))
    };
    if (value.HasValue)
        values["value"] = value.Value;
    return JsonSerializer.Serialize(values);
}

var fixtureInbox = new PacketObservationInbox(diagnosticsEnabled: true,
    allowedAdapters: new[] { PacketObservationFixtureReplay.FixtureAdapterId });
var fixtureText = string.Join("\n",
    FixtureRow("combat_activity"),
    FixtureRow("target_health_fraction", value: 0.25),
    FixtureRow("encounter_ended"));
var noOptIn = PacketObservationFixtureReplay.Replay(
    new StringReader(fixtureText), fixtureInbox, now);
Check(noOptIn.Status == "disabled" && noOptIn.Records == 0,
    "fixture replay without explicit opt-in");
Check(fixtureInbox.ReadFresh(PacketObservationKind.CombatActivityHint, now) is null,
    "disabled replay admitted event");

var validReplay = PacketObservationFixtureReplay.Replay(
    new StringReader(fixtureText), fixtureInbox, now, explicitlyEnabled: true);
Check(validReplay.Status == "replayed" && validReplay.Records == 3 &&
    validReplay.Accepted == 3 && validReplay.Rejected == 0,
    "verified fixture batch was not admitted");
Check(fixtureInbox.ReadFresh(PacketObservationKind.TargetHealthFractionHint, now)?.Value == 0.25,
    "fixture health field missing");

var duplicateReplay = PacketObservationFixtureReplay.Replay(
    new StringReader(fixtureText), fixtureInbox, now, explicitlyEnabled: true);
Check(duplicateReplay.Records == 3 && duplicateReplay.Accepted == 0 &&
    duplicateReplay.Rejected == 3, "replaying same fixture bypassed sequence guard");

var strangerInbox = new PacketObservationInbox(diagnosticsEnabled: true);
var strangerResult = PacketObservationFixtureReplay.Replay(
    new StringReader(FixtureRow()), strangerInbox, now, explicitlyEnabled: true);
Check(strangerResult.Status == "replayed" && strangerResult.Accepted == 0 &&
    strangerResult.Rejected == 1, "fixture ignored approved-adapter requirement");

void MustRejectEntireFixture(string text, string name)
{
    var fresh = new PacketObservationInbox(true,
        new[] { PacketObservationFixtureReplay.FixtureAdapterId });
    var result = PacketObservationFixtureReplay.Replay(
        new StringReader(text), fresh, now, explicitlyEnabled: true);
    Check(result.Status != "replayed" && result.Accepted == 0,
        "invalid fixture accepted: " + name);
    Check(fresh.ReadFresh(PacketObservationKind.CombatActivityHint, now) is null,
        "partial replay polluted inbox: " + name);
}
MustRejectEntireFixture("", "empty");
MustRejectEntireFixture("[]", "not object");
MustRejectEntireFixture("{broken", "malformed json");
MustRejectEntireFixture(FixtureRow().Replace("\"schemaVersion\":1",
    "\"schemaVersion\":2"), "schema version");
MustRejectEntireFixture(FixtureRow().Replace("\"sequence\":1",
    "\"sequence\":0"), "invalid sequence");
MustRejectEntireFixture(FixtureRow("unverified_packet_250"), "unknown kind");
MustRejectEntireFixture(FixtureRow("combat_activity", value: 0.5), "unexpected value");
MustRejectEntireFixture(FixtureRow("target_health_fraction"), "missing health");
MustRejectEntireFixture(FixtureRow("target_health_fraction", value: 1.5), "invalid health");
MustRejectEntireFixture(FixtureRow().Replace("\"kind\":\"combat_activity\"",
    "\"kind\":\"combat_activity\",\"kind\":\"encounter_ended\""), "duplicate key");
MustRejectEntireFixture(FixtureRow().Replace("\"sequence\":1",
    "\"sequence\":1,\"adapterId\":\"impostor\""), "unknown key");
MustRejectEntireFixture(FixtureRow().Replace("\"observedAtUtc\":\"", 
    "\"observedAtUtc\":\"2026-10-18T13:00:00+09:00"), "non UTC");
MustRejectEntireFixture(FixtureRow() + "\n" + "{broken", "batch atomic validation");
MustRejectEntireFixture(new string('x', 2049), "oversized row");
MustRejectEntireFixture(string.Join("\n", Enumerable.Repeat(FixtureRow(), 129)),
    "too many rows");

var staleResult = PacketObservationFixtureReplay.Replay(
    new StringReader(FixtureRow()), new PacketObservationInbox(true,
        new[] { PacketObservationFixtureReplay.FixtureAdapterId }),
    now.AddSeconds(6), explicitlyEnabled: true);
Check(staleResult.Status == "replayed" && staleResult.Rejected == 1 &&
    staleResult.Accepted == 0, "stale fixture promoted to active event");

fixtureInbox.Clear();
Check(fixtureInbox.ReadFresh(PacketObservationKind.EncounterEndedHint, now) is null,
    "clear failed after replay");
Console.WriteLine($"PASS {checks} synthetic telemetry and offline JSONL fixture regressions.");

