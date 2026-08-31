using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ProjectInboxFromIntent;
using Full.NET.Modules.Notifications.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class InboxIntentProjectionServiceTests
{
    [TestMethod]
    public async Task Duplicate_intent_recipient_returns_existing_inbox_without_insert()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var transaction = new RecordingTransaction();
        var directory = Substitute.For<IHostUserDirectory>();
        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.IsHost.Returns(true);
        currentTenant.IsAvailable.Returns(true);
        var recipientUserId = Guid.CreateVersion7();
        var intentId = Guid.CreateVersion7();
        var existingId = Guid.CreateVersion7();
        directory.FindActiveHostUserAsync(recipientUserId, Arg.Any<CancellationToken>())
            .Returns(new HostUserDirectoryEntry(recipientUserId, "user", "用户"));
        query.QuerySingleOrDefaultAsync<InboxMessageRecord>(
                InboxMessageSql.FindByIntentRecipient,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new InboxMessageRecord
            {
                Id = existingId,
                RecipientUserId = recipientUserId,
                Title = "已存在",
                Content = "正文",
                Status = InboxMessageStatuses.Unread,
                TenantScopeKey = "host",
                IntentId = intentId,
            });
        var service = new InboxIntentProjectionService(
            query,
            command,
            transaction,
            directory,
            currentTenant,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var result = await service.ProjectAsync(
            Guid.CreateVersion7(),
            intentId,
            recipientUserId,
            "新标题",
            "新正文");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(existingId, result.Value!.Id);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            await action(cancellationToken);
    }
}
