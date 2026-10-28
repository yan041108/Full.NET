using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.UnitTests.Workflow;

/// <summary>实际祖先不含未选中历史；环、缺边、跨根和跨作用域不能被执行序号掩盖。</summary>
[TestClass]
public sealed class WorkflowCausalGraphTests
{
    [TestMethod]
    public void A_join_retains_both_selected_branch_ancestors_without_admitting_unrelated_history()
    {
        var root=Guid.CreateVersion7();var left=Guid.CreateVersion7();var right=Guid.CreateVersion7();var join=Guid.CreateVersion7();var unrelated=Guid.CreateVersion7();
        var edges=new[]{Edge(root,left,root),Edge(root,right,root),Edge(root,join,left,"join"),Edge(root,join,right,"join"),Edge(root,unrelated,root)};
        Assert.IsTrue(WorkflowCausalGraph.TryReadAncestors(root,"host",join,edges,out var ancestors));
        CollectionAssert.AreEquivalent(new[]{root,left,right},ancestors.ToArray());Assert.IsFalse(ancestors.Contains(unrelated));
    }

    [TestMethod]
    [DataRow("cycle")]
    [DataRow("missing")]
    [DataRow("scope")]
    [DataRow("root")]
    [DataRow("kind")]
    [DataRow("duplicate")]
    public void Unproven_paths_fail_without_exposing_a_partial_candidate_set(string fault)
    {
        var root=Guid.CreateVersion7();var prior=Guid.CreateVersion7();var current=Guid.CreateVersion7();var edges=new List<WorkflowStepCauseRecord>{Edge(root,current,prior),Edge(root,prior,root)};
        if(fault=="cycle")edges[1]=Edge(root,prior,current);
        if(fault=="missing")edges.RemoveAt(1);
        if(fault=="scope")edges[0]=edges[0] with { TenantScopeKey="tenant:"+Guid.CreateVersion7().ToString("N") };
        if(fault=="root")edges[0]=edges[0] with { InstanceId=Guid.CreateVersion7() };
        if(fault=="kind")edges[0]=edges[0] with { KindKey="guess" };
        if(fault=="duplicate")edges.Add(edges[0]);
        Assert.IsFalse(WorkflowCausalGraph.TryReadAncestors(root,"host",current,edges,out var ancestors));Assert.AreEqual(0,ancestors.Count);
    }

    [TestMethod]
    public void An_old_root_without_actual_cause_edges_is_not_reconstructed_from_its_step_order()
    {
        Assert.IsFalse(WorkflowCausalGraph.TryReadAncestors(Guid.CreateVersion7(),"host",Guid.CreateVersion7(),[],out _));
    }

    [TestMethod]
    public void Traversal_budget_counts_the_real_start_step_and_uses_no_recursive_call_stack()
    {
        var root=Guid.CreateVersion7();var cursor=root;var edges=new List<WorkflowStepCauseRecord>();
        for(var index=0;index<WorkflowActivityScheduler.InstanceActivityBudget;index++)
        {var next=Guid.CreateVersion7();edges.Add(Edge(root,next,cursor));cursor=next;}
        Assert.IsFalse(WorkflowCausalGraph.TryReadAncestors(root,"host",cursor,edges,out var ancestors));Assert.AreEqual(0,ancestors.Count);
        edges.RemoveAt(edges.Count-1);cursor=edges[^1].StepId;
        Assert.IsTrue(WorkflowCausalGraph.TryReadAncestors(root,"host",cursor,edges,out _));
    }

    private static WorkflowStepCauseRecord Edge(Guid root,Guid child,Guid parent,string kind="sequence")=>new(Guid.CreateVersion7(),root,"host",child,parent,kind,DateTimeOffset.UtcNow);
}
