using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using NSubstitute;
using Available = Full.NET.Modules.Tenancy.Features.GetAvailableTenants;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>自助上下文列表不能泄漏其他企业；显式平台授权保留其声明的目录范围。</summary>
[TestClass]
public sealed class AvailableTenantMembershipScopeTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Available_list_is_intersected_with_authoritative_memberships(bool platform)
    {
        var own = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        var resolver = Substitute.For<ITenantResolver>();
        resolver.GetAvailableAsync(Arg.Any<CancellationToken>()).Returns(new[] {
            new TenantSummary(own, "own", "Own", "own.test", true, 1),
            new TenantSummary(other, "other", "Other", "other.test", true, 1) });
        var access = Substitute.For<IIdentityTenantContextAccess>();
        access.ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new IdentityTenantContextAccess(platform, new[] { own }));
        var result = await new Available.Handler(resolver, access).HandleAsync(new Available.Query(Guid.CreateVersion7()), default);
        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(platform ? new[] { own, other } : new[] { own }, result.Value!.Select(item => item.Id).ToArray());
    }
}
