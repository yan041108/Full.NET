using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>层级额度不得重置为4096，缺失子预留凭据时失败关闭。</summary>
[TestClass]
public sealed class WorkflowExecutionBudgetPolicyTests
{
    [TestMethod]
    public async Task Ordinary_flow_without_children_keeps_existing_budget_without_extra_query()
    {
        var query = Substitute.For<IQueryExecutor>();
        Assert.AreEqual(4089, await WorkflowExecutionBudgetPolicy.ReadRemainingAsync(Root("order"), false, 7, query, default));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_child_or_collaboration_grant_cannot_fall_back_to_a_new_root_budget(bool collaboration)
    {
        var query = Substitute.For<IQueryExecutor>();
        Assert.AreEqual(-1, await WorkflowExecutionBudgetPolicy.ReadRemainingAsync(Root(collaboration ? WorkflowCollaborationStartContext.BusinessType : WorkflowChildStartContext.BusinessType), false, 0, query, default));
    }
    [TestMethod]
    [DataRow(32, 0, 3, 29)]
    [DataRow(32, 28, 4, 0)]
    [DataRow(32, 33, 0, -1)]
    [DataRow(2, 0, 2, 0)]
    public async Task Plain_target_without_subprocess_nodes_still_consumes_only_the_agreed_slice(int granted, int reserved, int used, int expected)
    {
        var root = Root(WorkflowCollaborationStartContext.BusinessType); var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowExecutionBudgetRecord>(WorkflowSubprocessSql.FindBudget, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowExecutionBudgetRecord(root.Id, granted, reserved, 1));
        Assert.AreEqual(expected, await WorkflowExecutionBudgetPolicy.ReadRemainingAsync(root, false, used, query, default));
    }
    [TestMethod]
    [DataRow(64, 32, 7, 25)]
    [DataRow(64, 65, 0, -1)]
    [DataRow(4096, 0, 4096, 0)]
    public async Task Child_uses_only_its_grant_less_descendant_reservations(int granted, int reserved, int used, int expected)
    {
        var root = Root(WorkflowChildStartContext.BusinessType); var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowExecutionBudgetRecord>(WorkflowSubprocessSql.FindBudget, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowExecutionBudgetRecord(root.Id, granted, reserved, 1));
        Assert.AreEqual(expected, await WorkflowExecutionBudgetPolicy.ReadRemainingAsync(root, true, used, query, default));
    }
    private static WorkflowInstanceRecord Root(string type) => new(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
        type, "request", null, "active", 1, Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, null, null, null, null, null);
}
