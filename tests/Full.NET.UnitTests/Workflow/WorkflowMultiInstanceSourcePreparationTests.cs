using System.Text.Json;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Domain;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>真实角色来源只经最小目录，不接受任意主体数组，也不把未知角色当作可跳过空集合。</summary>
[TestClass]
public sealed class WorkflowMultiInstanceSourcePreparationTests
{
    private static WorkflowMultiInstancePolicy Role(Guid id, string empty = "reject") => new("parallel", "role_members", 2, null, null, id, "member", 100, empty, new Dictionary<string, string>());
    [TestMethod]
    public async Task Active_role_snapshot_is_copied_and_overflow_is_rejected_without_truncation()
    {
        var id = Guid.CreateVersion7(); var members = new List<Guid> { Guid.CreateVersion7(), Guid.CreateVersion7() }; var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 3, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [id] = members });
        var service = new WorkflowMultiInstanceSourcePreparation(roles); var prepared = await service.PrepareAsync(Role(id), new Dictionary<string, JsonElement>(), default);
        Assert.IsNotNull(prepared); Assert.AreEqual(2, prepared.Items.Count); members.Add(Guid.CreateVersion7()); Assert.AreEqual(2, prepared.RoleMembers.Count);
        Assert.IsNull(await service.PrepareAsync(Role(id), new Dictionary<string, JsonElement>(), default));
        await roles.Received(2).FindActiveMemberUserIdsByRoleIdsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == 1 && ids.Contains(id)), 3, Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Missing_role_is_not_an_explicitly_empty_active_role()
    {
        var id = Guid.CreateVersion7(); var roles = Substitute.For<IWorkflowRoleMemberDirectory>();
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 3, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IReadOnlyList<Guid>>());
        Assert.IsNull(await new WorkflowMultiInstanceSourcePreparation(roles).PrepareAsync(Role(id, "skip"), new Dictionary<string, JsonElement>(), default));
        roles.FindActiveMemberUserIdsByRoleIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), 3, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, IReadOnlyList<Guid>> { [id] = [] });
        var empty = await new WorkflowMultiInstanceSourcePreparation(roles).PrepareAsync(Role(id, "skip"), new Dictionary<string, JsonElement>(), default);
        Assert.IsNotNull(empty); Assert.AreEqual(0, empty.Items.Count);
    }
    [TestMethod]
    public async Task Fixed_count_does_not_query_identity()
    {
        var roles = Substitute.For<IWorkflowRoleMemberDirectory>(); var policy = new WorkflowMultiInstancePolicy("serial", "fixed_count", 2, 2, null, null, null, 100, "reject", new Dictionary<string, string>());
        Assert.IsNotNull(await new WorkflowMultiInstanceSourcePreparation(roles).PrepareAsync(policy, new Dictionary<string, JsonElement>(), default)); Assert.AreEqual(0, roles.ReceivedCalls().Count());
    }
}
