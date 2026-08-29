using Full.NET.Caching.Fusion;
using Full.NET.Modules.Tenancy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.UnitTests.Tenancy;

internal static class TenantCacheInvalidatorTestFactory
{
    internal static TenantCacheInvalidator Create(
        IFusionCache cache,
        IHostEnvironment environment)
    {
        var policies = CachePolicyRegistry.Create(new CacheOptions());
        return new TenantCacheInvalidator(
            new FusionCacheInvalidator(cache, policies),
            environment,
            policies,
            NullLogger<TenantCacheInvalidator>.Instance);
    }
}
