using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowSubprocessStarterPreparationTests
{
    [TestMethod]
    public async Task Parent_starter_is_checked_once_for_all_items_and_mismatched_scope_does_not_query()
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetHost(); var user = Guid.CreateVersion7(); var host = Substitute.For<IHostUserBatchSelectionDirectory>();
        var members = Substitute.For<ITenantMemberSelectionDirectory>();
        host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [user] = new(user, "owner", "Owner") });
        var service = new WorkflowSubprocessStarterPreparation(host, members, tenant); var source = new WorkflowMultiInstancePreparedSource([new("item-0001", 1, "1"), new("item-0002", 2, "2")], null, []);
        var prepared = await service.PrepareAsync(null, user, new Dictionary<string, JsonElement>(), source, new(null, "host", "host"), default);
        Assert.IsNotNull(prepared); Assert.AreEqual(user, prepared.ByItemKey["item-0001"]); Assert.AreEqual(user, prepared.ByItemKey["item-0002"]);
        await host.Received(1).FindActiveHostUsersAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == 1 && ids.Contains(user)), Arg.Any<CancellationToken>());
        Assert.IsNull(await service.PrepareAsync(null, user, new Dictionary<string, JsonElement>(), source, new(Guid.CreateVersion7(), "tenant", "tenant:foreign"), default));
        Assert.AreEqual(1, host.ReceivedCalls().Count()); Assert.AreEqual(0, members.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Role_item_starter_must_belong_to_the_frozen_role_and_still_be_active()
    {
        var tenant = new CurrentTenantAccessor(); tenant.SetHost(); var user = Guid.CreateVersion7(); var host = Substitute.For<IHostUserBatchSelectionDirectory>();
        host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry>());
        var service = new WorkflowSubprocessStarterPreparation(host, Substitute.For<ITenantMemberSelectionDirectory>(), tenant);
        var source = new WorkflowMultiInstancePreparedSource([new("item-0001", 1, JsonSerializer.Serialize(user))], Guid.CreateVersion7(), [user]);
        var policy = new WorkflowSubprocessStarterPolicy("item_user", null, null, null);
        Assert.IsNull(await service.PrepareAsync(policy, Guid.CreateVersion7(), new Dictionary<string, JsonElement>(), source, new(null, "host", "host"), default));
        host.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, HostUserDirectoryEntry> { [user] = new(user, "member", "Member") });
        var accepted = await service.PrepareAsync(policy, Guid.CreateVersion7(), new Dictionary<string, JsonElement>(), source, new(null, "host", "host"), default);
        Assert.IsNotNull(accepted); Assert.AreEqual(user, accepted.ByItemKey["item-0001"]);
        Assert.IsNull(await service.PrepareAsync(policy, user, new Dictionary<string, JsonElement>(), source with { RoleMembers = [] }, new(null, "host", "host"), default));
        Assert.AreEqual(2, host.ReceivedCalls().Count());
    }
}
