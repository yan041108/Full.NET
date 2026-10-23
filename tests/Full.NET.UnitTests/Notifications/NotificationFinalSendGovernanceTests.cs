using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Features.IntentAttachments;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>耗时准备或配额等待之后撤销许可，必须在厂商调用前停止并释放确定未发送的预留。</summary>
[TestClass]
public sealed class NotificationFinalSendGovernanceTests
{
    [TestMethod]
    [DataRow("attachments", "consent")]
    [DataRow("quota", "consent")]
    [DataRow("attachments", "channel")]
    [DataRow("quota", "quiet")]
    [DataRow("attachments", "catalog")]
    [DataRow("quota", "catalog")]
    public async Task Revocation_after_initial_policy_check_blocks_send_and_releases_quota(
        string boundary, string change)
    {
        var query = new GovernanceQueries(boundary, change);
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(query.Now);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var transaction = new ImmediateTransaction();
        var adapter = new CapturingAdapter();
        var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        var governor = new NotificationQuotaGovernor(query, command, transaction, clock, ids, options);
        var processor = new NotificationDeliveryBatchProcessor(query, command, transaction, [adapter],
            new NotificationAttachmentLoader(query, null!),
            new NotificationIntentAttachmentCoordinator(null!, null!, query, command, clock, ids),
            clock, ids, options,
            Options.Create(new NotificationDeliveryWorkerOptions { BatchSize = 1, MaxConcurrency = 1 }),
            new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider()),
            governor, NullLogger<NotificationDeliveryBatchProcessor>.Instance, null!);

        Assert.AreEqual(1, await processor.ProcessPendingAsync(CancellationToken.None));
        Assert.IsTrue(query.Changed, "替身必须在初次许可检查之后改变权威状态。");
        Assert.AreEqual(0, adapter.Calls);
        await command.Received(1).ExecuteAsync(NotificationPlatformSql.ReleaseQuotaCounter,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(NotificationPlatformSql.ConsumeQuotaCounter,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        var expectedStatus = change switch
        {
            "quiet" => "accepted",
            "catalog" => "failed",
            _ => "suppressed",
        };
        await command.Received(1).ExecuteAsync(NotificationPlatformSql.CompleteDelivery,
            Arg.Is<object?>(value => ((IReadOnlyDictionary<string, object?>)value!)["StatusKey"]!.Equals(expectedStatus)),
            Arg.Any<CancellationToken>());
        if (change == "quiet")
        {
            await command.Received(1).ExecuteAsync(NotificationPlatformSql.RefundGovernanceAttemptBudget,
                Arg.Any<object?>(), Arg.Any<CancellationToken>());
            await command.DidNotReceive().ExecuteAsync(NotificationPlatformSql.InsertAttempt,
                Arg.Any<object?>(), Arg.Any<CancellationToken>());
        }
    }

    /// <summary>显式行投影替身在附件或配额边界变更许可，避免单测仅验证入队前的政策求值。</summary>
    private sealed class GovernanceQueries(string boundary, string change) : IQueryExecutor
    {
        internal DateTimeOffset Now { get; } = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        internal bool Changed;
        private readonly Guid _id = Guid.CreateVersion7();

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            object? row = null;
            if (statement == NotificationPlatformSql.LockDeliveryForQuotaSqlServer)
            {
                if (boundary == "quota") Changed = true;
                row = _id;
            }
            else if (typeof(T) == typeof(long)) row = 1L;
            else if (typeof(T) == typeof(int)) row = 1;
            else if (typeof(T) == typeof(NotificationProviderProfileVersionRecord))
                row = new NotificationProviderProfileVersionRecord(_id, _id, 1, "test.final", "1", "{}", null, "hash", _id, Now);
            else if (typeof(T) == typeof(NotificationIntentRecord))
                row = new NotificationIntentRecord(_id, null, "host", "host", "producer", "scene", "key",
                    _id, null, "marketing", "all", "{}", "{}", "accepted", _id, Now, 1);
            else if (typeof(T) == typeof(NotificationRecipientRecord))
                row = new NotificationRecipientRecord(_id, _id, "user", "user", _id, null, "resolved", Now);
            else if (typeof(T) == typeof(NotificationTemplateVersionRecord))
                row = new NotificationTemplateVersionRecord(_id, _id, "en-US", 1, 1, "subject", "{}", "{}",
                    "ordinary", "hash", _id, Now);
            else if (typeof(T) == typeof(NotificationProducerSceneRecord))
                row = new NotificationProducerSceneRecord(_id, null, "host", "host", "producer", "scene",
                    "marketing", """["email"]""", false, !(Changed && change == "catalog"), _id, Now, Now, 1);
            else if (typeof(T) == typeof(NotificationPreferenceRecord))
                row = new NotificationPreferenceRecord(_id, null, "host", "host", _id, "email",
                    Changed && change == "channel", !(Changed && change == "consent"),
                    Changed && change == "quiet" ? """{"timeZone":"UTC","start":"11:00","end":"13:00"}""" : null, Now, 1);
            return Task.FromResult(row is null ? default : (T?)row);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(NotificationDeliveryRecord))
                return Rows<T>(new NotificationDeliveryRecord(_id, _id, _id, "email", _id, null,
                    "accepted", 1, "worker", Now.AddMinutes(1), 1, Now, Now, null,
                    """{"subject":"frozen","body":"frozen"}""", 1, "dispatch", null));
            if (typeof(T) == typeof(NotificationIntentAttachmentRecord))
            {
                if (boundary == "attachments") Changed = true;
                return Task.FromResult<IReadOnlyList<T>>([]);
            }
            if (typeof(T) == typeof(NotificationQuotaReservationRecord))
                return Rows<T>(new NotificationQuotaReservationRecord(_id, _id, "provider", Now));
            throw new InvalidOperationException($"不应执行查询：{statement.Name}");
        }

        private static Task<IReadOnlyList<T>> Rows<T>(object row) =>
            Task.FromResult<IReadOnlyList<T>>([(T)row]);
    }

    private sealed class CapturingAdapter : INotificationProviderAdapter
    {
        internal int Calls;
        public NotificationProviderTypeDescriptor Descriptor { get; } = new("test.final", "1", ["email"], [], [], true, "none");
        public string? RecipientEndpointKindKey => null;
        public bool AllowsAutomaticRetryAfterUnknownResult => false;
        public ValueTask<NotificationProviderResult> SendAsync(NotificationProviderRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new NotificationProviderResult(true, NotificationDeliveryRetry.Succeeded, null, null));
        }
    }

    /// <summary>仅验证调用顺序与收敛命令；行锁、原子回滚和真实配额由双库集成测试覆盖。</summary>
    private sealed class ImmediateTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) => action(cancellationToken);
        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default) => action(cancellationToken);
        public Task<Result<T>> ExecuteResultAsync<T>(Func<CancellationToken, Task<Result<T>>> action, CancellationToken cancellationToken = default) => action(cancellationToken);
    }
}
