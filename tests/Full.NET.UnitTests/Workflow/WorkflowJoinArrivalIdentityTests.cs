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
    [DataRow(0, 0, false)]
    [DataRow(1, 1, true)]
    public async Task Unknown_or_duplicate_branch_does_not_write(int selected, int arrivals, bool valid)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        var join = new WorkflowParallelJoinRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), "fork", "join",
            "parallel", 2, 1, "waiting", 2, DateTimeOffset.UtcNow, null);
        query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(join);
        query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountSelectedBranch,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(selected);
        query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountArrival,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(arrivals);
        var result = await new WorkflowParallelJoinCoordinator(command, query, Substitute.For<IIdGenerator>())
            .TryRecordArrivalAsync(join.InstanceId, join.Id, "a", DateTimeOffset.UtcNow);
        Assert.AreEqual(valid, result.IsValid);
        Assert.IsFalse(result.IsNewArrival);
        Assert.IsFalse(result.IsJoinComplete);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }
}
