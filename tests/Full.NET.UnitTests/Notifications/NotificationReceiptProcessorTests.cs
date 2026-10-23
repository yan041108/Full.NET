using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Features.ReceiveProviderReceipts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>单元测试（通知）：Notification Receipt Processor。</summary>
[TestClass]
public sealed class NotificationReceiptProcessorTests
{
    private const string ProviderTypeKey = "test.notification";

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Pending_apply_survives_repeated_CAS_conflicts_until_background_recovery(DatabaseProvider provider)
    {
        var now = DateTimeOffset.UtcNow;
        var delivery = new NotificationDeliveryRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "test", Guid.CreateVersion7(), null, "sent", 3, null, null, 1, null, now, null, null, null, null, null);
        var receipt = new NotificationReceiptRecord(Guid.CreateVersion7(), ProviderTypeKey, "recover-msg", "recover-key",
            delivery.Id, "delivered", "delivered", "digest", now, null, "pending_apply");
        var fixture = CreateFixture(now, new NotificationProviderTypeCatalog([new SignedTestProviderAdapter()]),
            new RecoveryQueries(provider, receipt, delivery));
        fixture.Command.ExecuteAsync(NotificationPlatformSql.ApplyDeliveryStatus, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(0, 0, 1);
        await fixture.Processor.RecoverPendingAsync(provider, CancellationToken.None);
        await fixture.Command.Received(1).ExecuteAsync(NotificationPlatformSql.UpdateReceiptProcessing,
            Arg.Is<object?>(p => ((IReadOnlyDictionary<string, object?>)p!)["ProcessStatusKey"]!.Equals("pending_apply")
                && ((IReadOnlyDictionary<string, object?>)p!)["ProcessedAtUtc"] == null), Arg.Any<CancellationToken>());
        await fixture.Processor.RecoverPendingAsync(provider, CancellationToken.None);
        await fixture.Command.Received(1).ExecuteAsync(NotificationPlatformSql.UpdateReceiptProcessing,
            Arg.Is<object?>(p => ((IReadOnlyDictionary<string, object?>)p!)["ProcessStatusKey"]!.Equals("completed")),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Duplicate_idempotency_key_returns_duplicate_without_status_change()
    {
        var existingId = Guid.CreateVersion7();
        var fixture = CreateFixture(
            DateTimeOffset.UtcNow,
            new NotificationProviderTypeCatalog([new SignedTestProviderAdapter()]));
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
                "completed"));

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
    public async Task Provider_without_receipt_capability_fails_closed()
    {
        var fixture = CreateFixture(
            DateTimeOffset.UtcNow,
            new NotificationProviderTypeCatalog([new NoReceiptEmailSmtpAdapter()]));
        var result = await fixture.Processor.ProcessAsync(
            "email.smtp",
            """{"receiptIdempotencyKey":"x"}"""u8.ToArray(),
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.ReceiptNotSupported, result.Error!.Code);
    }

    [TestMethod]
    public async Task Unknown_provider_type_fails_closed()
    {
        var fixture = CreateFixture(
            DateTimeOffset.UtcNow,
            new NotificationProviderTypeCatalog([]));
        var result = await fixture.Processor.ProcessAsync(
            "smtp.unknown",
            """{"receiptIdempotencyKey":"x"}"""u8.ToArray(),
            new Dictionary<string, string>(),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(NotificationsErrorCodes.ReceiptProviderUnknown, result.Error!.Code);
    }

    private static ProcessorFixture CreateFixture(
        DateTimeOffset now,
        NotificationProviderTypeCatalog catalog,
        IQueryExecutor? queryOverride = null)
    {
        var query = queryOverride ?? Substitute.For<IQueryExecutor>();
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
            catalog,
            [new StubReceiptVerifier()],
            clock,
            idGenerator,
            new NotificationQuotaGovernor(
                query, command, transaction, clock, idGenerator, Options.Create(new DatabaseOptions())));
        return new ProcessorFixture(query, command, processor);
    }

    private sealed record ProcessorFixture(
        IQueryExecutor Query,
        ICommandExecutor Command,
        NotificationReceiptProcessor Processor);

    /// <summary>显式查询替身避免动态代理内部持久化投影，并校验双库补偿选择。</summary>
    private sealed class RecoveryQueries(DatabaseProvider provider, NotificationReceiptRecord receipt,
        NotificationDeliveryRecord delivery) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(NotificationReceiptRecord))
            {
                Assert.AreEqual(provider == DatabaseProvider.MySql ? NotificationLifecycleSql.RecoverableReceiptsMySql
                    : NotificationLifecycleSql.RecoverableReceiptsSqlServer, statement);
                return Task.FromResult((IReadOnlyList<T>)(object)new[] { receipt });
            }
            if (typeof(T) == typeof(NotificationDeliveryRecord))
                return Task.FromResult((IReadOnlyList<T>)(object)new[] { delivery });
            if (typeof(T) == typeof(NotificationQuotaReservationRecord))
                return Task.FromResult((IReadOnlyList<T>)(object)Array.Empty<NotificationQuotaReservationRecord>());
            throw new InvalidOperationException("补偿查询超出预期边界。");
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((T?)(object)(delivery with { Revision = 4 }));
    }

    private sealed class SignedTestProviderAdapter : INotificationProviderAdapter
    {
        public string? RecipientEndpointKindKey => null;

        // 回执处理用例不涉及发送，按失败关闭默认不放开未知结果自动重试。
        public bool AllowsAutomaticRetryAfterUnknownResult => false;

        public NotificationProviderTypeDescriptor Descriptor { get; } = new(
            ProviderTypeKey,
            "1.0.0",
            ["test"],
            [],
            [],
            true,
            NotificationReceiptModeKeys.Signed);

        public ValueTask<NotificationProviderResult> SendAsync(
            NotificationProviderRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Receipt processor tests must not send.");
    }

    private sealed class NoReceiptEmailSmtpAdapter : INotificationProviderAdapter
    {
        public string? RecipientEndpointKindKey => "email";

        // 回执处理用例不涉及发送，按失败关闭默认不放开未知结果自动重试。
        public bool AllowsAutomaticRetryAfterUnknownResult => false;

        public NotificationProviderTypeDescriptor Descriptor { get; } = new(
            "email.smtp",
            "1.0.0",
            ["email"],
            [],
            ["password"],
            true,
            NotificationReceiptModeKeys.None);

        public ValueTask<NotificationProviderResult> SendAsync(
            NotificationProviderRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Receipt processor tests must not send.");
    }

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
