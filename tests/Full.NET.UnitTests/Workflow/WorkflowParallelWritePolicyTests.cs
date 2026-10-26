using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
namespace Full.NET.UnitTests.Workflow;

/// <summary>静态自动输出冲突在发布时失败；互斥默认分支及不同字段保留原语义。</summary>
[TestClass]
public sealed class WorkflowParallelWritePolicyTests
{
    [TestMethod]
    [DataRow("same",false)]
    [DataRow("different",true)]
    public void Parallel_known_write_sets_are_checked_before_publication(string target,bool valid)
    {
        var result=WorkflowDefinitionCompiler.Compile(Graph("gateway.parallel",target=="same"?"left":"right"));
        Assert.AreEqual(valid,result.IsSuccess);
        if(!valid)Assert.AreEqual(WorkflowErrorCodes.DefinitionParallelWriteConflict,result.ErrorCode);
    }
    [TestMethod]
    public void Inclusive_condition_branches_cannot_overlap_but_default_is_mutually_exclusive()
    {
        Assert.IsTrue(WorkflowDefinitionCompiler.Compile(Graph("gateway.inclusive","right")).IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionParallelWriteConflict,WorkflowDefinitionCompiler.Compile(Graph("gateway.inclusive","left")).ErrorCode);
    }
    [TestMethod]
    public void Connector_result_mapping_participates_in_the_same_write_set()
    {
        var graph=Graph("gateway.parallel","right");
        var nodes=graph.Nodes.Select(node=>node.NodeKey=="right"?Node("right","integration.trigger",new{nextNodeKeys=new[]{"join"},boundConnectorHash=new string('a',64),fieldPolicies=new{left="editable"},
            connectorPolicy=new{connectorKey="credit.check",connectorVersion=1,inputFields=new{},outputFields=new{left="score"}}}):node).ToArray();
        Assert.AreEqual(WorkflowErrorCodes.DefinitionParallelWriteConflict,WorkflowDefinitionCompiler.Compile(graph with{Nodes=nodes}).ErrorCode);
    }
    [TestMethod]
    [DataRow("left", false)]
    [DataRow("right", true)]
    public void Final_activity_callback_has_a_known_static_write_set(string target, bool expected)
    {
        var graph = Graph("gateway.parallel", "right");
        var nodes = graph.Nodes.Select(node => node.NodeKey == "right" ? Node("right", "human.approval", new {
            nextNodeKeys = new[] { "join" }, resultCallbackFieldKey = target, fieldPolicies = new Dictionary<string, string> { [target] = "editable" }
        }) : node).ToArray();
        var result = WorkflowDefinitionCompiler.Compile(graph with { Nodes = nodes });
        Assert.AreEqual(expected, result.IsSuccess);
        if (!expected) Assert.AreEqual(WorkflowErrorCodes.DefinitionParallelWriteConflict, result.ErrorCode);
    }

    private static WorkflowDefinitionDraft Graph(string kind,string rightField)
    {
        var branches=new[]{new{branchKey="left",nextNodeKey="left",condition=new{fieldKey="flag",@operator="equals",value=true}},new{branchKey="right",nextNodeKey="right",condition=new{fieldKey="flag",@operator="equals",value=false}}};
        object fork=kind=="gateway.parallel"?new{gatewayRoleKey="fork",joinNodeKey="join",branches=branches.Select(branch=>new{branch.branchKey,branch.nextNodeKey})}:
            new{gatewayRoleKey="fork",joinNodeKey="join",branches,defaultNextNodeKey="default"};
        var nodes=new List<WorkflowNodeDraft>{Node("start","start",new{nextNodeKeys=new[]{"fork"}}),Node("fork",kind,fork),Mutation("left","left"),Mutation("right",rightField),
            Node("join",kind,new{gatewayRoleKey="join",forkNodeKey="fork",nextNodeKeys=new[]{"end"}}),Node("end","end",new{})};
        if(kind=="gateway.inclusive")nodes.Add(Mutation("default","left"));
        return new(1,nodes,"automation");
    }
    private static WorkflowNodeDraft Mutation(string key,string target)=>Node(key,"data.modify",new{nextNodeKeys=new[]{"join"},fieldPolicies=new Dictionary<string,string>{{target,"editable"}},mutationPolicy=new{assignments=new[]{new{fieldKey=target,value="value"}}}});
    private static WorkflowNodeDraft Node(string key,string type,object config)=>new(key,type,1,JsonSerializer.SerializeToElement(config));
}
