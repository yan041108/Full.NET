using Full.NET.Abstractions.Time;
using Microsoft.Extensions.Options;
using Full.NET.Caching.Fusion;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;
using Microsoft.Extensions.DependencyInjection;
using Full.NET.Hosting.Observability;
using NSubstitute;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.UnitTests.ObservabilityAdmin;

/// <summary>单元测试（可观测管理）：Cache Policy Control Plane。</summary>
[TestClass]
public sealed class CachePolicyControlPlaneTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Summary_exports_precise_registered_policy_and_unknown_runtime_via_source_generated_json(bool failSafe)
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions
        {
            Entries = { ["demo.projection"] = new CacheEntryDefinitionOptions
            {
                OwnerModule = "demo", ConsistencyClass = "S2", L1Duration = TimeSpan.FromSeconds(2),
                L2Duration = TimeSpan.FromSeconds(30), Jitter = TimeSpan.FromMilliseconds(1250),
                NegativeDuration = failSafe ? null : TimeSpan.FromMilliseconds(750), FailSafeEnabled = failSafe,
                FailSafeMaxDuration = failSafe ? TimeSpan.FromSeconds(90) : null, MaxSerializedBytes = 12345,
            } },
        });
        var plane = new CachePolicyControlPlane(registry, Substitute.For<IClock>(),
            Options.Create(new CacheOptions()), Substitute.For<IDiagnosticPolicyStore>());
        var summary = plane.Get("demo.projection");
        Assert.IsNotNull(summary);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(summary,
            Full.NET.Modules.ObservabilityAdmin.Serialization.ObservabilityAdminJsonSerializerContext.Default.CachePolicySummary));
        var data = json.RootElement;
        Assert.AreEqual(1.25, data.GetProperty("jitterSeconds").GetDouble());
        Assert.AreEqual(failSafe, data.GetProperty("failSafeEnabled").GetBoolean());
        if (failSafe)
        {
            Assert.AreEqual(System.Text.Json.JsonValueKind.Null, data.GetProperty("negativeDurationSeconds").ValueKind);
            Assert.AreEqual(90d, data.GetProperty("failSafeMaxDurationSeconds").GetDouble());
        }
        else
        {
            Assert.AreEqual(0.75, data.GetProperty("negativeDurationSeconds").GetDouble());
            Assert.AreEqual(System.Text.Json.JsonValueKind.Null, data.GetProperty("failSafeMaxDurationSeconds").ValueKind);
        }
        Assert.IsFalse(data.GetProperty("requiresVersionRecheck").GetBoolean());
        Assert.AreEqual(12345, data.GetProperty("maxSerializedBytes").GetInt32());
        Assert.AreEqual("unknown", data.GetProperty("runtimeStatus").GetString());
    }

    [TestMethod]
    [DataRow("C0")]
    [DataRow("N0")]
    public void Summary_marks_cache_budgets_not_applicable_for_authority_and_bypass(string consistency)
    {
        var registry = CachePolicyRegistry.Create(new CacheOptions
        {
            Entries = { ["demo.authority"] = new CacheEntryDefinitionOptions
                { OwnerModule = "demo", ConsistencyClass = consistency } },
        });
        var summary = new CachePolicyControlPlane(registry, Substitute.For<IClock>(),
            Options.Create(new CacheOptions()), Substitute.For<IDiagnosticPolicyStore>()).Get("demo.authority");
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(summary,
            Full.NET.Modules.ObservabilityAdmin.Serialization.ObservabilityAdminJsonSerializerContext.Default.CachePolicySummary));
        foreach (var name in new[] { "jitterSeconds", "negativeDurationSeconds", "failSafeMaxDurationSeconds", "maxSerializedBytes" })
            Assert.AreEqual(System.Text.Json.JsonValueKind.Null, json.RootElement.GetProperty(name).ValueKind);
        Assert.IsFalse(json.RootElement.GetProperty("failSafeEnabled").GetBoolean());
        Assert.AreEqual("not_applicable", json.RootElement.GetProperty("runtimeStatus").GetString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Summary_reflects_current_node_observation_without_claiming_freshness(bool observing)
    {
        var observation = new CacheRuntimeObservationState();
        observation.SetMonitoring(observing);
        var plane = new CachePolicyControlPlane(CachePolicyRegistry.Create(new CacheOptions()),
            Substitute.For<IClock>(), Options.Create(new CacheOptions()), Substitute.For<IDiagnosticPolicyStore>(),
            observation: observation);
        var summary = plane.Get(CacheEntryNames.GridPreference);
        Assert.IsNotNull(summary);
        Assert.AreEqual(observing ? "observing" : "unknown", summary.RuntimeStatus);
    }

    [TestMethod]
    [DataRow("identity.users")]
    [DataRow("settings.future-grid")]
    public async Task Grid_operation_delegates_catalog_and_version_ownership_to_settings(string gridKey)
    {
        var owner = Substitute.For<IGridPreferenceCacheInvalidator>();
        var userId = Guid.NewGuid();
        owner.InvalidateAsync(userId, gridKey, true, Arg.Any<CancellationToken>()).Returns(true);
        var services = new ServiceCollection();
        services.AddSingleton(owner);
        using var provider = services.BuildServiceProvider();
        var plane = ActivatorUtilities.CreateInstance<CachePolicyControlPlane>(provider,
            CachePolicyRegistry.Create(new CacheOptions()), Substitute.For<IClock>(), Options.Create(new CacheOptions()),
            Substitute.For<IDiagnosticPolicyStore>());
        var result = await plane.InvalidateAsync(CacheEntryNames.GridPreference,
            new CacheInvalidationRequest("by-user-grid", new Dictionary<string, string>
            { ["userId"] = userId.ToString(), ["gridKey"] = gridKey }, null), CancellationToken.None);
        Assert.IsNotNull(result);
        await owner.Received(1).InvalidateAsync(userId, gridKey, true, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Grid_operation_is_unavailable_without_its_owner_module()
    {
        var policy = CreateControlPlane().Get(CacheEntryNames.GridPreference);
        Assert.IsNotNull(policy);
        Assert.IsFalse(policy.CanInvalidate);
        Assert.IsEmpty(policy.InvalidationOperations);
    }

    [TestMethod]
    public async Task Diagnostic_operation_refreshes_the_actual_local_snapshot()
    {
        var store = Substitute.For<IDiagnosticPolicyStore>();
        var services = new ServiceCollection();
        services.AddSingleton(store);
        using var provider = services.BuildServiceProvider();
        var plane = ActivatorUtilities.CreateInstance<CachePolicyControlPlane>(provider,
            CachePolicyRegistry.Create(new CacheOptions()), Substitute.For<IClock>(), Options.Create(new CacheOptions()));
        var result = await plane.InvalidateAsync(CacheEntryNames.DiagnosticPolicy,
            new CacheInvalidationRequest("global", null, CacheInvalidationScopeNames.CurrentNodeOnly), CancellationToken.None);
        Assert.IsNotNull(result);
        await store.Received(1).RefreshAsync(0, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Snapshot_refresh_rejects_unregistered_parameters()
    {
        var result = await CreateControlPlane().InvalidateAsync(CacheEntryNames.DiagnosticPolicy,
            new CacheInvalidationRequest("global", new Dictionary<string, string>
            {
                ["tenantId"] = Guid.NewGuid().ToString(), ["domain"] = new string('a', 300) + ".test",
            }, null), CancellationToken.None);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void List_returns_registered_policies_without_exposing_connection_strings()
    {
        var controlPlane = CreateControlPlane();
        var policies = controlPlane.List();

        Assert.IsGreaterThanOrEqualTo(policies.Count, 3);
        var tenantPolicy = policies.Single(policy => policy.EntryName == CacheEntryNames.TenantResolution);
        Assert.IsFalse(tenantPolicy.CanInvalidate);
        Assert.IsEmpty(tenantPolicy.InvalidationOperations);

        var serialized = System.Text.Json.JsonSerializer.Serialize(policies);
        Assert.DoesNotContain("RedisConnectionString", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task Invalidate_rejects_unknown_operations_and_unregistered_parameters()
    {
        var controlPlane = CreateControlPlane();
        var tenantId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf296");

        Assert.IsNull(await controlPlane.InvalidateAsync(
            CacheEntryNames.DiagnosticPolicy,
            new CacheInvalidationRequest("unknown", null, null),
            CancellationToken.None));

        Assert.IsNull(await controlPlane.InvalidateAsync(
            CacheEntryNames.TenantResolution,
            new CacheInvalidationRequest(
                "by-tenant",
                new Dictionary<string, string>
                {
                    ["tenantId"] = tenantId.ToString(),
                    ["domain"] = "tenant.example.com",
                    ["extra"] = "forbidden",
                },
                null),
            CancellationToken.None));

    }

    [TestMethod]
    public async Task Invalidate_rejects_retired_tenant_resolution_targets()
    {
        var controlPlane = CreateControlPlane();
        var tenantId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf296");

        var result = await controlPlane.InvalidateAsync(
            CacheEntryNames.TenantResolution,
            new CacheInvalidationRequest(
                "by-tenant",
                new Dictionary<string, string>
                {
                    ["tenantId"] = tenantId.ToString(),
                    ["domain"] = "Tenant.Example.Com.",
                },
                CacheInvalidationScopeNames.AllLayersSynchronous),
            CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task Invalidate_rejects_unknown_grid_keys()
    {
        var owner = Substitute.For<IGridPreferenceCacheInvalidator>();
        var controlPlane = new CachePolicyControlPlane(CachePolicyRegistry.Create(new CacheOptions()),
            Substitute.For<IClock>(), Options.Create(new CacheOptions()), Substitute.For<IDiagnosticPolicyStore>(), owner);
        var userId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf297");

        Assert.IsNull(await controlPlane.InvalidateAsync(
            CacheEntryNames.GridPreference,
            new CacheInvalidationRequest(
                "by-user-grid",
                new Dictionary<string, string>
                {
                    ["userId"] = userId.ToString(),
                    ["gridKey"] = "unknown.grid",
                },
                null),
            CancellationToken.None));
        await owner.Received(1).InvalidateAsync(userId, "unknown.grid", true, Arg.Any<CancellationToken>());

    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Snapshot_summary_reports_real_age_and_rejects_cross_node_refresh(bool safeDefault)
    {
        var now = DateTimeOffset.Parse("2026-10-17T00:00:00Z");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var store = Substitute.For<IDiagnosticPolicyStore>();
        store.Current.Returns(new DiagnosticPolicySnapshot(1, LoggingPressureState.Normal, [], now.AddSeconds(-2.5), safeDefault));
        var plane = new CachePolicyControlPlane(CachePolicyRegistry.Create(new CacheOptions()), clock,
            Options.Create(new CacheOptions()), store);
        var summary = plane.Get(CacheEntryNames.DiagnosticPolicy);
        Assert.IsNotNull(summary);
        Assert.AreEqual(CachePolicyAccessKinds.LocalSnapshot, summary.AccessKind);
        Assert.AreEqual(2.5, summary.SnapshotAgeSeconds);
        Assert.AreEqual(60d, summary.SnapshotMaxAgeSeconds);
        Assert.IsNull(summary.L1DurationSeconds);
        Assert.IsNull(summary.L2DurationSeconds);
        Assert.AreEqual(safeDefault ? "safe_default" : "snapshot_loaded", summary.RuntimeStatus);
        Assert.IsNull(await plane.InvalidateAsync(CacheEntryNames.DiagnosticPolicy,
            new CacheInvalidationRequest("global", null, CacheInvalidationScopeNames.AllLayersSynchronous), CancellationToken.None));
        await store.DidNotReceive().RefreshAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        var result = await plane.InvalidateAsync(CacheEntryNames.DiagnosticPolicy,
            new CacheInvalidationRequest("global", null, null), CancellationToken.None);
        Assert.IsNotNull(result);
        Assert.AreEqual(CacheInvalidationScopeNames.CurrentNodeOnly, result.Scope);
        await store.Received(1).RefreshAsync(0, Arg.Any<CancellationToken>());
    }

    private static CachePolicyControlPlane CreateControlPlane() => new(
        CachePolicyRegistry.Create(new CacheOptions()), Substitute.For<IClock>(),
        Options.Create(new CacheOptions()), Substitute.For<IDiagnosticPolicyStore>());
}
