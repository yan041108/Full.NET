using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>候选查询先验证实际待办资源，不借用定义设计权限或扩大 Host 目录。</summary>
[TestClass]
public sealed class WorkflowTodoAssigneeCandidateQueryServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Transfer_candidate_scope_supports_multi_approval_without_definition_or_countersign_permission(bool isTenant)
    {
        var f = Create(isTenant, "multi");
        f.Host.ListActiveHostUsersAsync(1, 50, Arg.Any<CancellationToken>()).Returns(new PagedResult<HostUserDirectoryEntry>([new(f.Actor, "actor", "Actor")], 1, 50, 1));
        f.TenantUsers.ListActiveTenantMembersAsync(1, 50, Arg.Any<CancellationToken>()).Returns(new PagedResult<TenantUserDirectoryEntry>([new(f.Actor, "actor", "Actor")], 1, 50, 1));
        var result = await f.Service.ListTransferAsync(f.Todo.Id, f.Actor, 1, 50);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(f.Actor, result.Value!.Items.Single().Id);
        await f.Host.Received(isTenant ? 0 : 1).ListActiveHostUsersAsync(1, 50, Arg.Any<CancellationToken>());
        await f.TenantUsers.Received(isTenant ? 1 : 0).ListActiveTenantMembersAsync(1, 50, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("transfer_forbidden")]
    [DataRow("foreign")]
    public async Task Transfer_denial_never_opens_the_member_directory(string scenario)
    {
        var f = Create(false, scenario);
        var result = await f.Service.ListTransferAsync(f.Todo.Id, scenario == "foreign" ? Guid.CreateVersion7() : f.Actor, 1, 50);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, f.Host.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Unbounded_page_does_not_overflow_the_owner_directory_offset()
    {
        var f = Create(false);
        var result = await f.Service.ListCountersignAsync(f.Todo.Id, f.Actor, int.MaxValue, 100);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.SchemaInvalid, result.Error!.Code);
        Assert.AreEqual(0, f.Host.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Owned_active_todo_queries_only_its_trusted_directory(bool isTenant)
    {
        var f = Create(isTenant);
        f.Host.ListActiveHostUsersAsync(1, 50, Arg.Any<CancellationToken>()).Returns(new PagedResult<HostUserDirectoryEntry>([new(f.Actor, "actor", "Actor")], 1, 50, 1));
        f.TenantUsers.ListActiveTenantMembersAsync(1, 50, Arg.Any<CancellationToken>()).Returns(new PagedResult<TenantUserDirectoryEntry>([new(f.Actor, "actor", "Actor")], 1, 50, 1));
        var result = await f.Service.ListCountersignAsync(f.Todo.Id, f.Actor, 1, 50);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(f.Actor, result.Value!.Items.Single().Id);
        await f.Host.Received(isTenant ? 0 : 1).ListActiveHostUsersAsync(1, 50, Arg.Any<CancellationToken>());
        await f.TenantUsers.Received(isTenant ? 1 : 0).ListActiveTenantMembersAsync(1, 50, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("foreign", WorkflowErrorCodes.TodoForbidden)]
    [DataRow("completed", WorkflowErrorCodes.InvalidTransition)]
    [DataRow("before_countersign", WorkflowErrorCodes.InvalidTransition)]
    [DataRow("paused", WorkflowErrorCodes.InvalidTransition)]
    [DataRow("forbidden", WorkflowErrorCodes.TodoActionForbidden)]
    [DataRow("missing", WorkflowErrorCodes.TodoNotActive)]
    public async Task Resource_denial_never_enumerates_any_user_directory(string scenario, string code)
    {
        var f = Create(false, scenario);
        var result = await f.Service.ListCountersignAsync(f.Todo.Id, scenario == "foreign" ? Guid.CreateVersion7() : f.Actor, 1, 50);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(code, result.Error!.Code);
        Assert.AreEqual(0, f.Host.ReceivedCalls().Count()); Assert.AreEqual(0, f.TenantUsers.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Multi_approval_uses_the_same_scoped_paged_candidate_directory()
    {
        var f = Create(false, "multi");
        f.Host.ListActiveHostUsersAsync(1, 50, Arg.Any<CancellationToken>()).Returns(new PagedResult<HostUserDirectoryEntry>([new(f.Actor, "actor", "Actor")], 1, 50, 1));
        var result = await f.Service.ListCountersignAsync(f.Todo.Id, f.Actor, 1, 50);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(1, result.Value!.Items.Count);
        await f.Host.Received(1).ListActiveHostUsersAsync(1, 50, Arg.Any<CancellationToken>());
    }

    private static Fixture Create(bool isTenant, string scenario = "active")
    {
        var query = Substitute.For<IQueryExecutor>(); var actor = Guid.CreateVersion7(); var instanceId = Guid.CreateVersion7();
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instanceId, Guid.CreateVersion7(), actor,
            scenario is "completed" or "before_countersign" ? scenario : "active", DateTimeOffset.UtcNow, null, null, 1,
            "review", 1, scenario == "multi" ? "all" : "single", 1, 1, null, null, null);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(scenario == "missing" ? null : todo);
        var instance = new WorkflowInstanceRecord(instanceId, null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "one", null,
            scenario == "paused" ? "paused" : "active", 1, actor, DateTimeOffset.UtcNow, null, null, null, null, null, null);
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        var config = scenario == "forbidden" ? "{\"operationPermissions\":{\"countersign\":false}}"
            : scenario == "transfer_forbidden" ? "{\"operationPermissions\":{\"transfer\":false}}" : "{}";
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(
            new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, instance.FormVersionId!.Value,
                "{\"schemaVersion\":1,\"nodes\":[{\"nodeKey\":\"review\",\"nodeTypeKey\":\"human.approval\",\"nodeSchemaVersion\":1,\"config\":" + config + "}]}", "{}", null, "active"));
        var tenant = new CurrentTenantAccessor();
        if (isTenant) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "test", "Test")); else tenant.SetHost();
        var host = Substitute.For<IHostUserSelectionDirectory>(); var tenantUsers = Substitute.For<ITenantMemberSelectionDirectory>();
        return new(new(query, tenant, host, tenantUsers), todo, actor, host, tenantUsers);
    }

    private sealed record Fixture(WorkflowTodoAssigneeCandidateQueryService Service, WorkflowTodoRuntimeRecord Todo, Guid Actor,
        IHostUserSelectionDirectory Host, ITenantMemberSelectionDirectory TenantUsers);
}
