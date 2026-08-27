using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper.Outbox;
using Full.NET.Messaging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Data;

/// <summary>
/// 验证 Outbox Writer 的 Typed Plan 是显式候选路径，默认仍经过通用执行器。
/// </summary>
[TestClass]
public sealed class DapperTypedOutboxWriterTests
{
    [TestMethod]
    public async Task Legacy_writer_defaults_to_static_registry_executor_path()
    {
        var collaborators = CreateCollaborators();
        collaborators.Command
            .ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var writer = new DapperOutboxWriter(
            collaborators.Command,
            collaborators.Serializer,
            collaborators.IdGenerator,
            collaborators.CurrentTenant,
            collaborators.Clock);

        await writer.AddAsync(
            "fullnet.test.entity.changed",
            1,
            new SamplePayload("legacy"));

        await collaborators.Command.Received(1).ExecuteAsync(
            Arg.Is<SqlStatement>(statement =>
                statement != null && statement.Name == "outbox.insert"),
            Arg.Any<OutboxMessage>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Append_writer_defaults_to_static_registry_executor_path()
    {
        var collaborators = CreateCollaborators();
        collaborators.Command
            .ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var writer = new DapperAppendOnlyOutboxWriter(
            collaborators.Command,
            collaborators.Serializer,
            collaborators.IdGenerator,
            collaborators.CurrentTenant,
            collaborators.Clock);

        await writer.AddAsync(
            "fullnet.test.entity.changed",
            1,
            new SamplePayload("append"),
            IntegrationEventMetadata.Create("partition-1", "fullnet.test"));

        await collaborators.Command.Received(1).ExecuteAsync(
            Arg.Is<SqlStatement>(statement =>
                statement != null
                && statement.Name == "messaging.outbox.append"),
            Arg.Any<AppendOnlyOutboxMessage>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Typed_plan_mode_refuses_non_dapper_executor_instead_of_falling_back()
    {
        var collaborators = CreateCollaborators();
        var writer = new DapperOutboxWriter(
            collaborators.Command,
            collaborators.Serializer,
            collaborators.IdGenerator,
            collaborators.CurrentTenant,
            collaborators.Clock,
            DapperOutboxCommandPath.TypedPlan);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => writer.AddAsync(
                "fullnet.test.entity.changed",
                1,
                new SamplePayload("typed")));

        StringAssert.Contains(exception.Message, "DapperSqlExecutor");
        await collaborators.Command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    private static Collaborators CreateCollaborators()
    {
        var command = Substitute.For<ICommandExecutor>();
        var serializer = Substitute.For<IIntegrationEventSerializer>();
        serializer.ContentType.Returns("application/x-memorypack");
        serializer.Serialize(Arg.Any<object>()).Returns([1, 2, 3]);
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Id.Returns(
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(
            new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero));
        return new Collaborators(
            command,
            serializer,
            idGenerator,
            currentTenant,
            clock);
    }

    private sealed record Collaborators(
        ICommandExecutor Command,
        IIntegrationEventSerializer Serializer,
        IIdGenerator IdGenerator,
        ICurrentTenant CurrentTenant,
        IClock Clock);

    private sealed record SamplePayload(string Value);
}
