using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageHostAnnouncements;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Organization.Contracts;
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
        var innerQuery = Substitute.For<IQueryExecutor>();
        var query = new AnnouncementTestQueryExecutor(innerQuery);
        var command = Substitute.For<ICommandExecutor>();
        var publisher = Substitute.For<IRealtimePublisher>();
        var outboxWriter = Substitute.For<IOutboxWriter>();
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
        innerQuery.QuerySingleOrDefaultAsync<AnnouncementRecord>(
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
        publisher.PublishToHostBroadcastAsync(
                Arg.Any<RealtimeMessage>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsFalse(transaction.IsActive);
                throw new OperationCanceledException("simulated publisher cancellation");
            });
        outboxWriter.AddAsync(
                NotificationRealtimeEventTypes.AnnouncementPublished,
                1,
                Arg.Any<AnnouncementPublishedIntegrationEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsTrue(transaction.IsActive);
                return Task.CompletedTask;
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
            outboxWriter,
            queries,
            CreateAudienceValidator(),
            new NotificationRealtimeDelivery(query, publisher),
            clock,
            idGenerator,
            NullLogger<HostAnnouncementManagementService>.Instance);

        var result = await service.PublishAsync(
            actorUserId,
            announcementId,
            1);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AnnouncementStatuses.Published, result.Value?.Status);
        await outboxWriter.Received(1).AddAsync(
            NotificationRealtimeEventTypes.AnnouncementPublished,
            1,
            Arg.Is<AnnouncementPublishedIntegrationEvent>(integrationEvent =>
                integrationEvent != null
                && integrationEvent.AnnouncementId == announcementId
                && integrationEvent.Title == published.Title),
            Arg.Any<CancellationToken>());
        await publisher.Received(1).PublishToHostBroadcastAsync(
            Arg.Any<RealtimeMessage>(),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task Publish_is_idempotent_when_announcement_is_already_published()
    {
        var announcementId = Guid.CreateVersion7();
        var actorUserId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var transaction = new RecordingTransaction();
        var innerQuery = Substitute.For<IQueryExecutor>();
        var query = new AnnouncementTestQueryExecutor(innerQuery);
        var command = Substitute.For<ICommandExecutor>();
        var published = CreateRecord(
            announcementId,
            AnnouncementStatuses.Published,
            2,
            now,
            now);
        innerQuery.QuerySingleOrDefaultAsync<AnnouncementRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(published);
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
            Substitute.For<IOutboxWriter>(),
            queries,
            CreateAudienceValidator(),
            new NotificationRealtimeDelivery(query, Substitute.For<IRealtimePublisher>()),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(),
            NullLogger<HostAnnouncementManagementService>.Instance);

        var result = await service.PublishAsync(actorUserId, announcementId, 2);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AnnouncementStatuses.Published, result.Value?.Status);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Is(AnnouncementSql.Publish),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Retract_is_idempotent_when_announcement_is_already_retracted()
    {
        var announcementId = Guid.CreateVersion7();
        var actorUserId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var transaction = new RecordingTransaction();
        var innerQuery = Substitute.For<IQueryExecutor>();
        var query = new AnnouncementTestQueryExecutor(innerQuery);
        var command = Substitute.For<ICommandExecutor>();
        var retracted = CreateRecord(
            announcementId,
            AnnouncementStatuses.Retracted,
            3,
            now,
            now);
        innerQuery.QuerySingleOrDefaultAsync<AnnouncementRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(retracted);
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
            Substitute.For<IOutboxWriter>(),
            queries,
            CreateAudienceValidator(),
            new NotificationRealtimeDelivery(query, Substitute.For<IRealtimePublisher>()),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(),
            NullLogger<HostAnnouncementManagementService>.Instance);

        var result = await service.RetractAsync(actorUserId, announcementId, 3);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AnnouncementStatuses.Retracted, result.Value?.Status);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Is(AnnouncementSql.Retract),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private static HostAnnouncementAudienceValidator CreateAudienceValidator()
    {
        var hostUsers = Substitute.For<IHostUserDirectory>();
        hostUsers.FindActiveHostUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new HostUserDirectoryEntry(Guid.CreateVersion7(), "demo", "Demo"));
        var organizations = Substitute.For<ITenantOrganizationUnitDirectory>();
        organizations.FindActiveUnitAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(new TenantOrganizationUnitDirectoryEntry(Guid.CreateVersion7(), "root", "Root"));
        return new HostAnnouncementAudienceValidator(hostUsers, organizations);
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
            Kind = AnnouncementKinds.Announcement,
            AudienceKind = AnnouncementAudienceKinds.All,
            Status = status,
            PublishedAtUtc = publishedAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = publishedAtUtc,
            CreatedByUserId = Guid.CreateVersion7(),
            Version = version,
        };

    private sealed class AnnouncementTestQueryExecutor(IQueryExecutor inner) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(AnnouncementTargetUserRecord)
                || typeof(T) == typeof(AnnouncementTargetOrganizationRecord))
            {
                return Task.FromResult<IReadOnlyList<T>>([]);
            }

            return inner.QueryAsync<T>(statement, parameters, cancellationToken);
        }
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
