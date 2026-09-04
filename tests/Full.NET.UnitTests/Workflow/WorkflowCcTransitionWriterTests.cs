using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowCcTransitionWriterTests
{
    [TestMethod]
    public async Task Write_creates_completed_step_and_only_new_instance_recipients()
    {
        var instanceId = Guid.CreateVersion7();
        var existingRecipient = Guid.CreateVersion7();
        var newRecipient = Guid.CreateVersion7();
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var ids = Substitute.For<IIdGenerator>();
        query.QueryAsync<Guid>(
                WorkflowSql.ListCcRecipientIdsByInstance,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new[] { existingRecipient });
        ids.NewId().Returns(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        var writer = new WorkflowCcTransitionWriter(query, command, ids);

        await writer.WriteAsync(
            instanceId,
            "host",
            [new WorkflowCcRuntimeNode("copy", [existingRecipient, newRecipient])],
            DateTimeOffset.UtcNow);

        await command.Received(1).ExecuteAsync(
            WorkflowSql.InsertCompletedCcStep,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(
            WorkflowSql.InsertCc,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(
            WorkflowSql.InsertExecutionLog,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }
}
