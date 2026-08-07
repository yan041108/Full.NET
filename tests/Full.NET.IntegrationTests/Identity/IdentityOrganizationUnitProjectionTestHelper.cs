using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Features.OrganizationUnitProjection;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>在集成测试中同步 Identity 机构单元投影，避免依赖 Worker 排空 Outbox。</summary>
internal static class IdentityOrganizationUnitProjectionTestHelper
{
    public static async Task BackfillTenantAsync(
        FullNetApiFactory factory,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<OrganizationUnitProjectionBackfillService>()
            .BackfillTenantAsync(tenantId, cancellationToken);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
        Assert.IsGreaterThan(0, result.Value!.AppliedCount);
    }
}
