using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.SendHostInboxMessages;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class HostInboxMessageServiceTests
{
    [TestMethod]
    public async Task Send_enqueues_received_event_inside_transaction()
    {
        var transaction = new RecordingTransaction();
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var outboxWriter = Substitute.For<IOutboxWriter>();
        var userDirectory = Substitute.For<IHostUserDirectory>();
        var publisher = Substitute.For<IRealtimePublisher>();
        var clock = Substitute.For<IClock>();
        var idGenerator = Substitute.For<IIdGenerator>();
        var actorUserId = Guid.CreateVersion7();
        var recipientUserId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        userDirectory.FindActiveHostUserAsync(
                recipientUserId,
                Arg.Any<CancellationToken>())
            .Returns(new HostUserDirectoryEntry(
                recipientUserId,
                "recipient",
                "收件人"));
        command.ExecuteAsync(
                InboxMessageSql.Insert,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        query.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new InboxMessageRecord
            {
                Id = messageId,
                RecipientUserId = recipientUserId,
                Title = "系统消息",
                Content = "消息正文",
                Status = InboxMessageStatuses.Unread,
                CreatedAtUtc = now,
                CreatedByUserId = actorUserId,
            });
        query.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1L);
        clock.UtcNow.Returns(now);
        idGenerator.NewId().Returns(messageId);
        outboxWriter.AddAsync(
                NotificationRealtimeEventTypes.InboxMessageReceived,
                1,
                Arg.Any<InboxMessageReceivedIntegrationEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsTrue(transaction.IsActive);
                return Task.CompletedTask;
            });
        var service = new HostInboxMessageService(
            query,
            command,
            transaction,
            outboxWriter,
            userDirectory,
            new NotificationRealtimeDelivery(query, publisher),
            clock,
            idGenerator,
            NullLogger<HostInboxMessageService>.Instance);

        var result = await service.SendAsync(
            actorUserId,
            new SendHostInboxMessageRequest(
                recipientUserId,
                "系统消息",
                "消息正文"));

        Assert.IsTrue(result.IsSuccess);
        await outboxWriter.Received(1).AddAsync(
            NotificationRealtimeEventTypes.InboxMessageReceived,
            1,
            Arg.Is<InboxMessageReceivedIntegrationEvent>(integrationEvent =>
                integrationEvent != null
                && integrationEvent.RecipientUserId == recipientUserId
                && integrationEvent.MessageId == messageId
                && integrationEvent.Title == "系统消息"),
            Arg.Any<CancellationToken>());
        await publisher.Received(2).PublishToUserAsync(
            recipientUserId,
            Arg.Any<RealtimeMessage>(),
            CancellationToken.None);
    }

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
