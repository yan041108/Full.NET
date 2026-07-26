using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Realtime;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class HostAnnouncementManagementServiceTests
{
    [TestMethod]
    public async Task Publish_keeps_committed_result_when_realtime_publisher_is_cancelled()
    {
        var announcementId = Guid.CreateVersion7();
        var actorUserId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var transaction = new RecordingTransaction();
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var publisher = Substitute.For<IRealtimePublisher>();
        var clock = Substitute.For<IClock>();
        var idGenerator = Substitute.For<IIdGenerator>();
        var draft = CreateRecord(
            announcementId,
            AnnouncementStatuses.Draft,
            1,
            now,
            publishedAtUtc: null);
        var published = CreateRecord(
            announcementId,
            AnnouncementStatuses.Published,
            2,
            now,
            now);
        query.QuerySingleOrDefaultAsync<AnnouncementRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(draft, published);
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        clock.UtcNow.Returns(now);
        publisher.PublishToGroupAsync(
                Arg.Any<string>(),
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsFalse(transaction.IsActive);
                throw new OperationCanceledException("simulated publisher cancellation");
            });
        var queries = new HostAnnouncementQueryService(
            query,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = "Server=(local);Database=unused",
            }));
        var service = new HostAnnouncementManagementService(
            query,
            command,
            transaction,
            queries,
            publisher,
            clock,
            idGenerator,
            NullLogger<HostAnnouncementManagementService>.Instance);

        var result = await service.PublishAsync(
            actorUserId,
            announcementId,
            1);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AnnouncementStatuses.Published, result.Value?.Status);
        await publisher.Received(1).PublishToGroupAsync(
            RealtimeGroups.HostBroadcast,
            Arg.Any<RealtimeMessage>(),
            CancellationToken.None);
    }

    private static AnnouncementRecord CreateRecord(
        Guid id,
        string status,
        int version,
        DateTimeOffset now,
        DateTimeOffset? publishedAtUtc) =>
        new()
        {
            Id = id,
            Title = "maintenance",
            Content = "scheduled maintenance",
            Status = status,
            PublishedAtUtc = publishedAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = publishedAtUtc,
            CreatedByUserId = Guid.CreateVersion7(),
            Version = version,
        };

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
