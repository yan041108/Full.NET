using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowRecipientCandidateQueryServiceTests
{
    [TestMethod]
    public async Task Tenant_scope_only_uses_current_tenant_directory()
    {
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var currentTenant = Substitute.For<ICurrentTenant>();
        var hostUsers = Substitute.For<IHostUserSelectionDirectory>();
        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        currentTenant.IsHost.Returns(false);
        currentTenant.IsAvailable.Returns(true);
        currentTenant.Id.Returns(tenantId);
        tenantUsers.ListActiveTenantUsersAsync(1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<TenantUserDirectoryEntry>(
                [new TenantUserDirectoryEntry(userId, "tenant-user", "租户用户")],
                1,
                50,
                1));
        var service = new WorkflowRecipientCandidateQueryService(
            currentTenant,
            hostUsers,
            tenantUsers);

        var result = await service.ListAsync(1, 50);

        Assert.AreEqual(1L, result.Total);
        Assert.AreEqual(userId, result.Items.Single().Id);
        await tenantUsers.Received(1).ListActiveTenantUsersAsync(
            1,
            50,
            Arg.Any<CancellationToken>());
        await hostUsers.DidNotReceiveWithAnyArgs().ListActiveHostUsersAsync(
            default,
            default,
            default);
    }

    [TestMethod]
    public async Task Host_scope_only_uses_host_directory()
    {
        var userId = Guid.CreateVersion7();
        var currentTenant = Substitute.For<ICurrentTenant>();
        var hostUsers = Substitute.For<IHostUserSelectionDirectory>();
        var tenantUsers = Substitute.For<ITenantUserSelectionDirectory>();
        currentTenant.IsHost.Returns(true);
        hostUsers.ListActiveHostUsersAsync(1, 50, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<HostUserDirectoryEntry>(
                [new HostUserDirectoryEntry(userId, "host-user", "Host 用户")],
                1,
                50,
                1));
        var service = new WorkflowRecipientCandidateQueryService(
            currentTenant,
            hostUsers,
            tenantUsers);

        var result = await service.ListAsync(1, 50);

        Assert.AreEqual(1L, result.Total);
        Assert.AreEqual(userId, result.Items.Single().Id);
        await hostUsers.Received(1).ListActiveHostUsersAsync(
            1,
            50,
            Arg.Any<CancellationToken>());
        await tenantUsers.DidNotReceiveWithAnyArgs().ListActiveTenantUsersAsync(
            default,
            default,
            default);
    }
}
