using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Features.ReadIntentDelivery;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>消费方只得到状态统计，所有者负责可信作用域及来源一致性。</summary>
[TestClass]
public sealed class NotificationIntentDeliveryDirectoryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Accepted_intent_returns_only_delivery_summary_in_trusted_scope(bool host)
    {
        var f = new Fixture(host);
        using var cancellation = new CancellationTokenSource();
        var result = await f.Service.FindByIdempotencyAsync("workflow", "workflow-result", cancellation.Token);
        Assert.IsNotNull(result);
        Assert.AreEqual(f.Row.Id, result.IntentId);
        Assert.AreEqual(9, result.TotalDeliveryCount);
        Assert.AreEqual(3, result.PendingDeliveryCount);
        Assert.AreEqual(2, result.SentDeliveryCount);
        Assert.AreEqual(1, result.FailedDeliveryCount);
        Assert.AreEqual(1, result.DeadLetteredDeliveryCount);
        Assert.AreEqual(1, result.UnknownDeliveryCount);
        Assert.AreEqual(1, result.OtherDeliveryCount);
        Assert.AreEqual(f.Row.NextAttemptAtUtc, result.NextAttemptAtUtc);
        await f.Queries.Received(1).QuerySingleOrDefaultAsync<NotificationIntentDeliveryRecord>(
            NotificationIntentDeliverySql.FindByIdempotency,
            Arg.Is<object?>(value => MatchesScope(value, host ? "host" : $"tenant:{f.TenantId:N}")), cancellation.Token);
        Assert.AreEqual(1, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Actual_context_accessor_accepts_resolved_scope_and_rejects_cleared_scope(bool host)
    {
        var f = new Fixture(host);
        var actual = new CurrentTenantAccessor();
        var writer = (ICurrentTenantContextWriter)actual;
        if (host) writer.SetHost();
        else writer.SetTenant(new TenantContext(f.TenantId, "progress", "通知进度测试"));
        var directory = new NotificationIntentDeliveryDirectory(f.Queries, actual);
        var result = await directory.FindByIdempotencyAsync("workflow", "workflow-result");
        Assert.IsNotNull(result); Assert.AreEqual(f.Row.Id, result.IntentId);
        await f.Queries.Received(1).QuerySingleOrDefaultAsync<NotificationIntentDeliveryRecord>(
            NotificationIntentDeliverySql.FindByIdempotency,
            Arg.Is<object?>(value => MatchesScope(value, host ? "host" : $"tenant:{f.TenantId:N}")), CancellationToken.None);
        f.Queries.ClearReceivedCalls();
        writer.Clear();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => directory.FindByIdempotencyAsync("workflow", "workflow-result"));
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Known_delivery_receipts_are_not_collapsed_into_other_states()
    {
        var f = new Fixture(); f.Returned = f.Row with { TotalDeliveryCount = 13, PersistedDeliveryCount = 1,
            DeliveredDeliveryCount = 1, ReadDeliveryCount = 1, SuppressedDeliveryCount = 1 };
        var result = await f.Service.FindByIdempotencyAsync("workflow", "workflow-result");
        Assert.IsNotNull(result); Assert.AreEqual(13, result.TotalDeliveryCount);
        Assert.AreEqual(1, result.PersistedDeliveryCount); Assert.AreEqual(1, result.DeliveredDeliveryCount);
        Assert.AreEqual(1, result.ReadDeliveryCount); Assert.AreEqual(1, result.SuppressedDeliveryCount);
        Assert.AreEqual(1, result.OtherDeliveryCount);
    }

    [TestMethod]
    public async Task No_pending_delivery_cannot_report_a_scheduled_attempt()
    {
        var f = new Fixture(); f.Returned = f.Row with { TotalDeliveryCount = 6, PendingDeliveryCount = 0 };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.FindByIdempotencyAsync("workflow", "workflow-result"));
    }

    [TestMethod]
    public async Task Missing_intent_is_not_reported_as_sent_or_retrying()
    {
        var f = new Fixture(); f.Returned = null;
        Assert.IsNull(await f.Service.FindByIdempotencyAsync("workflow", "workflow-result"));
        Assert.AreEqual(1, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("unavailable")]
    [DataRow("empty")]
    [DataRow("host-with-tenant")]
    public async Task Invalid_trusted_context_fails_before_query(string kind)
    {
        var f = new Fixture();
        if (kind == "unavailable") f.Tenant.IsAvailable.Returns(false);
        if (kind == "empty") f.Tenant.Id.Returns(Guid.Empty);
        if (kind == "host-with-tenant") f.Tenant.IsHost.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.FindByIdempotencyAsync("workflow", "workflow-result"));
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("scope")]
    [DataRow("producer")]
    [DataRow("key")]
    [DataRow("id")]
    [DataRow("status")]
    [DataRow("counts")]
    [DataRow("negative")]
    public async Task Inconsistent_owner_snapshot_is_rejected(string kind)
    {
        var f = new Fixture();
        f.Returned = kind switch
        {
            "scope" => f.Row with { TenantScopeKey = "host" },
            "producer" => f.Row with { ProducerKey = "other" },
            "key" => f.Row with { IdempotencyKey = "other" },
            "id" => f.Row with { Id = Guid.Empty },
            "status" => f.Row with { StatusKey = "draft" },
            "counts" => f.Row with { TotalDeliveryCount = 8 },
            _ => f.Row with { PendingDeliveryCount = -1, TotalDeliveryCount = 5 }
        };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.FindByIdempotencyAsync("workflow", "workflow-result"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("white space")]
    [DataRow("bad/key")]
    [DataRow("bad\\key")]
    [DataRow("bad\u0001key")]
    public async Task Invalid_key_never_queries(string key)
    {
        var f = new Fixture();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Service.FindByIdempotencyAsync("workflow", key));
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Caller_cancellation_is_preserved()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Service.FindByIdempotencyAsync("workflow", "workflow-result", cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Query_failure_is_not_converted_to_absent_or_sent()
    {
        var f = new Fixture(); var expected = new InvalidOperationException("database unavailable");
        f.Queries.QuerySingleOrDefaultAsync<NotificationIntentDeliveryRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<NotificationIntentDeliveryRecord?>(expected));
        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.FindByIdempotencyAsync("workflow", "workflow-result"));
        Assert.AreSame(expected, actual);
    }

    [TestMethod]
    public async Task Inbox_only_acceptance_has_zero_external_deliveries()
    {
        var f = new Fixture(); f.Returned = f.Row with
        { TotalDeliveryCount = 0, PendingDeliveryCount = 0, SentDeliveryCount = 0, FailedDeliveryCount = 0,
          DeadLetteredDeliveryCount = 0, UnknownDeliveryCount = 0, OtherDeliveryCount = 0, NextAttemptAtUtc = null };
        var result = await f.Service.FindByIdempotencyAsync("workflow", "workflow-result");
        Assert.IsNotNull(result); Assert.AreEqual(0, result.TotalDeliveryCount);
    }

    private static bool MatchesScope(object? value, string scope) => value is Dictionary<string, object?> parameters
        && Equals(parameters["TenantScopeKey"], scope) && Equals(parameters["ProducerKey"], "workflow")
        && Equals(parameters["IdempotencyKey"], "workflow-result");

    private sealed class Fixture
    {
        internal readonly Guid TenantId = Guid.NewGuid();
        internal readonly ICurrentTenant Tenant = Substitute.For<ICurrentTenant>();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly NotificationIntentDeliveryRecord Row;
        internal NotificationIntentDeliveryRecord? Returned;
        internal readonly NotificationIntentDeliveryDirectory Service;
        internal Fixture(bool host = false)
        {
            Tenant.IsHost.Returns(host); Tenant.IsAvailable.Returns(true); Tenant.Id.Returns(host ? null : TenantId);
            Row = new(Guid.NewGuid(), host ? "host" : $"tenant:{TenantId:N}", "workflow", "workflow-result", "accepted",
                DateTimeOffset.UtcNow, 9, 3, 2, 1, 1, 1, 1, DateTimeOffset.UtcNow.AddMinutes(1));
            Returned = Row;
            Queries.QuerySingleOrDefaultAsync<NotificationIntentDeliveryRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Returned);
            Service = new(Queries, Tenant);
        }
    }
}
