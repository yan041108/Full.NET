using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Tenancy;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class TenantCachePayloadOwnershipTests
{
    private static readonly string[] CachePayloadTypeNames =
    [
        "TenantCachePayload",
        "TenantResolutionCacheEntry",
    ];

    [TestMethod]
    public void Tenancy_cache_payload_shapes_are_internal_to_the_tenancy_module()
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
                           || item.TenancyType is null
                           || item.TenancyType.IsPublic)
            .Select(item => item.TypeName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }
}
