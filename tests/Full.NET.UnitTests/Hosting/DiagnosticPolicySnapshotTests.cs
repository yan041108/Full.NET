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

    [TestMethod]
    public void Expired_rule_cannot_expand_sampling_or_reduce_capacity_from_stale_snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new DiagnosticPolicySnapshot(
            2,
            LoggingPressureState.Normal,
            [
                new DiagnosticPolicyRule(
                    DiagnosticPolicyScopeKind.Endpoint,
                    "/api/items",
                    SuccessSampleRateOverride: 1.0,
                    BestEffortCapacityOverride: 1,
                    MaxRequestPayloadBytesOverride: null,
                    MaxResponsePayloadBytesOverride: null,
                    ExpiresAtUtc: now.AddMinutes(-1)),
            ],
            now.AddMinutes(-2),
            IsDefault: false);

        Assert.IsNull(snapshot.ResolveSuccessSampleRateOverride(
            LogClassification.HttpOperation, "/api/items", null, null));
        Assert.AreEqual(100, snapshot.ResolveBestEffortCapacity(100));
    }

    [TestMethod]
    public void Diagnostic_category_rule_does_not_expand_http_operation_sampling()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new DiagnosticPolicySnapshot(
            3,
            LoggingPressureState.Normal,
            [
                new DiagnosticPolicyRule(
                    DiagnosticPolicyScopeKind.Category,
                    LogClassification.Diagnostic,
                    SuccessSampleRateOverride: 1.0,
                    BestEffortCapacityOverride: null,
                    MaxRequestPayloadBytesOverride: null,
                    MaxResponsePayloadBytesOverride: null,
                    ExpiresAtUtc: now.AddMinutes(5)),
            ],
            now,
            IsDefault: false);

        Assert.IsNull(snapshot.ResolveSuccessSampleRateOverride(
            HttpOperationLogMiddleware.DiagnosticGroup, "/api/items", null, null));
    }

    [TestMethod]
    public void Default_store_reuses_immutable_snapshot_on_hot_path()
    {
        var store = new DefaultDiagnosticPolicyStore();
        var first = store.Current;
        var second = store.Current;

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void Tenant_scoped_capacity_override_cannot_shrink_every_request()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new DiagnosticPolicySnapshot(
            4,
            LoggingPressureState.Normal,
            [
                new DiagnosticPolicyRule(
                    DiagnosticPolicyScopeKind.Tenant,
                    Guid.NewGuid().ToString(),
                    SuccessSampleRateOverride: null,
                    BestEffortCapacityOverride: 1,
                    MaxRequestPayloadBytesOverride: null,
                    MaxResponsePayloadBytesOverride: null,
                    ExpiresAtUtc: now.AddMinutes(5)),
            ],
            now,
            IsDefault: false);

        Assert.AreEqual(100, snapshot.ResolveBestEffortCapacity(100));
    }
}
