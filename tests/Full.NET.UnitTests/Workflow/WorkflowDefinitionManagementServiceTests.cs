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
            query, command, new ImmediateTransaction(), tenant, clock, ids);

        var result = await service.PublishAsync(
            definitionId, actorId, new PublishWorkflowDefinitionRequest(1, formVersionId));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(WorkflowErrorCodes.VersionNotPublished, result.Error!.Code);
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

    private sealed class ImmediateTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
