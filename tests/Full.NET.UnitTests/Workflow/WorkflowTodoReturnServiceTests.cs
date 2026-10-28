using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证退回只命中合法历史人工节点，并原子保存完整人工活动的耐久重新激活输入。</summary>
[TestClass]
public sealed class WorkflowTodoReturnServiceTests
{
    [TestMethod]
    public async Task A_missing_or_corrupt_target_member_snapshot_cannot_fall_back_to_current_role_resolution()
    {
        var target = new WorkflowTodoReturnTargetRecord(Guid.CreateVersion7(), "manager", Guid.CreateVersion7(), 1, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));
        var f = CreateFixture(target);
        f.Query.QuerySingleOrDefaultAsync<WorkflowAssigneeSnapshotRecord>(WorkflowReturnRoundSql.FindCompletedSnapshot,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowAssigneeSnapshotRecord("{}", new string('a', 64)));
        var result = await f.Service.ReturnAsync(f.TodoId, f.ActorId, new(target.StepId, 3, JsonSerializer.SerializeToElement(new { }), "保留原成员", "bad-snapshot", 2));
        Assert.AreEqual(WorkflowErrorCodes.TodoReturnTargetInvalid, result.Error?.Code);
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.ReturnStepWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowReturnRoundSql.InsertWork, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("missingCause")]
    [DataRow("unbound")]
    public async Task A_completed_history_record_without_actual_ancestry_and_effective_binding_cannot_be_returned_to(string fault)
    {
        var target=new WorkflowTodoReturnTargetRecord(Guid.CreateVersion7(),"manager",Guid.CreateVersion7(),1,DateTimeOffset.UtcNow.AddHours(-2),DateTimeOffset.UtcNow.AddHours(-1));
        var f=CreateFixture(target);
        if(fault=="missingCause")f.Query.QueryAsync<WorkflowStepCauseRecord>(WorkflowCausalitySql.ListEdges,Arg.Any<object?>(),Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkflowStepCauseRecord>());
        else f.Query.QueryAsync<WorkflowActivationBindingRecord>(WorkflowActivationBindingSql.List,Arg.Any<object?>(),Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkflowActivationBindingRecord>());
        var result=await f.Service.ReturnAsync(f.TodoId,f.ActorId,new(target.StepId,3,JsonSerializer.SerializeToElement(new { }),"实际链校验","unproven-return",2));
        Assert.AreEqual(WorkflowErrorCodes.TodoReturnTargetInvalid,result.Error?.Code);
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.ReturnStepWithRevision,Arg.Any<object?>(),Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowActivitySql.InsertWork,Arg.Any<object?>(),Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Supplemental_signer_cannot_query_or_submit_route_changes()
    {
        var f = CreateFixture(new(Guid.CreateVersion7(), "manager", null, 1, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1)));
        f.Query.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(WorkflowSql.FindCountersignItemByTodoId,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowCountersignItemContextRecord(
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, f.ActorId, f.TodoId, "active", "before", Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "active"));
        var result = await f.Service.ReturnAsync(f.TodoId, f.ActorId, new(Guid.CreateVersion7(), 3, JsonSerializer.SerializeToElement(new { }), "不能代原席退回", "supplemental-route", 2));
        Assert.AreEqual(WorkflowErrorCodes.TodoActionForbidden, result.Error?.Code);
        var targets = await f.Service.ListReturnTargetsAsync(f.TodoId, f.ActorId, 1, 20);
        Assert.AreEqual(WorkflowErrorCodes.TodoActionForbidden, targets.Error?.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }
    [TestMethod]
    public async Task Return_cannot_replay_a_different_todo_even_with_identical_request_fields()
    {
        var f = CreateFixture(returnTarget: null);
        var target = Guid.CreateVersion7();
        var request = new ReturnWorkflowTodoRequest(target, 3, JsonSerializer.SerializeToElement(new { }),
            "资料不完整", "other-todo", 2);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"return\n{target:D}\n3\n2\n{{}}\n资料不完整\nsignature:")));
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowActionReceiptRecord(
                "return", f.ActorId, 8, request.IdempotencyKey, hash, Guid.CreateVersion7(), "active", Guid.CreateVersion7()));
        var result = await f.Service.ReturnAsync(f.TodoId, f.ActorId, request);
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    /// <summary>合法目标查询端点与退回动作必须共同绑定独立退回权限。</summary>
    [TestMethod]
    public async Task Return_endpoints_require_todos_return_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/return-targets",
            WorkflowPermissions.TodosReturn);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/return",
            WorkflowPermissions.TodosReturn);
    }

    /// <summary>提交自动节点或其他实例节点时，服务端再次校验必须失败且不产生写入。</summary>
    [TestMethod]
    public async Task Return_missing_legal_target_fails_before_writes()
    {
        var fixture = CreateFixture(returnTarget: null);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            fixture.ActorId,
            new ReturnWorkflowTodoRequest(
                Guid.CreateVersion7(), 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", "return-001", ExpectedFormRevision: 2));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.TodoReturnTargetInvalid, result.Error!.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(fixture.Command));
    }

    /// <summary>成功退回必须关闭来源工作、失效目标后的旧链、推进实例并创建目标审批的新工作。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Return_to_completed_human_step_enqueues_a_new_activation(bool grouped)
    {
        var target = new WorkflowTodoReturnTargetRecord(
            Guid.CreateVersion7(), "manager", Guid.CreateVersion7(),
            4, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));
        var fixture = CreateFixture(target, grouped: grouped);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            fixture.ActorId,
            new ReturnWorkflowTodoRequest(
                target.StepId, 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", "return-001", ExpectedFormRevision: 2));

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received(1).ExecuteAsync(WorkflowReturnClosureSql.CancelGroups,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual("active", result.Value!.StatusKey);
        Assert.AreEqual(8, result.Value.Revision);
        Assert.IsNull(result.Value.ActiveTodoId);
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.ReturnStepWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received().ExecuteAsync(
            WorkflowReturnClosureSql.InvalidateSteps, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(WorkflowReturnRoundSql.InsertWork,
            Arg.Is<object?>(value => HasValue(value, "NodeKey", target.NodeKey) && HasValue(value, "InputFormRevision", 3L)), Arg.Any<CancellationToken>());
        await fixture.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertStep, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.InsertApprovalActionRecord, Arg.Is<object?>(value => HasValue(value, "ActionKey", "return") && HasValue(value, "ResultStatusKey", "active")),
            Arg.Any<CancellationToken>());
        Assert.AreEqual(0, fixture.Outbox.ReceivedCalls().Count());
    }

    /// <summary>相同操作者、幂等键和目标语义重放时返回首次结果，不受实例后续状态变化影响。</summary>
    [TestMethod]
    public async Task Return_same_idempotency_semantics_replays_without_writes()
    {
        var targetStepId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"return\n{targetStepId:D}\n3\n2\n{{}}\n资料不完整\nsignature:")));
        var originalTodoId = Guid.CreateVersion7();
        var receipt = new WorkflowActionReceiptRecord(
            "return", actorId, 8, "return-001", requestHash, originalTodoId, "active");
        var fixture = CreateFixture(
            returnTarget: null, receipt: receipt, instanceRevision: 12, actorId: actorId);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            actorId,
            new ReturnWorkflowTodoRequest(
                targetStepId, 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", "return-001", ExpectedFormRevision: 2));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(8, result.Value!.Revision);
        Assert.AreEqual(originalTodoId, result.Value.ActiveTodoId);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(fixture.Command));
    }

    [TestMethod]
    public async Task Durable_return_replays_the_original_null_todo_receipt()
    {
        var target = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"return\n{target:D}\n3\n2\n{{}}\n资料不完整\nsignature:")));
        var fixture = CreateFixture(null, receipt: new("return", actor, 8, "durable-return", hash, null, "active"), instanceRevision: 12, actorId: actor);
        var result = await fixture.Service.ReturnAsync(fixture.TodoId, actor, new(target, 3, JsonSerializer.SerializeToElement(new { }), "资料不完整", "durable-return", 2));
        Assert.IsTrue(result.IsSuccess); Assert.IsNull(result.Value!.ActiveTodoId); Assert.AreEqual(8, result.Value.Revision);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(fixture.Command));
    }

    /// <summary>两个同语义请求竞争实例 CAS 时，失败方必须读取胜方回执并返回同一个首次结果。</summary>
    [TestMethod]
    public async Task Return_concurrent_same_idempotency_key_replays_winner_after_cas_loss()
    {
        var target = new WorkflowTodoReturnTargetRecord(
            Guid.CreateVersion7(), "manager", Guid.CreateVersion7(), 4,
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1));
        var actorId = Guid.CreateVersion7();
        var resultTodoId = Guid.CreateVersion7();
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"return\n{target.StepId:D}\n3\n2\n{{}}\n资料不完整\nsignature:")));
        var receipt = new WorkflowActionReceiptRecord(
            "return", actorId, 8, "return-001", requestHash, resultTodoId, "active");
        var fixture = CreateFixture(target, concurrentReceipt: receipt, actorId: actorId);
        fixture.Command.ExecuteAsync(
                WorkflowSql.UpdateFormSubmissionWithRevision,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            actorId,
            new ReturnWorkflowTodoRequest(
                target.StepId, 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", "return-001", ExpectedFormRevision: 2));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(8, result.Value!.Revision);
        Assert.AreEqual(resultTodoId, result.Value.ActiveTodoId);
        Assert.AreEqual(1, WorkflowTodoManagementTestDependencies.CountBusinessCommands(fixture.Command));
        Assert.AreEqual(0, fixture.Outbox.ReceivedCalls().Count());
    }

    /// <summary>JSON 显式传入空幂等键时必须返回稳定校验错误，不能抛出空引用异常。</summary>
    [TestMethod]
    public async Task Return_null_idempotency_key_returns_validation_error()
    {
        var fixture = CreateFixture(returnTarget: null);

        var result = await fixture.Service.ReturnAsync(
            fixture.TodoId,
            fixture.ActorId,
            new ReturnWorkflowTodoRequest(
                Guid.CreateVersion7(), 3, JsonSerializer.SerializeToElement(new { }),
                "资料不完整", null!, ExpectedFormRevision: 2));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.SchemaInvalid, result.Error!.Code);
        Assert.AreEqual(0, fixture.Query.ReceivedCalls().Count());
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(fixture.Command));
    }

    /// <summary>构造退回服务需要的可信实例、活动待办、不可变资产与可选目标。</summary>
    /// <param name="returnTarget">服务端复核得到的合法目标；为空表示非法。</param>
    /// <param name="receipt">可选的已提交动作回执，用于幂等重放。</param>
    /// <param name="instanceRevision">当前实例修订号。</param>
    /// <param name="actorId">可选的固定操作人标识。</param>
    /// <returns>包含服务和可观察替身的测试夹具。</returns>
    private static ReturnFixture CreateFixture(
        WorkflowTodoReturnTargetRecord? returnTarget,
        WorkflowActionReceiptRecord? receipt = null,
        WorkflowActionReceiptRecord? concurrentReceipt = null,
        long instanceRevision = 7,
        Guid? actorId = null,
        bool grouped = false)
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        if (receipt is not null) receipt = receipt with { TodoId = todoId };
        if (concurrentReceipt is not null) concurrentReceipt = concurrentReceipt with { TodoId = todoId };
        var stepId = Guid.CreateVersion7();
        var resolvedActorId = actorId ?? Guid.CreateVersion7();
        var definitionVersionId = Guid.CreateVersion7();
        var formVersionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(
                WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRuntimeRecord(
                todoId, instanceId, stepId, resolvedActorId, "active", now, null, null, 3, "finance", 1,
                grouped ? "group" : null, grouped ? 2 : null, grouped ? 4 : null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", definitionVersionId, formVersionId,
                "purchase", "PO-001", null, "active", instanceRevision, resolvedActorId, now,
                null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(
                receipt,
                concurrentReceipt ?? receipt);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRecord>(
                WorkflowSql.FindActiveTodoByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoRecord(
                Guid.CreateVersion7(), instanceId, Guid.CreateVersion7(), resolvedActorId,
                "active", now, null, null, 1));
        query.QuerySingleOrDefaultAsync<WorkflowTodoReturnTargetRecord>(
                WorkflowSql.FindTodoReturnTarget, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(returnTarget);
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(
                WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(
                definitionVersionId, formVersionId,
                "{\"schemaVersion\":1,\"nodes\":[{\"nodeKey\":\"start\",\"nodeTypeKey\":\"start\",\"nodeSchemaVersion\":1,\"config\":{\"nextNodeKeys\":[\"manager\"]}},{\"nodeKey\":\"manager\",\"nodeTypeKey\":\"human.approval\",\"nodeSchemaVersion\":1,\"config\":{\"nextNodeKeys\":[\"finance\"]}},{\"nodeKey\":\"finance\",\"nodeTypeKey\":\"human.approval\",\"nodeSchemaVersion\":1,\"config\":{\"nextNodeKeys\":[\"end\"]}},{\"nodeKey\":\"end\",\"nodeTypeKey\":\"end\",\"nodeSchemaVersion\":1,\"config\":{\"nextNodeKeys\":[]}}]}",
                "{\"schemaVersion\":1,\"adapterVersion\":1,\"sections\":[]}", null, "active"));
        if (grouped)
        {
            var definition = new WorkflowDefinitionDraft(1,
            [
                new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "manager" } })),
                new("manager", "human.approval", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "finance" } })),
                new("finance", "human.group_approval", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" },
                    groups = new[] { new { groupKey = "first", assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { Guid.CreateVersion7() } } } } },
                        new { groupKey = "second", assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { Guid.CreateVersion7() } } } } } },
                    groupApprovalPolicy = new { modeKey = "all" } })),
                new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))
            ]);
            query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new WorkflowRuntimeAssetRecord(definitionVersionId, formVersionId,
                    JsonSerializer.Serialize(definition, Full.NET.Modules.Workflow.Serialization.WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft),
                    """{"schemaVersion":1,"adapterVersion":1,"sections":[]}""", null, "active"));
        }
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(
                WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormSubmissionRecord(
                Guid.CreateVersion7(), instanceId, formVersionId, "{}", "none", 2, resolvedActorId, now));

        query.QuerySingleOrDefaultAsync<string>(WorkflowDesignerAssetSql.FindBindings,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns("""{"schemaVersion":1,"sections":[]}""");
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var outbox = Substitute.For<IOutboxWriter>();
        if (returnTarget is not null)
        {
            var snapshot = JsonSerializer.Serialize(new WorkflowAssigneeResolutionSnapshot(1, resolvedActorId,
                    [returnTarget.AssigneeUserId ?? resolvedActorId], []),
                Full.NET.Modules.Workflow.Serialization.WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
            query.QuerySingleOrDefaultAsync<WorkflowAssigneeSnapshotRecord>(WorkflowReturnRoundSql.FindCompletedSnapshot,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowAssigneeSnapshotRecord(snapshot,
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot)))));
        }
        query.QuerySingleOrDefaultAsync<WorkflowRegionRouteSourceRecord>(WorkflowReturnRoundSql.FindStep,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowRegionRouteSourceRecord(stepId, instanceId, instanceId,
                "finance", grouped ? "human.group_approval" : "human.approval", "returned", null, null, definitionVersionId));
        query.QuerySingleOrDefaultAsync<long>(WorkflowSql.FindNextStepExecutionSequence, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(6L);
        query.QuerySingleOrDefaultAsync<int>(WorkflowRegionSql.NextIteration, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        query.QueryAsync<WorkflowStepCauseRecord>(WorkflowCausalitySql.ListEdges,Arg.Any<object?>(),Arg.Any<CancellationToken>())
            .Returns(returnTarget is null ? new WorkflowStepCauseRecord[]{ new(Guid.CreateVersion7(),instanceId,"host",stepId,instanceId,"sequence",now) }
                : new WorkflowStepCauseRecord[]{new(Guid.CreateVersion7(),instanceId,"host",stepId,returnTarget.StepId,"sequence",now),
                    new(Guid.CreateVersion7(),instanceId,"host",returnTarget.StepId,instanceId,"sequence",now)});
        query.QueryAsync<WorkflowActivationBindingRecord>(WorkflowActivationBindingSql.List,Arg.Any<object?>(),Arg.Any<CancellationToken>())
            .Returns(returnTarget is null ? Array.Empty<WorkflowActivationBindingRecord>() : new[]{new WorkflowActivationBindingRecord(Guid.CreateVersion7(),instanceId,
                "host",instanceId,returnTarget.NodeKey,returnTarget.StepId,instanceId,now)});
        query.QueryAsync<WorkflowReturnStepFact>(WorkflowReturnScopeSql.ListStepsSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(returnTarget is null ? new[] { new WorkflowReturnStepFact(instanceId, instanceId, instanceId, "start", "start", "completed", null, null),
                new WorkflowReturnStepFact(stepId, instanceId, instanceId, "finance", grouped ? "human.group_approval" : "human.approval", "active", null, null) }
                : new[] { new WorkflowReturnStepFact(instanceId, instanceId, instanceId, "start", "start", "completed", null, null),
                    new WorkflowReturnStepFact(returnTarget.StepId, instanceId, instanceId, returnTarget.NodeKey, "human.approval", "completed", null, null),
                    new WorkflowReturnStepFact(stepId, instanceId, instanceId, "finance", grouped ? "human.group_approval" : "human.approval", "active", null, null) });
        var ccWriter = new WorkflowCcTransitionWriter(query, command, ids);
        var notificationPublisher = new WorkflowNotificationOutboxPublisher(outbox);
        var service = new WorkflowTodoManagementService(
            query, command, new TrackingTransaction(), tenant, clock, ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            new WorkflowHumanActivityPreparation(WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(), query, WorkflowTodoManagementTestDependencies.CreateStarterSelectionPreparation()),
            notificationPublisher,
            WorkflowTodoManagementTestDependencies.CreateCountersignService(query, command, tenant),
            WorkflowTodoManagementTestDependencies.CreateTransitionExecutor(
                query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateParallelJoinCoordinator(query, command, ids),
            WorkflowTodoManagementTestDependencies.CreateAttachmentCoordinator(),
            new WorkflowBranchStateCoordinator(query, command),
            WorkflowTodoManagementTestDependencies.CreateSignatureCoordinator(query, command),
            WorkflowTodoManagementTestDependencies.CreateStepResolution(query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateTaskDecisions(query, command, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateResultCallbacks(query, command, ids));
        return new ReturnFixture(service, query, command, outbox, todoId, resolvedActorId);
    }

    /// <summary>判断匿名 SQL 参数对象是否携带指定名称和值。</summary>
    /// <param name="parameters">待检查参数对象。</param>
    /// <param name="name">属性名。</param>
    /// <param name="expected">期望值。</param>
    /// <returns>属性存在且值相等时返回 <see langword="true"/>。</returns>
    private static bool HasValue(object? parameters, string name, object expected) =>
        parameters is IReadOnlyDictionary<string, object?> values &&
        values.TryGetValue(name, out var actual) && actual?.Equals(expected) == true;

    /// <summary>断言指定待办端点只绑定退回精确权限。</summary>
    /// <param name="route">端点路由模板。</param>
    /// <param name="permission">期望权限码。</param>
    private static async Task AssertEndpointPermissionAsync(string route, string permission)
    {
        var builder = WebApplication.CreateBuilder();
        var module = new WorkflowModule();
        module.AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        await using var app = builder.Build();
        module.MapEndpoints(app);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route);
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        Assert.HasCount(1, authorization);
        Assert.AreEqual(FullNetPermissionPolicies.For(permission), authorization[0].Policy);
    }

    /// <summary>保存退回测试需要的服务与替身。</summary>
    /// <param name="Service">待测服务。</param>
    /// <param name="Query">可观察查询执行器。</param>
    /// <param name="Command">可观察命令执行器。</param>
    /// <param name="Outbox">可观察 Outbox 写入器。</param>
    /// <param name="TodoId">当前待办标识。</param>
    /// <param name="ActorId">当前办理人标识。</param>
    private sealed record ReturnFixture(
        WorkflowTodoManagementService Service,
        IQueryExecutor Query,
        ICommandExecutor Command,
        IOutboxWriter Outbox,
        Guid TodoId,
        Guid ActorId);

    /// <summary>直接执行事务回调，使测试可以观察同一事务内的所有命令。</summary>
    private sealed class TrackingTransaction : ICommandTransaction
    {
        /// <summary>执行调用方事务回调。</summary>
        /// <typeparam name="T">结果类型。</typeparam>
        /// <param name="action">事务内操作。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>事务回调结果。</returns>
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }
}
