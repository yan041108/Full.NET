using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>预览与激活使用相同的来源、附加成员和去重顺序，不能隐式插入预览操作人。</summary>
[TestClass]
public sealed class WorkflowAssigneePreviewServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Preview_includes_additional_members_in_the_same_order_as_activation(bool tenantScope)
    {
        var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var host = Substitute.For<IHostUserBatchSelectionDirectory>(); var members = Substitute.For<ITenantMemberSelectionDirectory>();
        host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call =>
            (call.Arg<IReadOnlyCollection<Guid>>() ?? []).ToDictionary(id => id, id => new HostUserDirectoryEntry(id, "user", "User")));
        members.FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call =>
            (call.Arg<IReadOnlyCollection<Guid>>() ?? []).ToDictionary(id => id, id => new TenantUserDirectoryEntry(id, "user", "User")));
        var tenant = new CurrentTenantAccessor();
        if (tenantScope) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "test", "Test")); else tenant.SetHost();
        var resolver = new WorkflowAssigneeResolver(host, members, Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var service = new WorkflowAssigneePreviewService(tenant, resolver, host, members);
        var policy = JsonSerializer.SerializeToElement(new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { first } } } });
        var result = await service.PreviewAsync(new(policy, actor, [second, first]), actor);
        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { first, second }, result.Value!.Users.Select(x => x.Id).ToArray());
        await host.Received(tenantScope ? 0 : 2).FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
        await members.Received(tenantScope ? 2 : 0).FindActiveTenantMembersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("duplicate")]
    [DataRow("overflow")]
    public async Task Invalid_additional_members_fail_before_directory_access(string scenario)
    {
        var id = Guid.CreateVersion7();
        Guid[] additional = scenario switch { "empty" => [Guid.Empty], "duplicate" => [id, id],
            _ => Enumerable.Range(0, 21).Select(_ => Guid.CreateVersion7()).ToArray() };
        var host = Substitute.For<IHostUserBatchSelectionDirectory>(); var members = Substitute.For<ITenantMemberSelectionDirectory>();
        var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        var resolver = new WorkflowAssigneeResolver(host, members, Substitute.For<IWorkflowRoleMemberDirectory>(), Substitute.For<IWorkflowOrganizationAssigneeDirectory>());
        var policy = JsonSerializer.SerializeToElement(new { sources = new[] { new { resolverKindKey = "initiator" } } });
        var result = await new WorkflowAssigneePreviewService(tenant, resolver, host, members).PreviewAsync(new(policy, id, additional), id);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, host.ReceivedCalls().Count()); Assert.AreEqual(0, members.ReceivedCalls().Count());
    }
}
