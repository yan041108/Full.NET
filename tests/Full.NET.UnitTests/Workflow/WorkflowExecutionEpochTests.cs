using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>代次不是租约或节点名；来源缺失必须失败关闭，预算不能因代次绑定而重置。</summary>
[TestClass]
public sealed class WorkflowExecutionEpochTests
{
    [TestMethod]
    public async Task Closed_foreign_or_missing_epoch_cannot_authorize_dispatch()
    {
        var instance = Guid.CreateVersion7(); var epoch = Guid.CreateVersion7(); var query = Substitute.For<IQueryExecutor>();
        var row = new WorkflowExecutionEpochRecord(epoch, instance, "host", instance, Guid.CreateVersion7(), "region", 1, 1, "active", 1, DateTimeOffset.UtcNow, null);
        query.QuerySingleOrDefaultAsync<WorkflowExecutionEpochRecord>(WorkflowExecutionEpochSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        Assert.IsTrue(await WorkflowExecutionEpochScope.IsActiveAsync(query, instance, "host", epoch, default));
        foreach (var invalid in new[] { row with { InstanceId = Guid.CreateVersion7() }, row with { Id = Guid.CreateVersion7() },
                     row with { TenantScopeKey = "foreign" }, row with { StatusKey = "closed", ClosedAtUtc = DateTimeOffset.UtcNow } })
        {
            query.QuerySingleOrDefaultAsync<WorkflowExecutionEpochRecord>(WorkflowExecutionEpochSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(invalid);
            Assert.IsFalse(await WorkflowExecutionEpochScope.IsActiveAsync(query, instance, "host", epoch, default));
        }
    }
    [TestMethod]
    public async Task Empty_epoch_does_not_read_and_missing_epoch_does_not_dispatch()
    {
        var query = Substitute.For<IQueryExecutor>(); var instance = Guid.CreateVersion7();
        Assert.IsFalse(await WorkflowExecutionEpochScope.IsActiveAsync(query, instance, "host", Guid.Empty, default));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
        Assert.IsFalse(await WorkflowExecutionEpochScope.IsActiveAsync(query, instance, "host", Guid.CreateVersion7(), default));
    }
    [TestMethod]
    public void Dispatch_binds_one_real_epoch_without_resetting_shared_budget()
    {
        var budget = new WorkflowDispatchBudget { Remaining = 7, TotalRemaining = 19 }; var first = Guid.CreateVersion7();
        Assert.IsFalse(budget.TryBindExecutionEpoch(Guid.Empty));
        Assert.IsTrue(budget.TryBindExecutionEpoch(first)); Assert.IsTrue(budget.TryBindExecutionEpoch(first));
        Assert.IsFalse(budget.TryBindExecutionEpoch(Guid.CreateVersion7()));
        Assert.AreEqual(first, budget.ExecutionEpochId); Assert.AreEqual(7, budget.Remaining); Assert.AreEqual(19, budget.TotalRemaining);
    }
    [TestMethod]
    public async Task Continuation_reads_the_exact_source_step_epoch_in_the_trusted_scope()
    {
        var instance = Guid.CreateVersion7(); var step = Guid.CreateVersion7(); var epoch = Guid.CreateVersion7(); var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<Guid?>(WorkflowExecutionEpochSql.FindForStep, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(epoch);
        Assert.AreEqual(epoch, await WorkflowExecutionEpochScope.ReadForStepAsync(query, instance, "host", step, default));
        var values = (IReadOnlyDictionary<string, object?>)query.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.AreEqual(step, values["StepId"]); Assert.AreEqual(instance, values["InstanceId"]); Assert.AreEqual("host", values["TenantScopeKey"]);
        Assert.AreNotEqual(instance, epoch);
    }
    [TestMethod]
    public async Task Missing_source_or_empty_identity_never_falls_back_to_the_root_epoch()
    {
        var query = Substitute.For<IQueryExecutor>(); var instance = Guid.CreateVersion7(); var step = Guid.CreateVersion7();
        Assert.IsNull(await WorkflowExecutionEpochScope.ReadForStepAsync(query, Guid.Empty, "host", step, default));
        Assert.IsNull(await WorkflowExecutionEpochScope.ReadForStepAsync(query, instance, "host", Guid.Empty, default));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
        Assert.IsNull(await WorkflowExecutionEpochScope.ReadForStepAsync(query, instance, "host", step, default));
        query.QuerySingleOrDefaultAsync<Guid?>(WorkflowExecutionEpochSql.FindForStep, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Guid.Empty);
        Assert.IsNull(await WorkflowExecutionEpochScope.ReadForStepAsync(query, instance, "host", step, default));
    }
}
