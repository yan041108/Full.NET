using Full.NET.Hosting.Observability;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class DiagnosticPolicySnapshotTests
{
    [TestMethod]
    public void Pressure_only_shrinks_best_effort_capacity()
    {
        var now = DateTimeOffset.UtcNow;
        var degraded = new DiagnosticPolicySnapshot(
            1,
            LoggingPressureState.Degraded,
            [],
            now,
            IsDefault: false);
        var critical = degraded with { PressureState = LoggingPressureState.Critical };
        Assert.AreEqual(50, degraded.ResolveBestEffortCapacity(100));
        Assert.AreEqual(25, critical.ResolveBestEffortCapacity(100));
        Assert.AreEqual(100, DiagnosticPolicySnapshot.CreateDefault(now).ResolveBestEffortCapacity(100));
    }

    [TestMethod]
    public void Scoped_sample_rate_override_applies_to_matching_endpoint()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new DiagnosticPolicySnapshot(
            1,
            LoggingPressureState.Normal,
            [
                new DiagnosticPolicyRule(
                    DiagnosticPolicyScopeKind.Endpoint,
                    "/api/v1/settings/diagnostic-policy",
                    SuccessSampleRateOverride: 1.0,
                    BestEffortCapacityOverride: null,
                    MaxRequestPayloadBytesOverride: null,
                    MaxResponsePayloadBytesOverride: null,
                    ExpiresAtUtc: now.AddMinutes(10)),
            ],
            now,
            IsDefault: false);

        Assert.AreEqual(
            1.0,
            snapshot.ResolveSuccessSampleRateOverride(
                LogClassification.HttpOperation,
                "/api/v1/settings/diagnostic-policy",
                traceId: null,
                tenantId: null));
        Assert.IsNull(
            snapshot.ResolveSuccessSampleRateOverride(
                LogClassification.HttpOperation,
                "/api/v1/other",
                traceId: null,
                tenantId: null));
    }
}
