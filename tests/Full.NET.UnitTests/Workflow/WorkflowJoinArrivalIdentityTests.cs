using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>汇合只接受本次分叉选定身份；重复到达不依赖唯一异常，也不重复推进。</summary>
[TestClass]
public sealed class WorkflowJoinArrivalIdentityTests
{
    [TestMethod]
    [DataRow(0, "join", false)]
    [DataRow(1, "join", true)]
    [DataRow(1, "other", false)]
    public async Task Return_continuation_join_uses_only_the_exact_receipt_and_never_falls_back_to_ordinary_source(int proofs, string target, bool valid)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var epoch = Guid.CreateVersion7(); var source = Guid.CreateVersion7(); var round = Guid.CreateVersion7(); var work = Guid.CreateVersion7();
        var join = new WorkflowParallelJoinRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), "fork", "join", "parallel", 2, 1, "waiting", 2, DateTimeOffset.UtcNow, null, epoch);
        query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(join);
        query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountSelectedBranch, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountArrival, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<int>(WorkflowCausalitySql.CountSourceFrame, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<int>(WorkflowReturnRoundSql.CountJoinSourceProof, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(proofs);
        var result = await new WorkflowParallelJoinCoordinator(command, query, Substitute.For<IIdGenerator>()).TryRecordArrivalAsync(join.InstanceId,
            epoch, join.Id, "fork", "join", "left", DateTimeOffset.UtcNow, sourceStepId: source, tenantScopeKey: "host", returnProof: new(round, work, target, new string('a', 64), 3));
        Assert.AreEqual(valid, result.IsValid);
        await query.DidNotReceive().QuerySingleOrDefaultAsync<int>(WorkflowCausalitySql.CountSourceFrame, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.HasCount(0, command.ReceivedCalls().ToArray());
    }

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, true)]
    public async Task Receipted_route_source_requires_its_exact_leased_work_proof(int proofs, bool valid)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var epoch = Guid.CreateVersion7(); var source = Guid.CreateVersion7(); var work = Guid.CreateVersion7();
        var join = new WorkflowParallelJoinRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), "fork", "join", "parallel", 2, 1, "waiting", 2, DateTimeOffset.UtcNow, null, epoch);
        query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(join);
        query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountSelectedBranch, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountArrival, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<int>(WorkflowCausalitySql.CountSourceFrame, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QuerySingleOrDefaultAsync<int>(WorkflowRouteSql.CountJoinSourceProof, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(proofs);
        var result = await new WorkflowParallelJoinCoordinator(command, query, Substitute.For<IIdGenerator>()).TryRecordArrivalAsync(join.InstanceId,
            epoch, join.Id, "fork", "join", "left", DateTimeOffset.UtcNow, sourceStepId: source, tenantScopeKey: "host", routeProof: new(source, work, "join", new string('a', 64)));
        Assert.AreEqual(valid, result.IsValid); Assert.IsFalse(result.IsNewArrival); Assert.AreEqual(0, command.ReceivedCalls().Count());
        await query.DidNotReceive().QuerySingleOrDefaultAsync<int>(WorkflowCausalitySql.CountSourceFrame, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        var values = (IReadOnlyDictionary<string, object?>)query.ReceivedCalls().Single(call => Equals(call.GetArguments()[0], WorkflowRouteSql.CountJoinSourceProof)).GetArguments()[1]!;
        Assert.AreEqual(source, values["RouteDecisionId"]); Assert.AreEqual(work, values["WorkId"]); Assert.AreEqual(join.Id, values["ParallelJoinId"]);
        Assert.AreEqual("left", values["ParallelBranchKey"]); Assert.AreEqual("join", values["JoinNodeKey"]);
    }

    [TestMethod]
    public async Task A_route_receipt_for_another_target_cannot_be_reused_for_a_join()
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>(); var source = Guid.CreateVersion7();
        var epoch = Guid.CreateVersion7(); var join = new WorkflowParallelJoinRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), "fork", "join", "parallel", 2, 0, "waiting", 1, DateTimeOffset.UtcNow, null, epoch);
        query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(join);
        query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountSelectedBranch, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var result = await new WorkflowParallelJoinCoordinator(command, query, Substitute.For<IIdGenerator>()).TryRecordArrivalAsync(join.InstanceId,
            epoch, join.Id, "fork", "join", "left", DateTimeOffset.UtcNow, sourceStepId: source, tenantScopeKey: "host", routeProof: new(source, Guid.CreateVersion7(), "other", new string('a', 64)));
        Assert.IsFalse(result.IsValid); Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(0, 0, false)]
    [DataRow(1, 1, true)]
    public async Task Unknown_or_duplicate_branch_does_not_write(int selected, int arrivals, bool valid)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var epoch = Guid.CreateVersion7();
        var join = new WorkflowParallelJoinRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), "fork", "join",
            "parallel", 2, 1, "waiting", 2, DateTimeOffset.UtcNow, null, epoch);
        query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(join);
        query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountSelectedBranch,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(selected);
        query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountArrival,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(arrivals);
        query.QuerySingleOrDefaultAsync<int>(WorkflowCausalitySql.CountSourceFrame,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var result = await new WorkflowParallelJoinCoordinator(command, query, Substitute.For<IIdGenerator>())
            .TryRecordArrivalAsync(join.InstanceId, epoch, join.Id, "fork", "join", "a", DateTimeOffset.UtcNow,
                sourceStepId:Guid.CreateVersion7(),tenantScopeKey:"host");
        Assert.AreEqual(valid, result.IsValid);
        Assert.IsFalse(result.IsNewArrival);
        Assert.IsFalse(result.IsJoinComplete);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("epoch")]
    [DataRow("empty")]
    [DataRow("instance")]
    [DataRow("activation")]
    [DataRow("fork")]
    [DataRow("join")]
    public async Task Previous_round_or_foreign_frame_never_records_an_arrival(string mismatch)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var epoch = Guid.CreateVersion7(); var instance = Guid.CreateVersion7(); var activation = Guid.CreateVersion7();
        var join = new WorkflowParallelJoinRecord(activation, instance, "fork", "join", "parallel", 2, 0, "waiting", 1,
            DateTimeOffset.UtcNow, null, epoch);
        join = mismatch switch {
            "epoch" => join with { ExecutionEpochId = Guid.CreateVersion7() },
            "instance" => join with { InstanceId = Guid.CreateVersion7() },
            "activation" => join with { Id = Guid.CreateVersion7() },
            "fork" => join with { ForkNodeKey = "other" },
            "join" => join with { JoinNodeKey = "other" }, _ => join };
        query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(join);
        var result = await new WorkflowParallelJoinCoordinator(command, query, Substitute.For<IIdGenerator>())
            .TryRecordArrivalAsync(instance, mismatch == "empty" ? Guid.Empty : epoch, activation, "fork", "join", "a", DateTimeOffset.UtcNow);
        Assert.IsFalse(result.IsValid); Assert.IsFalse(result.IsNewArrival); Assert.IsFalse(result.IsJoinComplete);
        Assert.AreEqual(activation, mismatch == "activation" ? activation : join.ForkActivationId);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
        Assert.AreEqual(1, query.ReceivedCalls().Count());
    }
}
