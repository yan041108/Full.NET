using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Realtime;
using Full.NET.Serialization.MessagePack;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationRealtimeIntegrationEventHandlerTests
{
    [TestMethod]
    public async Task Announcement_handler_publishes_existing_realtime_contract()
    {
        var publisher = Substitute.For<IRealtimePublisher>();
        var publishedMessages = new List<RealtimeMessage>();
        publisher
            .When(instance => instance.PublishToGroupAsync(
                Arg.Any<string>(),
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>()))
            .Do(call => publishedMessages.Add(call.ArgAt<RealtimeMessage>(1)));
        var serializer = new MessagePackIntegrationEventSerializer();
        var handler = new AnnouncementPublishedRealtimeHandler(
            serializer,
            CreateDelivery(publisher));
        var announcementId = Guid.CreateVersion7();

        await handler.HandleAsync(
            serializer.Serialize(new AnnouncementPublishedIntegrationEvent(
                announcementId,
                "维护通知")),
            CancellationToken.None);

        await publisher.Received(1).PublishToGroupAsync(
            RealtimeGroups.HostBroadcast,
            Arg.Any<RealtimeMessage>(),
            CancellationToken.None);
        Assert.HasCount(1, publishedMessages);
        var message = publishedMessages[0];
        Assert.AreEqual(RealtimeMessageCodes.AnnouncementPublished, message.Code);
        Assert.IsNotNull(message.Data);
        Assert.AreEqual(announcementId, message.Data["announcementId"]);
        Assert.AreEqual("维护通知", message.Data["title"]);
    }

    [TestMethod]
    public async Task Inbox_received_handler_queries_current_unread_count()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(7L);
        var publisher = Substitute.For<IRealtimePublisher>();
        var publishedMessages = new List<RealtimeMessage>();
        publisher
            .When(instance => instance.PublishToUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>()))
            .Do(call => publishedMessages.Add(call.ArgAt<RealtimeMessage>(1)));
        var serializer = new MessagePackIntegrationEventSerializer();
        var handler = new InboxMessageReceivedRealtimeHandler(
            serializer,
            new NotificationRealtimeDelivery(query, publisher));
        var recipientUserId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();

        await handler.HandleAsync(
            serializer.Serialize(new InboxMessageReceivedIntegrationEvent(
                recipientUserId,
                messageId,
                "新消息")),
            CancellationToken.None);

        await publisher.Received(2).PublishToUserAsync(
            recipientUserId,
            Arg.Any<RealtimeMessage>(),
            CancellationToken.None);
        Assert.HasCount(2, publishedMessages);
        var received = publishedMessages.Single(message =>
            message.Code == RealtimeMessageCodes.InboxMessageReceived);
        Assert.IsNotNull(received.Data);
        Assert.AreEqual(messageId, received.Data["messageId"]);
        Assert.AreEqual("新消息", received.Data["title"]);
        var unread = publishedMessages.Single(message =>
            message.Code == RealtimeMessageCodes.InboxUnreadCountChanged);
        Assert.IsNotNull(unread.Data);
        Assert.AreEqual(7L, unread.Data["unreadCount"]);
    }

    [TestMethod]
    public async Task Inbox_read_state_handler_publishes_only_current_unread_count()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(2L);
        var publisher = Substitute.For<IRealtimePublisher>();
        var publishedMessages = new List<RealtimeMessage>();
        publisher
            .When(instance => instance.PublishToUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>()))
            .Do(call => publishedMessages.Add(call.ArgAt<RealtimeMessage>(1)));
        var serializer = new MessagePackIntegrationEventSerializer();
        var handler = new InboxReadStateChangedRealtimeHandler(
            serializer,
            new NotificationRealtimeDelivery(query, publisher));
        var recipientUserId = Guid.CreateVersion7();

        await handler.HandleAsync(
            serializer.Serialize(new InboxReadStateChangedIntegrationEvent(
                recipientUserId)),
            CancellationToken.None);

        await publisher.Received(1).PublishToUserAsync(
            recipientUserId,
            Arg.Any<RealtimeMessage>(),
            CancellationToken.None);
        Assert.HasCount(1, publishedMessages);
        var message = publishedMessages[0];
        Assert.AreEqual(RealtimeMessageCodes.InboxUnreadCountChanged, message.Code);
        Assert.IsNotNull(message.Data);
        Assert.AreEqual(2L, message.Data["unreadCount"]);
    }

    [TestMethod]
    public async Task Handlers_propagate_realtime_publisher_failure()
    {
        var expected = new InvalidOperationException("模拟 Redis 发布失败。");
        var publisher = Substitute.For<IRealtimePublisher>();
        publisher.PublishToGroupAsync(
                Arg.Any<string>(),
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(expected));
        publisher.PublishToUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(expected));
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<long>(
                InboxMessageSql.CountUnreadForRecipient,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1L);
        var serializer = new MessagePackIntegrationEventSerializer();
        var delivery = new NotificationRealtimeDelivery(query, publisher);

        var announcement = new AnnouncementPublishedRealtimeHandler(
            serializer,
            delivery);
        var received = new InboxMessageReceivedRealtimeHandler(
            serializer,
            delivery);
        var readState = new InboxReadStateChangedRealtimeHandler(
            serializer,
            delivery);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            announcement.HandleAsync(
                serializer.Serialize(
                    new AnnouncementPublishedIntegrationEvent(
                        Guid.CreateVersion7(),
                        "维护通知")),
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            received.HandleAsync(
                serializer.Serialize(
                    new InboxMessageReceivedIntegrationEvent(
                        Guid.CreateVersion7(),
                        Guid.CreateVersion7(),
                        "新消息")),
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            readState.HandleAsync(
                serializer.Serialize(
                    new InboxReadStateChangedIntegrationEvent(
                        Guid.CreateVersion7())),
                CancellationToken.None));
    }

    private static NotificationRealtimeDelivery CreateDelivery(
        IRealtimePublisher publisher) =>
        new(Substitute.For<IQueryExecutor>(), publisher);
}
