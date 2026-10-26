using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>个人选票、共享结果及首次回执分别裁定，部分提交不能提前推进。</summary>
[TestClass]
public sealed class WorkflowVoteTodoServiceTests
{
    [TestMethod]
    public async Task Partial_ballot_does_not_update_main_form_or_close_vote_even_when_current_percentages_pass()
    {
        var f = Create(new(2, 0, 0, 1, 0));
        var result = await f.Service.VoteAsync(f.Todo.Id, f.Actor, f.Request);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code); Assert.AreEqual("active", result.Value!.StatusKey);
        await f.Commands.Received(1).ExecuteAsync(WorkflowVoteSql.SubmitBallot, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowVoteSql.SetViewOutput,
            Arg.Is<object?>(p => ((IReadOnlyDictionary<string, object?>)p!)["OutputJson"]!.ToString()!.Contains("\"choiceKey\":\"yes\"", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowVoteSql.CloseRound, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowSql.DecideApprovalSlotWithRevision, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Final_approved_vote_writes_only_declared_result_and_completes_once()
    {
        var f = Create(new(3, 0, 0, 0, 0));
        var result = await f.Service.VoteAsync(f.Todo.Id, f.Actor, f.Request);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code); Assert.AreEqual("completed", result.Value!.StatusKey);
        await f.Commands.Received(1).ExecuteAsync(WorkflowSql.UpdateFormSubmissionWithRevision,
            Arg.Is<object?>(p => MainResultOnly(p)), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowVoteSql.CloseRound, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Terminal_replay_returns_first_result_without_second_claim_or_vote()
    {
        var f = Create(new(3, 0, 0, 0, 0));
        using var values = JsonDocument.Parse("{\"reason\":\"private\"}");
        Assert.IsTrue(WorkflowVoteSubmissionRules.TryCompile(f.BallotSchema, values.RootElement, "yes", true, out var submitted));
        var hash = WorkflowVoteSubmissionRules.HashRequest(f.Todo.Id, f.Actor, 1, 1, submitted!, null, null);
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Todo with { StatusKey = "completed", Revision = 3 });
        f.Query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(f.Instance with { StatusKey = "cancelled", Revision = 99 });
        f.Query.QuerySingleOrDefaultAsync<WorkflowActionReceiptRecord>(WorkflowSql.FindActionReceipt, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowActionReceiptRecord("vote", f.Actor, 8, f.Request.IdempotencyKey, hash, null, "completed", f.Todo.Id));
        var result = await f.Service.VoteAsync(f.Todo.Id, f.Actor, f.Request);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(8L, result.Value!.Revision); Assert.AreEqual("completed", result.Value.StatusKey);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count()); Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("deadline")]
    [DataRow("actor")]
    [DataRow("revision")]
    public async Task Invalid_authority_or_closed_revision_is_rejected_before_any_write(string invalid)
    {
        var f = Create(new(1, 0, 0, 2, 0));
        if (invalid == "deadline") f.Query.QuerySingleOrDefaultAsync<WorkflowVoteRoundRecord>(WorkflowVoteSql.FindRound,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(f.Round with { ClosesAtUtc = f.Now });
        var result = await f.Service.VoteAsync(f.Todo.Id, invalid == "actor" ? Guid.CreateVersion7() : f.Actor,
            invalid == "revision" ? f.Request with { ExpectedBallotRevision = 2 } : f.Request);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    private static bool MainResultOnly(object? parameters)
    {
        using var json = JsonDocument.Parse((string)((IReadOnlyDictionary<string, object?>)parameters!)["SubmissionJson"]!);
        return json.RootElement.GetProperty("accepted").GetBoolean() && json.RootElement.GetProperty("summary").GetString() == "initial" &&
            !json.RootElement.TryGetProperty("reason", out _);
    }

    private static Fixture Create(WorkflowVoteCountsRecord counts)
    {
        var actor = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow; var main = Guid.CreateVersion7(); var ballotVersion = Guid.CreateVersion7();
        var instanceId = Guid.CreateVersion7(); var stepId = Guid.CreateVersion7();
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instanceId, stepId, actor, "active", now, null, null, 1,
            "vote", 1, null, null, null, null, null, null) { NodeTypeKey = WorkflowHumanActivityKinds.Vote };
        var instance = new WorkflowInstanceRecord(instanceId, null, "host", "host", Guid.CreateVersion7(), main, "vote", "one", null,
            "active", 7, actor, now, null, null, null, null, null, null);
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>(); var files = Substitute.For<IHostFileReferenceClaimService>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var mainSchema = new WorkflowFormSchema(1, 1, [new("main", [new("summary", "text", true, new Dictionary<string, JsonElement>()), new("accepted", "switch", false, new Dictionary<string, JsonElement>())])]);
        var ballotSchema = new WorkflowFormSchema(1, 1, [new("ballot", [new("reason", "text", true, new Dictionary<string, JsonElement>())])]);
        var graph = new WorkflowDefinitionDraft(1, [new("start", "start", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "vote" } })),
            new("vote", "human.vote", 1, JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" }, votePolicy = new {
                ballotFormVersionId = ballotVersion, participationPercentage = 66, approvalPercentage = 100, allowAbstain = true, resultFieldKey = "accepted" } })),
            new("end", "end", 1, JsonSerializer.SerializeToElement(new { }))]);
        var canonical = JsonSerializer.Serialize(graph, WorkflowJsonSerializerContext.Default.WorkflowDefinitionDraft);
        var ballotJson = JsonSerializer.Serialize(ballotSchema, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var binding = new WorkflowVoteFormBindingRecord(Guid.CreateVersion7(), ballotVersion, ballotJson, WorkflowSignatureCoordinator.HashSubmission(ballotJson), "active");
        var round = new WorkflowVoteRoundRecord(stepId, instanceId, binding.Id, 3, 66, 100, true, null, "active", 1, null, null, null, null);
        query.QuerySingleOrDefaultAsync<WorkflowTodoRuntimeRecord>(WorkflowSql.FindTodoById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(todo);
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowRuntimeAssetRecord(
            instance.DefinitionVersionId, main, canonical, JsonSerializer.Serialize(mainSchema, WorkflowJsonSerializerContext.Default.WorkflowFormSchema), null, "active"));
        query.QuerySingleOrDefaultAsync<WorkflowVoteRoundRecord>(WorkflowVoteSql.FindRound, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(round);
        query.QuerySingleOrDefaultAsync<WorkflowVoteFormBindingRecord>(WorkflowVoteSql.FindFormBinding, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(binding);
        query.QuerySingleOrDefaultAsync<WorkflowVoteBallotRecord>(WorkflowVoteSql.FindBallot, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowVoteBallotRecord(
            Guid.CreateVersion7(), stepId, todo.Id, actor, "pending", null, "{}", "{}", 1));
        query.QuerySingleOrDefaultAsync<WorkflowVoteCountsRecord>(WorkflowVoteSql.TallySqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(counts);
        query.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowFormSubmissionRecord(
            Guid.CreateVersion7(), instanceId, main, "{\"summary\":\"initial\",\"accepted\":false}", "Internal", 4, actor, now));
        var tenant = new CurrentTenantAccessor(); tenant.SetHost(); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7()); var outbox = Substitute.For<IOutboxWriter>();
        var branch = new WorkflowBranchStateCoordinator(query, commands);
        var attach = new WorkflowVoteAttachmentCoordinator(files, Substitute.For<IHostFileDescriptorReader>(), Substitute.For<ITenantResourceFileDescriptorReader>(),
            query, commands, tenant, clock, ids, NullLogger<WorkflowVoteAttachmentCoordinator>.Instance);
        var writer = new WorkflowVoteResultWriter(query, commands, branch, WorkflowTodoManagementTestDependencies.CreateTransitionExecutor(query, commands, ids, outbox),
            WorkflowTodoManagementTestDependencies.CreateParallelJoinCoordinator(query, commands), new(outbox),
            WorkflowTodoManagementTestDependencies.CreateResultCallbacks(query, commands, ids));
        var service = new WorkflowVoteTodoService(query, commands, new Transaction(), tenant, clock, ids, attach,
            WorkflowTodoManagementTestDependencies.CreateSignatureCoordinator(query, commands), new(WorkflowTodoManagementTestDependencies.CreateAssigneeCoordinator(), query, WorkflowTodoManagementTestDependencies.CreateStarterSelectionPreparation()),
            new(query, commands, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer })), writer, branch);
        return new(service, query, commands, files, outbox, todo, instance, actor, now, round, ballotSchema,
            new(1, 1, "yes", JsonDocument.Parse("{\"reason\":\"private\"}").RootElement.Clone(), null, "vote-once"));
    }
    private sealed class Transaction : ICommandTransaction
    { public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => action(cancellationToken); }
    private sealed record Fixture(WorkflowVoteTodoService Service, IQueryExecutor Query, ICommandExecutor Commands, IHostFileReferenceClaimService Files,
        IOutboxWriter Outbox, WorkflowTodoRuntimeRecord Todo, WorkflowInstanceRecord Instance, Guid Actor, DateTimeOffset Now,
        WorkflowVoteRoundRecord Round, WorkflowFormSchema BallotSchema, VoteWorkflowTodoRequest Request);
}
