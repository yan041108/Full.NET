using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class TenantMembershipActiveTenantGuardTests
{
    [TestMethod]
    public async Task EnsureCurrentTenantActiveAsync_fails_when_tenant_suspended()
    {
        var tenantId = Guid.NewGuid();
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.IsAvailable.Returns(true);
        currentTenant.Id.Returns(tenantId);
        var directory = Substitute.For<IIdentityActiveTenantDirectory>();
        directory.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await TenantMembershipActiveTenantGuard.EnsureCurrentTenantActiveAsync(
            currentTenant,
            directory,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.TenantMembershipTenantInactive, result.Error!.Code);
    }

    [TestMethod]
    public async Task EnsureTenantActiveAsync_succeeds_when_tenant_active()
    {
        var tenantId = Guid.NewGuid();
        var directory = Substitute.For<IIdentityActiveTenantDirectory>();
        directory.IsActiveTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await TenantMembershipActiveTenantGuard.EnsureTenantActiveAsync(
            tenantId,
            directory,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
    }
}
