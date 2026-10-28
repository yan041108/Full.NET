using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>显式因果游标按路径隔离，存储失败不能前移，更不能从历史同名活动补猜来源。</summary>
[TestClass]
public sealed class WorkflowActivationTraceTests
{
    [TestMethod]
    public async Task Return_receipt_is_cleared_after_actual_steps_or_joins_and_not_inherited_by_forks()
    {
        var source = Guid.CreateVersion7(); var proof = new WorkflowReturnContinuationProof(Guid.CreateVersion7(), Guid.CreateVersion7(), "join", new string('a', 64), 2);
        var trace = new WorkflowActivationTrace(source, kindKey: "return", returnProof: proof);
        Assert.AreSame(proof, trace.ReturnProof); Assert.IsNull(trace.Fork(Guid.CreateVersion7(), "left").ReturnProof);
        var (command, ids) = Storage();
        await trace.RecordAsync(command, ids, Guid.CreateVersion7(), "host", Guid.CreateVersion7(), DateTimeOffset.UtcNow, default);
        Assert.IsNull(trace.ReturnProof);
        trace = new(source, kindKey: "return", returnProof: proof); trace.Join([source], null, null); Assert.IsNull(trace.ReturnProof);
    }

    [TestMethod]
    [DataRow("kind")]
    [DataRow("round")]
    [DataRow("work")]
    [DataRow("node")]
    [DataRow("hash")]
    [DataRow("revision")]
    public void Malformed_return_proof_never_becomes_a_causal_cursor(string defect)
    {
        var proof = new WorkflowReturnContinuationProof(Guid.CreateVersion7(), Guid.CreateVersion7(), "join", new string('a', 64), 2);
        proof = defect switch { "round" => proof with { RoundId = Guid.Empty }, "work" => proof with { WorkId = Guid.Empty },
            "node" => proof with { NodeKey = "" }, "hash" => proof with { InputHash = "bad" }, "revision" => proof with { InputFormRevision = 0 }, _ => proof };
        Assert.ThrowsExactly<InvalidOperationException>(() => new WorkflowActivationTrace(Guid.CreateVersion7(), kindKey: defect == "kind" ? "sequence" : "return", returnProof: proof));
    }

    [TestMethod]
    public async Task Route_receipt_is_cleared_after_a_real_step_or_join_and_is_not_inherited_by_a_fork()
    {
        var source = Guid.CreateVersion7(); var proof = new WorkflowRouteContinuationProof(source, Guid.CreateVersion7(), "join", new string('a', 64));
        var trace = new WorkflowActivationTrace(source, kindKey: "region", routeProof: proof);
        Assert.AreSame(proof, trace.RouteProof); Assert.IsNull(trace.Fork(Guid.CreateVersion7(), "left").RouteProof);
        var (command, ids) = Storage();
        await trace.RecordAsync(command, ids, Guid.CreateVersion7(), "host", Guid.CreateVersion7(), DateTimeOffset.UtcNow, default);
        Assert.IsNull(trace.RouteProof);
        trace = new(source, kindKey: "region", routeProof: proof); trace.Join(new Guid?[] { source }, null, null); Assert.IsNull(trace.RouteProof);
    }

    [TestMethod]
    public void Route_proof_cannot_claim_a_different_parent_or_cause_kind()
    {
        var source = Guid.CreateVersion7(); var proof = new WorkflowRouteContinuationProof(source, Guid.CreateVersion7(), "join", new string('a', 64));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WorkflowActivationTrace(source, routeProof: proof));
        Assert.ThrowsExactly<InvalidOperationException>(() => new WorkflowActivationTrace(Guid.CreateVersion7(), kindKey: "region", routeProof: proof));
    }

    [TestMethod]
    [DataRow("fork")]
    [DataRow("return")]
    [DataRow("sequence")]
    public async Task Durable_resume_preserves_the_explicit_cause_kind_without_guessing_from_node_names(string kind)
    {
        var parent=Guid.CreateVersion7();var trace=new WorkflowActivationTrace(parent,kindKey:kind);var (command,ids)=Storage();
        await trace.RecordAsync(command,ids,Guid.CreateVersion7(),"host",Guid.CreateVersion7(),DateTimeOffset.UtcNow,default);
        Assert.AreEqual(kind,Values(command).Single()["KindKey"]);
        Assert.ThrowsExactly<InvalidOperationException>(()=>new WorkflowActivationTrace(parent,kindKey:"unknown"));
    }

    [TestMethod]
    public async Task Forks_keep_the_same_real_fork_parent_but_advance_independently()
    {
        var parent=Guid.CreateVersion7();var join=Guid.CreateVersion7();var trace=new WorkflowActivationTrace(parent);
        var left=trace.Fork(join,"left");var right=trace.Fork(join,"right");
        var (command,ids)=Storage();var next=Guid.CreateVersion7();
        await left.RecordAsync(command,ids,Guid.CreateVersion7(),"host",next,DateTimeOffset.UtcNow,default);
        Assert.AreEqual(parent,trace.LastStepId);Assert.AreEqual(parent,right.LastStepId);Assert.AreEqual(next,left.LastStepId);
        Assert.AreEqual(join,left.ParallelJoinId);Assert.AreEqual("left",left.BranchKey);
        var values=Values(command).Single();Assert.AreEqual(parent,values["ParentStepId"]);Assert.AreEqual("fork",values["KindKey"]);
    }

    [TestMethod]
    public async Task Complete_join_records_every_actual_arrival_parent_before_advancing_to_the_new_step()
    {
        var first=Guid.CreateVersion7();var second=Guid.CreateVersion7();var next=Guid.CreateVersion7();var trace=new WorkflowActivationTrace(first);
        trace.Join(new Guid?[]{first,second},null,null);var (command,ids)=Storage();
        await trace.RecordAsync(command,ids,Guid.CreateVersion7(),"host",next,DateTimeOffset.UtcNow,default);
        CollectionAssert.AreEquivalent(new[]{first,second},Values(command).Select(values=>(Guid)values["ParentStepId"]!).ToArray());
        Assert.IsTrue(Values(command).All(values=>Equals(values["StepId"],next)&&Equals(values["KindKey"],"join")));
        Assert.AreEqual(next,trace.LastStepId);Assert.AreEqual("sequence",trace.KindKey);
    }

    [TestMethod]
    public async Task Empty_branches_retain_independent_arrival_facts_but_share_one_actual_fork_cause_edge()
    {
        var source=Guid.CreateVersion7();var trace=new WorkflowActivationTrace(source);trace.Join(new Guid?[]{source,source},null,null);
        var (command,ids)=Storage();await trace.RecordAsync(command,ids,Guid.CreateVersion7(),"host",Guid.CreateVersion7(),DateTimeOffset.UtcNow,default);
        Assert.AreEqual(1,Values(command).Length);
    }

    [TestMethod]
    public async Task A_storage_conflict_keeps_the_original_cursor_and_requires_transaction_rollback()
    {
        var source=Guid.CreateVersion7();var trace=new WorkflowActivationTrace(source);var (command,ids)=Storage();
        command.ExecuteAsync(WorkflowCausalitySql.InsertEdge,Arg.Any<object?>(),Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>trace.RecordAsync(command,ids,Guid.CreateVersion7(),"host",Guid.CreateVersion7(),DateTimeOffset.UtcNow,default));
        Assert.AreEqual(source,trace.LastStepId);
    }

    [TestMethod]
    public void Missing_arrival_sources_and_invalid_frames_are_rejected_without_inventing_a_parent()
    {
        Assert.ThrowsExactly<InvalidOperationException>(()=>new WorkflowActivationTrace(Guid.Empty));
        Assert.ThrowsExactly<InvalidOperationException>(()=>new WorkflowActivationTrace(Guid.CreateVersion7(),Guid.CreateVersion7(),null));
        var source=Guid.CreateVersion7();var trace=new WorkflowActivationTrace(source);
        Assert.ThrowsExactly<InvalidOperationException>(()=>trace.Join(new Guid?[]{null},null,null));
        Assert.AreEqual(source,trace.LastStepId);
    }

    private static (ICommandExecutor,IIdGenerator) Storage()
    {
        var command=Substitute.For<ICommandExecutor>();command.ExecuteAsync(Arg.Any<SqlStatement>(),Arg.Any<object?>(),Arg.Any<CancellationToken>()).Returns(1);
        var ids=Substitute.For<IIdGenerator>();ids.NewId().Returns(_=>Guid.CreateVersion7());return(command,ids);
    }
    private static IReadOnlyDictionary<string,object?>[] Values(ICommandExecutor command)=>command.ReceivedCalls()
        .Where(call=>Equals(call.GetArguments()[0],WorkflowCausalitySql.InsertEdge)).Select(call=>(IReadOnlyDictionary<string,object?>)call.GetArguments()[1]!).ToArray();
}
