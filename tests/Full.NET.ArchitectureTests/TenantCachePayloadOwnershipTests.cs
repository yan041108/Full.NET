using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Tenancy;

namespace Full.NET.ArchitectureTests;

/// <summary>架构测试：TenantCachePayloadOwnership 约束与回归门禁。</summary>
[TestClass]
public sealed class TenantCachePayloadOwnershipTests
{
    private static readonly string[] CachePayloadTypeNames =
    [
        "TenantCachePayload",
        "TenantResolutionCacheEntry",
    ];

    [TestMethod]
    public void Tenancy_authority_reads_have_no_retired_cache_payloads()
    {
        var abstractionTypes = typeof(ICurrentTenant).Assembly.GetTypes();
        var tenancyTypes = typeof(TenancyModule).Assembly.GetTypes();
        var offenders = CachePayloadTypeNames
            .Select(typeName => new
            {
                TypeName = typeName,
                AbstractionType = abstractionTypes.SingleOrDefault(type => type.Name == typeName),
                TenancyType = tenancyTypes.SingleOrDefault(type => type.Name == typeName),
            })
            .Where(item => item.AbstractionType is not null
                           || item.TenancyType is not null)
            .Select(item => item.TypeName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }
}
