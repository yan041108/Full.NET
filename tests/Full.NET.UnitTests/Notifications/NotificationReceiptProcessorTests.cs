using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ReceiveProviderReceipts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationReceiptProcessorTests
{
    private const string ProviderTypeKey = "test.notification";

    [TestMethod]
    public async Task Duplicate_idempotency_key_returns_duplicate_without_status_change()
    {
        var existingId = Guid.CreateVersion7();
        var fixture = CreateFixture(DateTimeOffset.UtcNow);
        fixture.Query.QuerySingleOrDefaultAsync<NotificationReceiptRecord>(
                NotificationPlatformSql.FindReceiptByIdempotency,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new NotificationReceiptRecord(
                existingId,
                ProviderTypeKey,
                "msg-1",
                "dup-key",
                Guid.CreateVersion7(),
                "delivered",
                "delivered",
                "digest",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "processed"));

        var body = """{"receiptIdempotencyKey":"dup-key","providerMessageId":"msg-1","externalStatusKey":"delivered","mappedStatusKey":"delivered"}"""u8.ToArray();
        var result = await fixture.Processor.ProcessAsync(
            ProviderTypeKey,
            body,
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("duplicate", result.Value!.ProcessStatusKey);
        Assert.AreEqual(existingId, result.Value.Id);
        await fixture.Command.DidNotReceive().ExecuteAsync(
            NotificationPlatformSql.ApplyDeliveryStatus,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Unknown_provider_type_fails_closed()
    {
        var fixture = CreateFixture(DateTimeOffset.UtcNow);
        var result = await fixture.Processor.ProcessAsync(
            "smtp.unknown",
            """{"receiptIdempotencyKey":"x"}"""u8.ToArray(),
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.ReceiptProviderUnknown, result.Error!.Code);
    }

    private static ProcessorFixture CreateFixture(DateTimeOffset now)
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var transaction = new RecordingTransaction();
        var clock = Substitute.For<IClock>();
        var idGenerator = Substitute.For<IIdGenerator>();
        clock.UtcNow.Returns(now);
        idGenerator.NewId().Returns(Guid.CreateVersion7());
        var processor = new NotificationReceiptProcessor(
            query,
            command,
            transaction,
            [new StubReceiptVerifier()],
            clock,
            idGenerator);
        return new ProcessorFixture(query, command, processor);
    }

    private sealed record ProcessorFixture(
        IQueryExecutor Query,
        ICommandExecutor Command,
        NotificationReceiptProcessor Processor);

    private sealed class StubReceiptVerifier : INotificationReceiptVerifier
    {
        public string ProviderTypeKey => "test.notification";

        public Result<VerifiedNotificationReceipt> Verify(
            ReadOnlyMemory<byte> body,
            IReadOnlyDictionary<string, string> headers)
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var root = document.RootElement;
            return Result<VerifiedNotificationReceipt>.Success(
                new VerifiedNotificationReceipt(
                    root.GetProperty("receiptIdempotencyKey").GetString()!,
                    root.TryGetProperty("providerMessageId", out var message)
                        ? message.GetString()
                        : null,
                    root.GetProperty("externalStatusKey").GetString()!,
                    root.GetProperty("mappedStatusKey").GetString()!,
                    "digest"));
        }
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public Task<Result<T>> ExecuteResultAsync<T>(
            Func<CancellationToken, Task<Result<T>>> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }
}
