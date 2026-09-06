using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Full.NET.UnitTests.ObservabilityAdmin;

[TestClass]
public sealed class CachePolicyControlPlaneTests
{
    [TestMethod]
    public void List_returns_registered_policies_without_exposing_connection_strings()
    {
        var controlPlane = CreateControlPlane(new FakeCacheInvalidator());
        var policies = controlPlane.List();

        Assert.IsGreaterThanOrEqualTo(policies.Count, 3);
        var tenantPolicy = policies.Single(policy => policy.EntryName == CacheEntryNames.TenantResolution);
        Assert.IsTrue(tenantPolicy.CanInvalidate);
        Assert.HasCount(1, tenantPolicy.InvalidationOperations);

        var serialized = System.Text.Json.JsonSerializer.Serialize(policies);
        Assert.DoesNotContain("RedisConnectionString", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task Invalidate_rejects_unknown_operations_and_unregistered_parameters()
    {
        var invalidator = new FakeCacheInvalidator();
        var controlPlane = CreateControlPlane(invalidator);
        var tenantId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf296");

        Assert.IsNull(await controlPlane.InvalidateAsync(
            CacheEntryNames.TenantResolution,
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

        Assert.AreEqual(0, invalidator.RemoveCalls);
    }

    [TestMethod]
    public async Task Invalidate_executes_registered_tenant_resolution_targets()
    {
        var invalidator = new FakeCacheInvalidator();
        var controlPlane = CreateControlPlane(invalidator);
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

        Assert.IsNotNull(result);
        Assert.HasCount(4, result.InvalidatedTargets);
        Assert.AreEqual(2, invalidator.RemoveCalls);
        Assert.AreEqual(2, invalidator.RemoveByTagCalls);
    }

    [TestMethod]
    public async Task Invalidate_rejects_unknown_grid_keys()
    {
        var invalidator = new FakeCacheInvalidator();
        var controlPlane = CreateControlPlane(invalidator);
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

        Assert.AreEqual(0, invalidator.RemoveCalls);
    }

    private static CachePolicyControlPlane CreateControlPlane(ICacheInvalidator invalidator)
    {
        var environment = new TestHostEnvironment
        {
            EnvironmentName = "Development",
            ContentRootFileProvider = new NullFileProvider(),
        };
        return new CachePolicyControlPlane(
            CachePolicyRegistry.Create(new CacheOptions()),
            invalidator,
            environment);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Full.NET.UnitTests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class FakeCacheInvalidator : ICacheInvalidator
    {
        public int RemoveCalls { get; private set; }
        public int RemoveByTagCalls { get; private set; }

        public ValueTask RemoveAsync(
            string entryName,
            string key,
            CacheInvalidationScope scope,
            CancellationToken cancellationToken = default)
        {
            RemoveCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveByTagAsync(
            string entryName,
            string tag,
            CacheInvalidationScope scope,
            CancellationToken cancellationToken = default)
        {
            RemoveByTagCalls++;
            return ValueTask.CompletedTask;
        }
    }
}
