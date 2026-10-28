using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>局部代次只能沿固定外层分支继续；错误父链不能靠同根身份获准。</summary>
[TestClass]
public sealed class WorkflowExecutionFrameScopeTests
{
    [TestMethod]
    public async Task Root_without_a_branch_needs_no_invented_region_frame()
    {
        var data = Create();
        Assert.IsTrue(await WorkflowExecutionFrameScope.IsValidAsync(data.Query, data.Instance, data.Instance.Id, null, null, default));
    }

    [TestMethod]
    public async Task Nested_local_regions_can_use_their_actual_enclosing_fork()
    {
        var data = Create();
        Assert.IsTrue(await WorkflowExecutionFrameScope.IsValidAsync(data.Query, data.Instance, data.Leaf, data.Fork.Id, "left", default));
    }

    [TestMethod]
    public async Task Nested_global_regions_return_only_through_global_parent_frames()
    {
        var data = Create();
        foreach (var key in data.Frames.Keys.ToArray()) data.Frames[key] = data.Frames[key] with { ParentForkActivationId = null, ParentBranchKey = null };
        Assert.IsTrue(await WorkflowExecutionFrameScope.IsValidAsync(data.Query, data.Instance, data.Leaf, null, null, default));
    }

    [TestMethod]
    public async Task A_local_region_cannot_drop_the_branch_identity_to_escape_its_fork()
    {
        var data = Create();
        Assert.IsFalse(await WorkflowExecutionFrameScope.IsValidAsync(data.Query, data.Instance, data.Leaf, null, null, default));
    }

    [TestMethod]
    [DataRow("sibling")]
    [DataRow("foreignFork")]
    [DataRow("version")]
    [DataRow("scope")]
    [DataRow("foreignRoot")]
    [DataRow("missingFrame")]
    [DataRow("closedParent")]
    [DataRow("depth")]
    [DataRow("cycle")]
    [DataRow("missingAncestor")]
    [DataRow("oldForkEpoch")]
    [DataRow("unselected")]
    public async Task Unproven_parent_frames_cannot_authorize_a_branch_arrival(string defect)
    {
        var data = Create(); var frame = data.Frames[data.Parent]; var epoch = data.Epochs[data.Parent];
        switch (defect)
        {
            case "sibling": data.Frames[data.Parent] = frame with { ParentBranchKey = "right" }; break;
            case "foreignFork": data.Frames[data.Parent] = frame with { ParentForkActivationId = Guid.CreateVersion7() }; break;
            case "version": data.Frames[data.Parent] = frame with { DefinitionVersionId = Guid.CreateVersion7() }; break;
            case "scope": data.Frames[data.Parent] = frame with { TenantScopeKey = "foreign" }; break;
            case "foreignRoot": data.Frames[data.Parent] = frame with { InstanceId = Guid.CreateVersion7() }; break;
            case "missingFrame": data.Frames.Remove(data.Parent); break;
            case "closedParent": data.Epochs[data.Parent] = epoch with { StatusKey = "closed", ClosedAtUtc = DateTimeOffset.UtcNow }; break;
            case "depth": data.Epochs[data.Parent] = epoch with { Depth = 3 }; break;
            case "cycle": data.Epochs[data.Parent] = epoch with { ParentEpochId = data.Leaf }; break;
            case "missingAncestor": data.Epochs.Remove(data.Instance.Id); break;
            case "oldForkEpoch": data.Fork = data.Fork with { ExecutionEpochId = Guid.CreateVersion7() }; break;
            case "unselected": data.Query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountSelectedBranch, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0); break;
        }
        Assert.IsFalse(await WorkflowExecutionFrameScope.IsValidAsync(data.Query, data.Instance, data.Leaf, data.Fork.Id, "left", default));
    }

    [TestMethod]
    [DataRow("orphanBranch")]
    [DataRow("emptyFork")]
    [DataRow("wrongScope")]
    [DataRow("suspended")]
    public async Task Invalid_input_cannot_even_read_a_frame(string defect)
    {
        var data = Create(); var instance = data.Instance;
        if (defect == "wrongScope") instance = instance with { TenantScopeKey = "tenant:foreign" };
        if (defect == "suspended") instance = instance with { StatusKey = "suspended" };
        Assert.IsFalse(await WorkflowExecutionFrameScope.IsValidAsync(data.Query, instance, data.Instance.Id,
            defect == "emptyFork" ? Guid.Empty : null, defect is "orphanBranch" or "emptyFork" ? "left" : null, default));
        Assert.AreEqual(0, data.Query.ReceivedCalls().Count());
    }

    internal static FrameCase Create()
    {
        var root = Guid.CreateVersion7(); var parent = Guid.CreateVersion7(); var leaf = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        var instance = new WorkflowInstanceRecord(root, null, "host", "host", Guid.CreateVersion7(), null, "test", "frame", null, "active", 1, Guid.CreateVersion7(), now, null, null, null, null, null, null);
        var fork = new WorkflowParallelJoinRecord(Guid.CreateVersion7(), root, "fork", "join", "parallel", 2, 0, "waiting", 1, now, null, root);
        var data = new FrameCase(instance, parent, leaf, fork, Substitute.For<IQueryExecutor>());
        data.Epochs[root] = new(root, root, "host", null, null, "root", 0, 0, "active", 1, now, null);
        data.Epochs[parent] = new(parent, root, "host", root, Guid.CreateVersion7(), "outer", 1, 1, "active", 1, now, null);
        data.Epochs[leaf] = new(leaf, root, "host", parent, Guid.CreateVersion7(), "inner", 1, 2, "active", 1, now, null);
        foreach (var id in new[] { parent, leaf }) data.Frames[id] = new(id, root, "host", instance.DefinitionVersionId, "entry", "exit", fork.Id, "left", now);
        data.Query.QuerySingleOrDefaultAsync<WorkflowExecutionEpochRecord>(WorkflowExecutionEpochSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => data.Epochs.GetValueOrDefault(Argument(call.ArgAt<object>(1), "ExecutionEpochId")));
        data.Query.QuerySingleOrDefaultAsync<WorkflowRegionFrameRecord>(WorkflowRegionSql.FindFrame, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => data.Frames.GetValueOrDefault(Argument(call.ArgAt<object>(1), "ExecutionEpochId")));
        data.Query.QuerySingleOrDefaultAsync<WorkflowParallelJoinRecord>(WorkflowSql.FindParallelJoinById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => data.Fork);
        data.Query.QuerySingleOrDefaultAsync<int>(WorkflowBranchSql.CountSelectedBranch, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        return data;
    }
    private static Guid Argument(object value, string name) => (Guid)((IReadOnlyDictionary<string, object?>)value)[name]!;
    internal sealed class FrameCase(WorkflowInstanceRecord instance, Guid parent, Guid leaf, WorkflowParallelJoinRecord fork, IQueryExecutor query)
    {
        public WorkflowInstanceRecord Instance { get; } = instance;
        public Guid Parent { get; } = parent;
        public Guid Leaf { get; } = leaf;
        public WorkflowParallelJoinRecord Fork { get; set; } = fork;
        public IQueryExecutor Query { get; } = query;
        public Dictionary<Guid, WorkflowExecutionEpochRecord> Epochs { get; } = [];
        public Dictionary<Guid, WorkflowRegionFrameRecord> Frames { get; } = [];
    }
}
