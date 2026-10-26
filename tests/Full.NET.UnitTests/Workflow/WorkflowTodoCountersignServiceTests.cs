using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证前加签、后加签、取消与幂等语义。</summary>
[TestClass]
public sealed class WorkflowTodoCountersignServiceTests
{
    [TestMethod]
    [DataRow("before")]
    [DataRow("after")]
    public async Task Supplemental_rejection_decides_only_the_original_member_in_any_approval(string direction)
    {
        var f = CreateFixture(null, []);
        var original = (await f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, null, default))!
            with { ApprovalModeKey = "any", RequiredApprovalCount = 1, ApprovalSlotCount = 2,
                StatusKey = direction == "before" ? "before_countersign" : "completed", ResultActionKey = direction == "after" ? "approve" : null };
        var instance = (await f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, null, default))!;
        var signer = Guid.CreateVersion7(); var supplemental = original with { Id = Guid.CreateVersion7(), AssigneeUserId = signer, StatusKey = "active", ResultActionKey = null };
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(original);
        f.Query.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(WorkflowSql.FindCountersignItemByTodoId, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowCountersignItemContextRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, signer, supplemental.Id,
                "active", direction, original.Id, instance.Id, original.StepId, "active"));
        f.Query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotByTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowApprovalSlotRecord(Guid.CreateVersion7(), 1));
        f.Query.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(WorkflowSql.FindApprovalTallyByStepSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowApprovalTallyRecord(0, 1, 1));
        var draft = new WorkflowDefinitionDraft(1, [
            new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "finance" } })),
            new("finance", "human.approval", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, approvalPolicy = new { modeKey = "any", approverUserIds = new[] { original.AssigneeUserId, Guid.CreateVersion7() } } })),
            new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))]);
        f.Query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, instance.FormVersionId!.Value,
                JsonSerializer.Serialize(draft, Full.NET.Modules.Workflow.Serialization.WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft),
                """{"schemaVersion":1,"adapterVersion":1,"sections":[]}""", null, "active"));
        var result = await f.Service.TryHandleActAsync(supplemental, instance, instance.FormVersionId.Value, "{}",
            new(3, JsonSerializer.SerializeToElement(new { }), null, "consultation-reject", 1), "reject", signer,
            new(null, "host", "host"), WorkflowPreparedHumanActivities.Empty, default);
        Assert.IsTrue(result!.IsSuccess); Assert.AreEqual("active", result.Value!.StatusKey);
        await f.Command.Received(1).ExecuteAsync(WorkflowSql.DecideApprovalSlotWithRevision,
            Arg.Is<object?>(parameters => HasValue(parameters, "AssigneeUserId", original.AssigneeUserId) && HasValue(parameters, "DecisionKey", "reject")), Arg.Any<CancellationToken>());
        await f.Command.Received(1).ExecuteAsync(WorkflowSql.CompleteTodoWithRevision,
            Arg.Is<object?>(parameters => HasValue(parameters, "AssigneeUserId", signer) && HasValue(parameters, "ResultActionKey", "reject")), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.CompleteStepWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertApprovalSlot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Before_chain_resume_captures_a_new_todo_input_and_rebinds_the_original_slot(bool cancel, bool slotConflict)
    {
        var f = CreateFixture(null, []);
        var origin = (await f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, null, default))!
            with { StatusKey = "before_countersign", ApprovalModeKey = "single" };
        var instance = (await f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, null, default))!;
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(origin);
        var chainId = Guid.CreateVersion7(); var slotId = Guid.CreateVersion7();
        f.Query.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(WorkflowSql.FindActiveCountersignChainByOriginTodo,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowCountersignChainRecord(
                chainId, instance.Id, origin.StepId, origin.Id, "before", "active", origin.AssigneeUserId, DateTimeOffset.UtcNow));
        f.Query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotByTodo,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowApprovalSlotRecord(slotId, 4));
        const string currentInput = "{\"evidence\":[\"019c1a90-8f9b-7b9c-9cf4-b2c7f5a1d003\"]}";
        f.Query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowFormSubmissionRecord(
                Guid.CreateVersion7(), instance.Id, instance.FormVersionId!.Value, currentInput, "digest", 2, f.ActorId, DateTimeOffset.UtcNow));
        if (slotConflict) f.Command.ExecuteAsync(WorkflowSql.RebindApprovalSlotForCountersignResume,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Result<WorkflowInstanceResponse>? result;
        if (cancel)
        {
            var active = origin with { Id = Guid.CreateVersion7(), AssigneeUserId = Guid.CreateVersion7(), StatusKey = "active", Revision = 1 };
            f.Query.QueryAsync<WorkflowCountersignItemRecord>(WorkflowSql.ListCountersignItemsByChain,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[] { new WorkflowCountersignItemRecord(
                    Guid.CreateVersion7(), chainId, 1, active.AssigneeUserId, active.Id, "active") });
            f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call => HasValue(call.ArgAt<object?>(1), "Id", active.Id) ? active : origin);
            result = await f.Service.CancelAsync(origin.Id, f.ActorId, new(3, null, "resume-cancel"));
            await f.Command.Received().ExecuteAsync(WorkflowSql.CancelTodoWithRevision,
                Arg.Is<object?>(p => HasValue(p, "Id", active.Id) && HasValue(p, "Revision", active.Revision)), Arg.Any<CancellationToken>());
        }
        else
        {
            var signer = Guid.CreateVersion7();
            var supplemental = origin with { Id = Guid.CreateVersion7(), AssigneeUserId = signer, StatusKey = "active" };
            f.Query.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(WorkflowSql.FindCountersignItemByTodoId,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowCountersignItemContextRecord(
                    Guid.CreateVersion7(), chainId, 1, signer, supplemental.Id, "active", "before", origin.Id,
                    instance.Id, origin.StepId, "active"));
            result = await f.Service.TryHandleActAsync(supplemental, instance, instance.FormVersionId.Value, currentInput,
                new(1, JsonSerializer.SerializeToElement(new { }), null, "resume-complete", 1), "approve", signer,
                new(null, "host", "host"), new(new Dictionary<string, Result<WorkflowHumanActivityAssignees>>()), default);
        }
        if (slotConflict)
        {
            Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result!.Error?.Code);
            await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.AdvanceInstanceWithRevision,
                Arg.Any<object?>(), Arg.Any<CancellationToken>());
            Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
            return;
        }
        Assert.IsTrue(result!.IsSuccess);
        var newTodoId = result.Value!.ActiveTodoId!.Value;
        Assert.AreNotEqual(origin.Id, newTodoId);
        await f.Command.Received().ExecuteAsync(WorkflowTodoViewSql.Capture,
            Arg.Is<object?>(p => HasValue(p, "Id", newTodoId) && HasValue(p, "InputJson", currentInput)), Arg.Any<CancellationToken>());
        await f.Command.Received().ExecuteAsync(WorkflowSql.RebindApprovalSlotForCountersignResume,
            Arg.Is<object?>(p => HasValue(p, "Id", slotId) && HasValue(p, "TodoId", origin.Id) && HasValue(p, "NewTodoId", newTodoId)),
            Arg.Any<CancellationToken>());
        Received.InOrder(() => {
            f.Command.ExecuteAsync(WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
            f.Command.ExecuteAsync(WorkflowTodoViewSql.Capture, Arg.Any<object?>(), Arg.Any<CancellationToken>());
            f.Command.ExecuteAsync(WorkflowSql.CloseOriginTodoAfterBeforeCountersign, Arg.Any<object?>(), Arg.Any<CancellationToken>());
            f.Command.ExecuteAsync(WorkflowSql.RebindApprovalSlotForCountersignResume, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Chain_and_cancel_receipts_are_bound_to_the_original_todo(bool cancel)
    {
        var assignee = Guid.CreateVersion7(); var f = CreateFixture(null, [assignee]);
        var action = cancel ? "countersign.cancel" : "countersign.after";
        var text = cancel ? "countersign.cancel\n3\n" : $"countersign\nafter\n3\n\n{assignee:D}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowActionReceiptRecord(
                action, f.ActorId, 8, "other-todo", hash, Guid.CreateVersion7(), "active", Guid.CreateVersion7()));
        var result = cancel
            ? await f.Service.CancelAsync(f.TodoId, f.ActorId, new(3, null, "other-todo"))
            : await f.Service.CountersignAsync(f.TodoId, f.ActorId, new("after", [assignee], 3, null, "other-todo"));
        Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error?.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Assignee_directory_is_prepared_before_workflow_transaction()
    {
        var assignee = Guid.CreateVersion7();
        var f = CreateFixture(null, [assignee]);
        f.Directory.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsFalse(f.Transaction.IsActive);
                return new Dictionary<Guid, HostUserDirectoryEntry> { [assignee] = new(assignee, "member", "Member") };
            });
        var result = await f.Service.CountersignAsync(f.TodoId, f.ActorId,
            new("after", [assignee], 3, null, "outside-directory"));
        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task Countersign_replay_uses_first_result_even_after_terminal_or_user_deactivation()
    {
        var assignee = Guid.CreateVersion7(); var actor = Guid.CreateVersion7(); var resultTodo = Guid.CreateVersion7();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"countersign\nbefore\n3\n\n{assignee:D}")));
        var f = CreateFixture(null, [], new("countersign.before", actor, 8, "frozen-chain", hash, resultTodo, "active"),
            actor, instanceStatus: "completed");
        var result = await f.Service.CountersignAsync(f.TodoId, actor, new("before", [assignee], 3, null, "frozen-chain"));
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("active", result.Value!.StatusKey);
        Assert.AreEqual(resultTodo, result.Value.ActiveTodoId);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Cancel_replay_does_not_require_the_closed_chain_to_remain_active()
    {
        var actor = Guid.CreateVersion7(); var resultTodo = Guid.CreateVersion7();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("countersign.cancel\n3\n取消")));
        var f = CreateFixture(null, [], new("countersign.cancel", actor, 8, "cancel-chain", hash, resultTodo, "active"),
            actor, instanceStatus: "completed");
        var result = await f.Service.CancelAsync(f.TodoId, actor, new(3, "取消", "cancel-chain"));
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("active", result.Value!.StatusKey);
        Assert.AreEqual(resultTodo, result.Value.ActiveTodoId);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(f.Command));
    }

    [TestMethod]
    public async Task Last_after_countersigner_uses_next_node_snapshot_instead_of_assigning_self()
    {
        var f = CreateFixture(null, []);
        var original = (await f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, null, default))!
            with { StatusKey = "completed", ResultActionKey = "approve", ApprovalModeKey = "single" };
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(original);
        var instance = (await f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, null, default))!;
        var signer = Guid.CreateVersion7(); var nextActor = Guid.CreateVersion7();
        var supplemental = original with { Id = Guid.CreateVersion7(), AssigneeUserId = signer, StatusKey = "active", ResultActionKey = null };
        f.Query.QuerySingleOrDefaultAsync<WorkflowCountersignItemContextRecord>(WorkflowSql.FindCountersignItemByTodoId,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowCountersignItemContextRecord(
                Guid.CreateVersion7(), Guid.CreateVersion7(), 1, signer, supplemental.Id, "active", "after", original.Id,
                instance.Id, original.StepId, "active"));
        f.Query.QuerySingleOrDefaultAsync<WorkflowApprovalSlotRecord>(WorkflowSql.FindApprovalSlotByTodo,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowApprovalSlotRecord(Guid.CreateVersion7(), 1));
        var draft = JsonSerializer.Serialize(new { schemaVersion = 1, nodes = new object[] {
            new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "finance" } } },
            new { nodeKey = "finance", nodeTypeKey = "human.approval", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "next" } } },
            new { nodeKey = "next", nodeTypeKey = "human.approval", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { nextActor } } } } } },
            new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { } },
        } });
        f.Query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(instance.DefinitionVersionId, instance.FormVersionId!.Value, draft,
                "{\"schemaVersion\":1,\"adapterVersion\":1,\"sections\":[]}", null, "active"));
        var prepared = new WorkflowPreparedHumanActivities(new Dictionary<string, Result<WorkflowHumanActivityAssignees>>
        {
            ["next"] = Result<WorkflowHumanActivityAssignees>.Success(new(null, nextActor)),
        });
        var result = await f.Service.TryHandleActAsync(supplemental, instance, instance.FormVersionId.Value, "{}",
            new(3, JsonSerializer.SerializeToElement(new { }), null, "last-sign", 2), "approve", signer,
            new WorkflowManagementScope(null, "host", "host"), prepared, default);
        Assert.IsTrue(result!.IsSuccess); Assert.AreEqual("active", result.Value!.StatusKey);
        await f.Command.Received().ExecuteAsync(WorkflowSql.InsertTodo,
            Arg.Is<object?>(p => HasValue(p, "AssigneeUserId", nextActor)), Arg.Any<CancellationToken>());
        await f.Command.DidNotReceive().ExecuteAsync(WorkflowSql.InsertTodo,
            Arg.Is<object?>(p => HasValue(p, "AssigneeUserId", signer)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Countersign_endpoints_require_todos_countersign_permission()
    {
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/countersign-candidates",
            WorkflowPermissions.TodosCountersign);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/countersign-chain",
            WorkflowPermissions.TodosCountersign);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/countersign",
            WorkflowPermissions.TodosCountersign);
        await AssertEndpointPermissionAsync(
            "/api/v1/workflow/todos/{todoId:guid}/countersign/cancel",
            WorkflowPermissions.TodosCountersign);
    }

    [TestMethod]
    public async Task Countersign_invalid_assignee_fails_before_writes()
    {
        var fixture = CreateFixture(activeChain: null, hostUsers: []);
        var result = await fixture.Service.CountersignAsync(
            fixture.TodoId,
            fixture.ActorId,
            new CountersignWorkflowTodoRequest(
                "before",
                [Guid.CreateVersion7()],
                3,
                "请协助审批",
                "cs-001"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.TodoCountersignAssigneeInvalid, result.Error!.Code);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(fixture.Command));
    }

    [TestMethod]
    public async Task Countersign_before_creates_first_countersign_todo_and_suspends_origin()
    {
        var assignee = Guid.CreateVersion7();
        var fixture = CreateFixture(activeChain: null, hostUsers: [assignee]);
        var result = await fixture.Service.CountersignAsync(
            fixture.TodoId,
            fixture.ActorId,
            new CountersignWorkflowTodoRequest(
                "before",
                [assignee],
                3,
                "请协助审批",
                "cs-001"));

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.SuspendOriginTodoForBeforeCountersign, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Command.Received().ExecuteAsync(
            WorkflowSql.InsertTodo, Arg.Is<object?>(value => HasValue(value, "AssigneeUserId", assignee)),
            Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            fixture.Command.ExecuteAsync(WorkflowSql.InsertTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>());
            fixture.Command.ExecuteAsync(WorkflowSql.InsertCountersignItem, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        });
        Assert.AreEqual(1, fixture.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Countersign_same_idempotency_replays_without_writes()
    {
        var assignee = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"countersign\nbefore\n3\n请协助审批\n{assignee:D}")));
        var resultTodoId = Guid.CreateVersion7();
        var receipt = new WorkflowActionReceiptRecord(
            "countersign.before", actorId, 8, "cs-001", requestHash, resultTodoId, "active");
        var fixture = CreateFixture(activeChain: null, hostUsers: [assignee], receipt: receipt, actorId: actorId);

        var result = await fixture.Service.CountersignAsync(
            fixture.TodoId,
            fixture.ActorId,
            new CountersignWorkflowTodoRequest(
                "before",
                [assignee],
                3,
                "请协助审批",
                "cs-001"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(resultTodoId, result.Value!.ActiveTodoId);
        Assert.AreEqual(0, WorkflowTodoManagementTestDependencies.CountBusinessCommands(fixture.Command));
    }

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

    private static CountersignFixture CreateFixture(
        WorkflowCountersignChainRecord? activeChain,
        IReadOnlyList<Guid> hostUsers,
        WorkflowActionReceiptRecord? receipt = null,
        Guid? actorId = null,
        string instanceStatus = "active")
    {
        var instanceId = Guid.CreateVersion7();
        var todoId = Guid.CreateVersion7();
        if (receipt is not null) receipt = receipt with { TodoId = todoId };
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
                "single", 1, 1, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowApprovalTallyRecord>(WorkflowSql.FindApprovalTallyByStepSqlServer,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowApprovalTallyRecord(1, 0, 0));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(
                WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(
                instanceId, null, "host", "host", definitionVersionId, formVersionId,
                "purchase", "PO-1", null, instanceStatus, 7, resolvedActorId, now, null, null, null, null, null, null));
        query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(
                WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(receipt);
        query.QuerySingleOrDefaultAsync<WorkflowCountersignChainRecord>(
                WorkflowSql.FindActiveCountersignChainByOriginTodo, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(activeChain);
        query.QueryAsync<WorkflowCountersignItemRecord>(
                WorkflowSql.ListCountersignItemsByChain, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowCountersignItemRecord>>([]));
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowFormSubmissionRecord(
                Guid.CreateVersion7(), instanceId, formVersionId, "{}", "none", 1, resolvedActorId, now));

        var hostDirectory = Substitute.For<IHostUserBatchSelectionDirectory>();
        hostDirectory.FindActiveHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(hostUsers.ToDictionary(
                id => id,
                id => new HostUserDirectoryEntry(id, $"user-{id:N}".Substring(0, 8), $"User {id:N}".Substring(0, 12))));

        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var outbox = Substitute.For<IOutboxWriter>();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var transaction = new TrackingTransaction();
        var service = new WorkflowTodoCountersignService(
            query,
            command,
            transaction,
            tenant,
            Substitute.For<IClock>(),
            ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            hostDirectory,
            Substitute.For<ITenantMemberSelectionDirectory>(),
            new WorkflowNotificationOutboxPublisher(outbox),
            new WorkflowBranchStateCoordinator(query, command),
            WorkflowTodoManagementTestDependencies.CreateStepResolution(query, command, ids, outbox));

        return new CountersignFixture(service, query, command, outbox, todoId, resolvedActorId, hostDirectory, transaction);
    }

    private static bool HasValue(object? parameters, string name, object expected) =>
        parameters is IReadOnlyDictionary<string, object?> values &&
        values.TryGetValue(name, out var actual) && actual?.Equals(expected) == true;

    private sealed class TrackingTransaction : ICommandTransaction
    {
        public bool IsActive { get; private set; }
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            IsActive = true;
            try { return await action(cancellationToken); }
            finally { IsActive = false; }
        }
    }

    private sealed record CountersignFixture(
        WorkflowTodoCountersignService Service,
        IQueryExecutor Query,
        ICommandExecutor Command,
        IOutboxWriter Outbox,
        Guid TodoId,
        Guid ActorId,
        IHostUserBatchSelectionDirectory Directory,
        TrackingTransaction Transaction);
}
