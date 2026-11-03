using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Approvals;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Full.NET.Agents.Workflows;
using Full.NET.Modules.Ai.Features.CompletePersonalSchedules;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>原操作与闭合回执不可变、静态无模型图和独立批准门禁。</summary>
[TestClass]
public sealed class PersonalScheduleCompletionTests
{
    private static PersonalScheduleCompletionRequest Request() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 3,
        Guid.CreateVersion7(), new(Guid.CreateVersion7(), null, Guid.CreateVersion7(), "stamp", "host", "host"), DateTimeOffset.UtcNow.AddMinutes(5));

    [TestMethod]
    [DataRow(true,true)][DataRow(false,true)][DataRow(true,false)][DataRow(false,false)]
    public void Tenant_approval_scope_only_opens_the_closed_schedule_target(bool host,bool schedule)
    {
        var tenant=Substitute.For<ICurrentTenant>();tenant.Id.Returns(host?null:(Guid?)Guid.CreateVersion7());tenant.IsHost.Returns(host);tenant.IsAvailable.Returns(true);
        Assert.AreEqual(host||schedule,AiAgentApprovalService.SupportsScope(schedule?PersonalScheduleCompletionKeys.Tool:"ai.chat.rename_session",tenant));
    }

    [TestMethod]
    [DataRow("run")][DataRow("operation")][DataRow("schedule")][DataRow("version")][DataRow("approver")]
    [DataRow("user")][DataRow("tenant")][DataRow("session")][DataRow("stamp")][DataRow("actor_scope")]
    [DataRow("effective_scope")][DataRow("session_kind")][DataRow("deadline")]
    public void Original_operation_digest_binds_all_authority_and_parameters(string field)
    {
        var original = Request(); var changed = field switch
        {
            "run" => original with {RunId = Guid.CreateVersion7()}, "operation" => original with {OperationId = Guid.CreateVersion7()},
            "schedule" => original with {ScheduleId = Guid.CreateVersion7()}, "version" => original with {ExpectedVersion = 4},
            "approver" => original with {ApproverId = Guid.CreateVersion7()}, "deadline" => original with {ExpiresAtUtc = original.ExpiresAtUtc.AddSeconds(1)},
            _ => original with { Original = field switch
            {
                "user" => original.Original with {UserId = Guid.CreateVersion7()}, "tenant" => original.Original with {TenantId = Guid.CreateVersion7()},
                "session" => original.Original with {SessionId = Guid.CreateVersion7()}, "stamp" => original.Original with {SecurityStamp = "changed"},
                "actor_scope" => original.Original with {ActorScope = "changed"}, "effective_scope" => original.Original with {EffectiveScope = "changed"},
                "session_kind" => original.Original with {SessionKind = "unknown"}, _ => throw new AssertFailedException()
            }}
        };
        Assert.AreEqual(original.Digest(), (original with {}).Digest()); Assert.AreNotEqual(original.Digest(), changed.Digest());
        var receipt = new PersonalScheduleCompletionReceipt(original.OperationId, original.Digest(), "completed", null, 4, DateTimeOffset.UtcNow);
        Assert.IsTrue(receipt.IsFor(original)); Assert.IsFalse(receipt.IsFor(changed));
    }

    [TestMethod]
    [DataRow("operation")][DataRow("digest")][DataRow("outcome")][DataRow("version")][DataRow("time")][DataRow("reason")]
    public void Unknown_or_incomplete_receipt_never_proves_business_completion(string field)
    {
        var request = Request(); var receipt = new PersonalScheduleCompletionReceipt(request.OperationId, request.Digest(), "completed", null, 4, DateTimeOffset.UtcNow);
        receipt = field switch {"operation" => receipt with {OperationId = Guid.CreateVersion7()}, "digest" => receipt with {RequestDigest = new('0',64)},
            "outcome" => receipt with {Outcome = "accepted"}, "version" => receipt with {ActualVersion = 3}, "time" => receipt with {CompletedAtUtc = null},
            _ => receipt with {Reason = "target_changed"}};
        Assert.IsFalse(receipt.IsFor(request));
        Assert.IsTrue(new PersonalScheduleCompletionReceipt(request.OperationId, request.Digest(), "rejected", "target_changed", null, null).IsFor(request));
    }

    [TestMethod]
    [DataRow("null")][DataRow("{}")][DataRow("{\"scheduleId\":0,\"expectedVersion\":1}")]
    [DataRow("{\"scheduleId\":\"00000000-0000-0000-0000-000000000000\",\"expectedVersion\":1}")]
    public void Invalid_static_arguments_are_rejected_without_throwing(string json)
    { using var value = JsonDocument.Parse(json); Assert.IsNull(ScheduleCompletionArguments.Parse(value.RootElement)); }

    [TestMethod]
    [DataRow(true)][DataRow(false)]
    public void Schedule_approval_requires_independence_and_real_original_node(bool self)
    {
        var request = Request(); var operation = AgentWorkflowNodeReceipt.ResolveOperationId(request.RunId, "complete_schedule");
        using var args = JsonDocument.Parse($$"""{"scheduleId":"{{request.ScheduleId}}","expectedVersion":3}""");
        var actor = new ToolActor(request.Original.UserId, null, request.Original.SessionId);
        var record = new AiAgentApprovalRecord {Id=Guid.CreateVersion7(), ScopeKey="host", RunId=request.RunId,OperationId=operation,SessionId=actor.SessionId,
            RequestedBy=actor.UserId,ApproverId=self?actor.UserId:request.ApproverId,ToolName=PersonalScheduleCompletionKeys.Tool,ToolVersion=1,Version=1,
            ArgumentsHash=AgentApprovalGate.ComputeArgumentsHash(args.RootElement),PolicyVersion=AgentApprovalGate.CurrentPolicyVersion,DecisionKey="approved",ExpiresAtUtc=request.ExpiresAtUtc};
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
        var invocation = new ToolInvocation(operation,request.RunId,PersonalScheduleCompletionKeys.Tool,1,args.RootElement);
        Assert.AreEqual(!self,AiAgentApprovalConsumption.Matches(record,invocation,actor,tenant,DateTimeOffset.UtcNow));
        Assert.IsFalse(AiAgentApprovalConsumption.Matches(record,invocation with {OperationId=Guid.CreateVersion7()},actor,tenant,DateTimeOffset.UtcNow));
    }

    [TestMethod]
    [DataRow("exact")][DataRow("invisible_version")][DataRow("display_version")][DataRow("bound_version")]
    public async Task Schedule_approval_only_consumes_the_exact_visible_and_bound_version(string variant)
    {
        var target=Guid.CreateVersion7();var user=Guid.CreateVersion7();
        using var args=JsonDocument.Parse($$"""{"scheduleId":"{{target}}","expectedVersion":7}""");
        var change=variant=="invisible_version"?"pending -> completed":variant=="display_version"
            ?"pending -> completed; expected version: 8":"pending -> completed; expected version: 7";
        var version=variant=="bound_version"?8:7;
        var record=new AiAgentApprovalRecord {PresentationJson=$$"""{"action":"Complete own personal schedule","target":"{{target:D}}","change":"{{change}}","scope":"Owned personal schedule; independent approval required","costCeiling":null,"currency":null,"targetVersion":{{version}}}"""};
        var queries=Substitute.For<Full.NET.Data.Abstractions.IQueryExecutor>();var tenant=Substitute.For<ICurrentTenant>();tenant.IsHost.Returns(true);
        var invocation=new ToolInvocation(Guid.CreateVersion7(),Guid.CreateVersion7(),PersonalScheduleCompletionKeys.Tool,1,args.RootElement);
        Assert.AreEqual(variant=="exact",await AiAgentApprovalConsumption.TargetMatchesAsync(queries,record,invocation,new(user,null,Guid.CreateVersion7()),tenant,default));
        Assert.AreEqual(0,queries.ReceivedCalls().Count(),"本地批准快照校验不得跨模块读取业务表。");
    }

    [TestMethod]
    public async Task Static_schedule_graph_preserves_original_operation_across_independent_approval_without_model_calls()
    {
        var run=Guid.CreateVersion7(); var target=Guid.CreateVersion7(); var state=AgentWorkflowState.Create(Guid.CreateVersion7());
        state.TargetResourceId=target;state.TargetResourceVersion=7;state.InputTokens=0;state.OutputTokens=0;
        var executor=Substitute.For<IAgentToolExecutor>(); var model=Substitute.For<IAgentModelRunner>(); var client=Substitute.For<IChatClient>();
        var calls=new List<ToolInvocation>(); var approved=false;
        executor.ExecuteAsync(Arg.Any<ToolInvocation>(),Arg.Any<CancellationToken>()).Returns(info=>
        {
            var invocation=info.Arg<ToolInvocation>() ?? throw new AssertFailedException("缺少原工具调用。");calls.Add(invocation);
            Assert.AreEqual(target,invocation.Arguments.GetProperty("scheduleId").GetGuid());Assert.AreEqual(7,invocation.Arguments.GetProperty("expectedVersion").GetInt32());
            return ValueTask.FromResult(approved?new ToolExecutionResult("succeeded",JsonSerializer.SerializeToElement(new {state="accepted"}),null)
                :new ToolExecutionResult("denied",null,"ai.tool.approval_required"));
        });
        var registry=new AgentToolRegistry([new(PersonalScheduleCompletionKeys.Tool,1,PersonalScheduleCompletionKeys.Permission,"write",true,Substitute.For<IAgentToolHandler>())]);
        var request=new AgentWorkflowRunRequest(run,AgentWorkflowRegistry.ScheduleCompletionWorkflowKey,1,state,client,model,executor,registry,
            key=>AgentWorkflowNodeReceipt.ResolveOperationId(run,key));
        var waiting=await new AgentWorkflowRunner().RunAsync(request); Assert.AreEqual(AgentWorkflowRunStatus.AwaitingApproval,waiting.Status);
        approved=true;var accepted=await new AgentWorkflowRunner().RunAsync(request);Assert.AreEqual(AgentWorkflowRunStatus.Completed,accepted.Status);
        Assert.AreEqual(2,calls.Count);Assert.AreEqual(calls[0].OperationId,calls[1].OperationId);Assert.AreEqual(run,calls[1].RunId);
        Assert.AreEqual(0,model.ReceivedCalls().Count());Assert.AreEqual(0,client.ReceivedCalls().Count());Assert.AreEqual(0L,state.InputTokens);
        Assert.AreEqual("{\"state\":\"accepted\"}",state.Outputs["complete_schedule"]);
    }
}
