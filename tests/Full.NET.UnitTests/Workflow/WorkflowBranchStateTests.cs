using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证冻结输入、局部修订及无隐式覆盖的原子合并。</summary>
[TestClass]
public sealed class WorkflowBranchStateTests
{
    [TestMethod]
    public async Task Continuation_revision_uses_its_branch_instead_of_the_main_form()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowBranchStateRecord>(WorkflowBranchSql.Find,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowBranchStateRecord("a", "{}", 2, "{}", 7));
        var coordinator = new WorkflowBranchStateCoordinator(query, Substitute.For<ICommandExecutor>());
        Assert.AreEqual(7L, await coordinator.ReadRevisionAsync(Instance(), Guid.CreateVersion7(), "a", default));
        await query.DidNotReceive().QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
            WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.IsNull(await coordinator.ReadRevisionAsync(Instance(), Guid.CreateVersion7(), null, default));
    }

    [TestMethod]
    public async Task Nested_merge_updates_its_parent_branch_instead_of_the_shared_form()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var parentJoin = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<WorkflowForkContextRecord>(WorkflowBranchSql.FindContext,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowForkContextRecord(parentJoin, "parent"));
        query.QueryAsync<WorkflowBranchStateRecord>(WorkflowBranchSql.List, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkflowBranchStateRecord("inner", "{}", 4, """{"first":1}""", 2) });
        query.QuerySingleOrDefaultAsync<WorkflowBranchStateRecord>(WorkflowBranchSql.Find,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowBranchStateRecord("parent", "{}", 1, "{}", 4));
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var instance = Instance();
        var result = await new WorkflowBranchStateCoordinator(query, command).MergeAsync(
            instance, Guid.CreateVersion7(), instance.StartedById, DateTimeOffset.UtcNow, default);
        Assert.IsTrue(result.IsSuccess);
        await command.DidNotReceive().ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(WorkflowBranchSql.Update,
            Arg.Is<object?>(value => ((IReadOnlyDictionary<string, object?>)value!)["ParallelJoinId"]!.Equals(parentJoin)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Branch_detail_uses_its_frozen_input_and_local_revision()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowForkContextRecord>(WorkflowBranchSql.FindContext,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowForkContextRecord(null, null));
        var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowBranchStateRecord>(WorkflowBranchSql.Find,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowBranchStateRecord(
                "a", """{"first":1,"second":1}""", 1, """{"first":2}""", 3));
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), "active", DateTimeOffset.UtcNow, null, null, 1, "approve", 1,
            "single", 1, 1, Guid.CreateVersion7(), "a", "join");
        var main = new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), todo.InstanceId, Guid.CreateVersion7(),
            """{"first":9,"second":9}""", "none", 9, todo.AssigneeUserId, DateTimeOffset.UtcNow);

        var result = await new WorkflowBranchStateCoordinator(query, command).ReadAsync(todo, main, "host", default);

        Assert.AreEqual(3L, result!.Revision);
        using var json = JsonDocument.Parse(result.SubmissionJson);
        Assert.AreEqual(2, json.RootElement.GetProperty("first").GetInt32());
        Assert.AreEqual(1, json.RootElement.GetProperty("second").GetInt32());
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Disjoint_outputs_merge_against_the_original_form_revision()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowForkContextRecord>(WorkflowBranchSql.FindContext,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowForkContextRecord(null, null));
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QueryAsync<WorkflowBranchStateRecord>(WorkflowBranchSql.List,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[]
            {
                new WorkflowBranchStateRecord("b", "{}", 7, """{"second":2}""", 3),
                new WorkflowBranchStateRecord("a", "{}", 7, """{"first":1}""", 5),
            });
        var instance = Instance();

        var result = await new WorkflowBranchStateCoordinator(query, command).MergeAsync(
            instance, Guid.CreateVersion7(), instance.StartedById, DateTimeOffset.UtcNow, default);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!["first"].GetInt32());
        Assert.AreEqual(2, result.Value["second"].GetInt32());
        await command.Received(1).ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision,
            Arg.Is<object?>(value => (long)((IReadOnlyDictionary<string, object?>)value!)["Revision"]! == 7),
            Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(WorkflowBranchSql.InsertMerge,
            Arg.Is<object?>(value => (long)((IReadOnlyDictionary<string, object?>)value!)["FormRevision"]! == 8),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Overlapping_outputs_conflict_even_when_values_are_equal()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowForkContextRecord>(WorkflowBranchSql.FindContext,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowForkContextRecord(null, null));
        var command = Substitute.For<ICommandExecutor>();
        query.QueryAsync<WorkflowBranchStateRecord>(WorkflowBranchSql.List,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[]
            {
                new WorkflowBranchStateRecord("a", "{}", 1, """{"same":2}""", 2),
                new WorkflowBranchStateRecord("b", "{}", 1, """{"same":2}""", 2),
            });
        var instance = Instance();
        var result = await new WorkflowBranchStateCoordinator(query, command).MergeAsync(
            instance, Guid.CreateVersion7(), instance.StartedById, DateTimeOffset.UtcNow, default);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Concurrent_main_form_change_prevents_a_merge_receipt()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowForkContextRecord>(WorkflowBranchSql.FindContext,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowForkContextRecord(null, null));
        var command = Substitute.For<ICommandExecutor>();
        query.QueryAsync<WorkflowBranchStateRecord>(WorkflowBranchSql.List,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[]
            { new WorkflowBranchStateRecord("a", "{}", 1, "{}", 1) });
        var instance = Instance();
        var result = await new WorkflowBranchStateCoordinator(query, command).MergeAsync(
            instance, Guid.CreateVersion7(), instance.StartedById, DateTimeOffset.UtcNow, default);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        await command.DidNotReceive().ExecuteAsync(WorkflowBranchSql.InsertMerge,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private static WorkflowInstanceRecord Instance() => new(Guid.CreateVersion7(), null, "host", "host",
        Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "one", null, "active", 1,
        Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, null, null, null, null, null);
}
