using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageMyInboxMessages;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class MyInboxManagementServiceTests
{
    [TestMethod]
    public async Task Mark_read_enqueues_state_change_inside_transaction()
    {
        var fixture = CreateFixture();
        var messageId = Guid.CreateVersion7();
        var unread = CreateRecord(
            messageId,
            fixture.RecipientUserId,
            InboxMessageStatuses.Unread,
            readAtUtc: null);
        var read = CreateRecord(
            messageId,
            fixture.RecipientUserId,
            InboxMessageStatuses.Read,
            fixture.Now);
        fixture.Query.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(unread, read);
        fixture.Command.ExecuteAsync(
                InboxMessageSql.MarkRead,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        ConfigureOutboxAssertion(fixture);

        var result = await fixture.Service.MarkReadAsync(
            fixture.RecipientUserId,
            messageId);

        Assert.IsTrue(result.IsSuccess);
        await fixture.OutboxWriter.Received(1).AddAsync(
            NotificationRealtimeEventTypes.InboxReadStateChanged,
            1,
            Arg.Is<InboxReadStateChangedIntegrationEvent>(integrationEvent =>
                integrationEvent != null
                && integrationEvent.RecipientUserId == fixture.RecipientUserId),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Mark_read_when_already_read_does_not_enqueue()
    {
        var fixture = CreateFixture();
        var messageId = Guid.CreateVersion7();
        fixture.Query.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateRecord(
                messageId,
                fixture.RecipientUserId,
                InboxMessageStatuses.Read,
                fixture.Now));

        var result = await fixture.Service.MarkReadAsync(
            fixture.RecipientUserId,
            messageId);

        Assert.IsTrue(result.IsSuccess);
        await fixture.OutboxWriter.DidNotReceive().AddAsync(
            NotificationRealtimeEventTypes.InboxReadStateChanged,
            1,
            Arg.Any<InboxReadStateChangedIntegrationEvent>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Mark_all_read_enqueues_only_when_rows_changed()
    {
        var changed = CreateFixture();
        changed.Command.ExecuteAsync(
                InboxMessageSql.MarkAllRead,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(3);
        ConfigureOutboxAssertion(changed);

        var changedResult = await changed.Service.MarkAllReadAsync(
            changed.RecipientUserId);

        Assert.IsTrue(changedResult.IsSuccess);
        await changed.OutboxWriter.Received(1).AddAsync(
            NotificationRealtimeEventTypes.InboxReadStateChanged,
            1,
            Arg.Any<InboxReadStateChangedIntegrationEvent>(),
            Arg.Any<CancellationToken>());

        var unchanged = CreateFixture();
        unchanged.Command.ExecuteAsync(
                InboxMessageSql.MarkAllRead,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);

        var unchangedResult = await unchanged.Service.MarkAllReadAsync(
            unchanged.RecipientUserId);

        Assert.IsTrue(unchangedResult.IsSuccess);
        await unchanged.OutboxWriter.DidNotReceive().AddAsync(
            NotificationRealtimeEventTypes.InboxReadStateChanged,
            1,
            Arg.Any<InboxReadStateChangedIntegrationEvent>(),
            Arg.Any<CancellationToken>());
    }

    private static Fixture CreateFixture()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var outboxWriter = Substitute.For<IOutboxWriter>();
        var publisher = Substitute.For<IRealtimePublisher>();
        var clock = Substitute.For<IClock>();
        var transaction = new RecordingTransaction();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        query.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0L);
        var service = new MyInboxManagementService(
            query,
            command,
            transaction,
            outboxWriter,
            new NotificationRealtimeDelivery(query, publisher),
            clock,
            NullLogger<MyInboxManagementService>.Instance);
        return new Fixture(
            query,
            command,
            outboxWriter,
            transaction,
            service,
            Guid.CreateVersion7(),
            now);
    }

    private static void ConfigureOutboxAssertion(Fixture fixture)
    {
        fixture.OutboxWriter.AddAsync(
                NotificationRealtimeEventTypes.InboxReadStateChanged,
                1,
                Arg.Any<InboxReadStateChangedIntegrationEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsTrue(fixture.Transaction.IsActive);
                return Task.CompletedTask;
            });
    }

    private static InboxMessageRecord CreateRecord(
        Guid messageId,
        Guid recipientUserId,
        string status,
        DateTimeOffset? readAtUtc) =>
        new()
        {
            Id = messageId,
            RecipientUserId = recipientUserId,
            Title = "系统消息",
            Content = "消息正文",
            Status = status,
            ReadAtUtc = readAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    private sealed record Fixture(
        IQueryExecutor Query,
        ICommandExecutor Command,
        IOutboxWriter OutboxWriter,
        RecordingTransaction Transaction,
        MyInboxManagementService Service,
        Guid RecipientUserId,
        DateTimeOffset Now);

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public bool IsActive { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            IsActive = true;
            try
            {
                return await action(cancellationToken);
            }
            finally
            {
                IsActive = false;
            }
        }
    }
}
