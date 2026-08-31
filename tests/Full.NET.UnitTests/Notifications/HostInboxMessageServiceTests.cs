using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
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
                InboxMessageSql.InsertHost,
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
        var service = CreateService(
            query,
            command,
            transaction,
            outboxWriter,
            userDirectory,
            publisher,
            clock,
            idGenerator);

        var result = await service.SendAsync(
            actorUserId,
            new SendHostInboxMessageRequest(
                recipientUserId,
                "系统消息",
                "消息正文"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, transaction.ExecutionCount);
        await outboxWriter.Received(1).AddAsync(
            NotificationRealtimeEventTypes.InboxMessageReceived,
            1,
            Arg.Is<InboxMessageReceivedIntegrationEvent>(integrationEvent =>
                integrationEvent != null
                && integrationEvent.RecipientUserId == recipientUserId
                && integrationEvent.MessageId == messageId
                && integrationEvent.Title == "系统消息"
                && integrationEvent.TenantScopeKey == "host"),
            Arg.Any<CancellationToken>());
        await publisher.Received(2).PublishToUserAsync(
            recipientUserId,
            Arg.Any<RealtimeMessage>(),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task Send_rejects_tenant_session_without_directory_or_transaction()
    {
        var transaction = new RecordingTransaction();
        var userDirectory = Substitute.For<IHostUserDirectory>();
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.IsHost.Returns(false);
        currentTenant.IsAvailable.Returns(true);
        currentTenant.Id.Returns(Guid.CreateVersion7());
        var service = new HostInboxMessageService(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            transaction,
            Substitute.For<IOutboxWriter>(),
            userDirectory,
            currentTenant,
            new NotificationRealtimeDelivery(
                Substitute.For<IQueryExecutor>(),
                Substitute.For<IRealtimePublisher>()),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(),
            NullLogger<HostInboxMessageService>.Instance);

        var result = await service.SendAsync(
            Guid.CreateVersion7(),
            new SendHostInboxMessageRequest(Guid.CreateVersion7(), "标题", "正文"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.InboxScopeForbidden, result.Error!.Code);
        Assert.AreEqual(0, transaction.ExecutionCount);
        await userDirectory.DidNotReceive().FindActiveHostUserAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Send_does_not_start_transaction_when_recipient_not_found()
    {
        var transaction = new RecordingTransaction();
        var userDirectory = Substitute.For<IHostUserDirectory>();
        var recipientUserId = Guid.CreateVersion7();
        userDirectory.FindActiveHostUserAsync(
                recipientUserId,
                Arg.Any<CancellationToken>())
            .Returns((HostUserDirectoryEntry?)null);
        var service = CreateService(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            transaction,
            Substitute.For<IOutboxWriter>(),
            userDirectory,
            Substitute.For<IRealtimePublisher>(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var result = await service.SendAsync(
            Guid.CreateVersion7(),
            new SendHostInboxMessageRequest(recipientUserId, "标题", "正文"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.InboxRecipientNotFound, result.Error!.Code);
        Assert.AreEqual(0, transaction.ExecutionCount);
    }

    [TestMethod]
    public async Task Send_does_not_start_transaction_when_directory_throws()
    {
        var transaction = new RecordingTransaction();
        var userDirectory = Substitute.For<IHostUserDirectory>();
        var recipientUserId = Guid.CreateVersion7();
        userDirectory
            .FindActiveHostUserAsync(recipientUserId, Arg.Any<CancellationToken>())
            .Returns<HostUserDirectoryEntry?>(_ => throw new InvalidOperationException("directory unavailable"));
        var service = CreateService(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ICommandExecutor>(),
            transaction,
            Substitute.For<IOutboxWriter>(),
            userDirectory,
            Substitute.For<IRealtimePublisher>(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.SendAsync(
                Guid.CreateVersion7(),
                new SendHostInboxMessageRequest(recipientUserId, "标题", "正文")));

        Assert.AreEqual(0, transaction.ExecutionCount);
    }

    [TestMethod]
    public async Task Send_returns_failure_without_committing_when_message_record_missing_after_insert()
    {
        var transaction = new RecordingTransaction();
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var outboxWriter = Substitute.For<IOutboxWriter>();
        var userDirectory = Substitute.For<IHostUserDirectory>();
        var recipientUserId = Guid.CreateVersion7();
        userDirectory.FindActiveHostUserAsync(
                recipientUserId,
                Arg.Any<CancellationToken>())
            .Returns(new HostUserDirectoryEntry(recipientUserId, "recipient", "收件人"));
        command.ExecuteAsync(
                InboxMessageSql.InsertHost,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        query.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindForRecipientById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((InboxMessageRecord?)null);
        var service = CreateService(
            query,
            command,
            transaction,
            outboxWriter,
            userDirectory,
            Substitute.For<IRealtimePublisher>(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var result = await service.SendAsync(
            Guid.CreateVersion7(),
            new SendHostInboxMessageRequest(recipientUserId, "标题", "正文"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.InboxMessageNotFound, result.Error!.Code);
        Assert.AreEqual(1, transaction.ExecutionCount);
        await outboxWriter.DidNotReceive().AddAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<InboxMessageReceivedIntegrationEvent>(),
            Arg.Any<CancellationToken>());
    }

    private static HostInboxMessageService CreateService(
        IQueryExecutor query,
        ICommandExecutor command,
        RecordingTransaction transaction,
        IOutboxWriter outboxWriter,
        IHostUserDirectory userDirectory,
        IRealtimePublisher publisher,
        IClock clock,
        IIdGenerator idGenerator) =>
        new(
            query,
            command,
            transaction,
            outboxWriter,
            userDirectory,
            CreateHostTenant(),
            new NotificationRealtimeDelivery(query, publisher),
            clock,
            idGenerator,
            NullLogger<HostInboxMessageService>.Instance);

    private static ICurrentTenant CreateHostTenant()
    {
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.IsHost.Returns(true);
        currentTenant.IsAvailable.Returns(true);
        return currentTenant;
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public bool IsActive { get; private set; }

        public int ExecutionCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
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
