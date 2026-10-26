using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>纯计划必须在消费者之前停下，受信续办只跳过首个等待，不提前求值后继。</summary>
[TestClass]
public sealed class WorkflowAsyncRuntimePlanTests
{
    [TestMethod]
    public void Async_dispatch_is_distinct_and_an_independent_activity_can_proceed_before_terminal_wait()
    {
        var plan=Plan([Producer("call","independent"),Node("independent","control.empty",new{nextNodeKeys=new[]{"end"}}),Node("end","end",new{})]);
        Assert.IsTrue(plan.TryResolveStart(Values,out var start)); Assert.IsTrue(((WorkflowConnectorWaitPlan)start.AutomaticWait!).IsAsync);
        Assert.IsTrue(plan.TryResolveContinuation("independent",Values,null,out var next)); Assert.AreEqual("independent",next.AutomaticNodes.Single().NodeKey);
        Assert.IsFalse(next.CompletesInstance); Assert.AreEqual("end",((WorkflowAsyncBarrierWaitPlan)next.AutomaticWait!).NodeKey);
    }
    [TestMethod]
    public void A_matching_acknowledgement_skips_only_that_consumer_wait()
    {
        var plan=Plan([Producer("first","second"),Producer("second","one"),Node("one","control.empty",new{nextNodeKeys=new[]{"two"}}) with{AwaitActivityNodeKeys=["first"]},
            Node("two","control.empty",new{nextNodeKeys=new[]{"end"}}) with{AwaitActivityNodeKeys=["second"]},Node("end","end",new{})]);
        Assert.IsTrue(plan.TryResolveContinuation("one",Values,null,out var first)); Assert.AreEqual("one",first.AutomaticWait!.NodeKey);
        Assert.IsFalse(plan.TryResolveContinuation("one",Values,null,out _,"two"));
        Assert.IsTrue(plan.TryResolveContinuation("one",Values,null,out var second,"one")); Assert.AreEqual("two",second.AutomaticWait!.NodeKey); Assert.IsFalse(second.CompletesInstance);
        Assert.IsTrue(plan.TryResolveContinuation("two",Values,null,out var end,"two")); Assert.IsTrue(end.CompletesInstance);
    }
    private static readonly IReadOnlyDictionary<string,JsonElement> Values=new Dictionary<string,JsonElement>{{"title",JsonSerializer.SerializeToElement("input")}};
    private static WorkflowRuntimePlan Plan(WorkflowNodeDraft[] nodes)
    {
        var draft=new WorkflowDefinitionDraft(1,[Node("start","start",new{nextNodeKeys=new[]{nodes[0].NodeKey}}),..nodes],"automation");
        Assert.IsTrue(WorkflowRuntimePlan.TryCreate(draft,out var plan)); return plan!;
    }
    private static WorkflowNodeDraft Producer(string key,string next)=>Node(key,"integration.async_trigger",new{nextNodeKeys=new[]{next},boundConnectorHash=new string('a',64),
        fieldPolicies=new{title="readOnly"},connectorPolicy=new{connectorKey="credit.check",connectorVersion=1,inputFields=new{subject="title"},outputFields=new{}}});
    private static WorkflowNodeDraft Node(string key,string type,object config)=>new(key,type,1,JsonSerializer.SerializeToElement(config));
}
