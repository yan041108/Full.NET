using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Identity.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowDefinitionManagementServiceTests
{
    [TestMethod]
    public async Task Publish_rejects_form_version_outside_the_trusted_scope_before_writing()
    {
        var definitionId = Guid.CreateVersion7();
        var draftId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var formVersionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var clock = Substitute.For<IClock>();
        var ids = Substitute.For<IIdGenerator>();
        var users = Substitute.For<IHostUserDirectory>();
        tenant.IsHost.Returns(true);
        clock.UtcNow.Returns(now);

        query.QuerySingleOrDefaultAsync<WorkflowDefinitionRecord>(
                WorkflowSql.FindDefinitionById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowDefinitionRecord(
                definitionId, null, "host", "host", "leave", draftId, null,
                actorId, now, null, 1));
        query.QuerySingleOrDefaultAsync<WorkflowDefinitionDraftRecord>(
                WorkflowSql.FindDefinitionDraftByDefinition, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowDefinitionDraftRecord(
                draftId, definitionId, CreateValidDraftJson(), 1, new string('a', 64), actorId, now));
        query.QuerySingleOrDefaultAsync<WorkflowFormVersionRecord>(
                WorkflowSql.FindFormVersionById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((WorkflowFormVersionRecord?)null);

        var service = new WorkflowDefinitionManagementService(
            query, command, new ImmediateTransaction(), tenant, clock, ids, users);

        var result = await service.PublishAsync(
            definitionId, actorId, new PublishWorkflowDefinitionRequest(1, formVersionId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.VersionNotPublished, result.Error!.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Publish_rejects_inactive_cc_recipient_before_claiming_the_draft()
    {
        var definitionId = Guid.CreateVersion7();
        var draftId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var recipientId = Guid.CreateVersion7();
        var formDefinitionId = Guid.CreateVersion7();
        var formVersionId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var clock = Substitute.For<IClock>();
        var ids = Substitute.For<IIdGenerator>();
        var users = Substitute.For<IHostUserDirectory>();
        var transaction = new TrackingTransaction();
        tenant.IsHost.Returns(true);
        clock.UtcNow.Returns(now);

        query.QuerySingleOrDefaultAsync<WorkflowDefinitionRecord>(
                WorkflowSql.FindDefinitionById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowDefinitionRecord(
                definitionId, null, "host", "host", "leave", draftId, null,
                actorId, now, null, 1));
        query.QuerySingleOrDefaultAsync<WorkflowDefinitionDraftRecord>(
                WorkflowSql.FindDefinitionDraftByDefinition, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowDefinitionDraftRecord(
                draftId, definitionId, CreateCcDraftJson(recipientId), 1,
                new string('a', 64), actorId, now));
        query.QuerySingleOrDefaultAsync<WorkflowFormVersionRecord>(
                WorkflowSql.FindFormVersionById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowFormVersionRecord(
                formVersionId, formDefinitionId, 1, 1, 1, 1,
                CreateFormSchemaJson(), "{}", new string('b', 64), actorId, now));
        users.FindActiveHostUserAsync(recipientId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsFalse(transaction.HasStarted,
                    "跨模块 Identity 目录读取必须发生在 Workflow 本地事务之外。");
                return (HostUserDirectoryEntry?)null;
            });

        var service = new WorkflowDefinitionManagementService(
            query, command, transaction, tenant, clock, ids, users);

        var result = await service.PublishAsync(
            definitionId, actorId, new PublishWorkflowDefinitionRequest(1, formVersionId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.DefinitionCcRecipientsInvalid, result.Error!.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    private static string CreateValidDraftJson()
    {
        using var startConfig = JsonDocument.Parse("""{"nextNodeKeys":["end"]}""");
        using var endConfig = JsonDocument.Parse("{} ");
        var draft = new WorkflowDefinitionDraft(1,
        [
            new("start", "start", 1, startConfig.RootElement.Clone()),
            new("end", "end", 1, endConfig.RootElement.Clone()),
        ]);
        return JsonSerializer.Serialize(draft);
    }

    private static string CreateCcDraftJson(Guid recipientId)
    {
        var draft = new WorkflowDefinitionDraft(1,
        [
            new("start", "start", 1,
                JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "copy" } })),
            new("copy", "notify.cc", 1,
                JsonSerializer.SerializeToElement(new
                {
                    nextNodeKeys = new[] { "approve" },
                    recipientUserIds = new[] { recipientId },
                })),
            new("approve", "human.approval", 1,
                JsonSerializer.SerializeToElement(new { nextNodeKeys = new[] { "end" } })),
            new("end", "end", 1,
                JsonSerializer.SerializeToElement(new { nextNodeKeys = Array.Empty<string>() })),
        ]);
        return JsonSerializer.Serialize(draft);
    }

    private static string CreateFormSchemaJson() =>
        JsonSerializer.Serialize(new WorkflowFormSchema(1, 1,
        [
            new WorkflowFormSection("main",
            [
                new WorkflowFormField("reason", "text", false,
                    new Dictionary<string, JsonElement>()),
            ]),
        ]));

    private sealed class ImmediateTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
            action(cancellationToken);
    }

    private sealed class TrackingTransaction : ICommandTransaction
    {
        public bool HasStarted { get; private set; }

        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            HasStarted = true;
            return action(cancellationToken);
        }
    }
}
