using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageOrders;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Serialization;
using NSubstitute;

namespace Full.NET.UnitTests.Payments;

/// <summary>原创建身份读取只允许 Host、绑定租户和请求键，不查询渠道或推进意图。</summary>
[TestClass]
public sealed class PaymentOrderCreationQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Lookup_returns_persisted_identity_exact_amount_and_server_read_time()
    {
        var tenant = Guid.CreateVersion7(); var key = Guid.CreateVersion7(); var row = Row(tenant, key); var fixture = Create(row);
        var result = await fixture.Service.GetAsync(tenant, key, default); Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(key, result.Value!.IdempotencyKey); Assert.AreEqual(row.Id, result.Value.Order.Id); Assert.AreEqual(Now, result.Value.ReadAtUtc);
        await fixture.Queries.Received(1).QuerySingleOrDefaultAsync<PaymentOrderRecord>(PaymentOrderSql.FindByIdempotencyKey,
            Arg.Is<IReadOnlyDictionary<string, object?>>(p => p != null && (Guid)p["TenantId"]! == tenant && (Guid)p["IdempotencyKey"]! == key), Arg.Any<CancellationToken>());
        var context = new PaymentsJsonSerializerContext(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, context.PaymentOrderCreationLookupResponse));
        Assert.AreEqual("9223372036854775807", json.RootElement.GetProperty("order").GetProperty("amountMinor").GetString());
        Assert.AreEqual(key, json.RootElement.GetProperty("idempotencyKey").GetGuid());
    }

    [TestMethod]
    public async Task Tenant_context_is_denied_before_query_even_with_a_known_creation_key()
    {
        var fixture = Create(Row(Guid.CreateVersion7(), Guid.CreateVersion7()), host: false);
        var result = await fixture.Service.GetAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), default);
        Assert.AreEqual(PaymentErrorCodes.OrderCreationLookupForbidden, result.Error!.Code);
        await NoQuery(fixture.Queries);
    }

    [TestMethod] [DataRow(true)] [DataRow(false)]
    public async Task Empty_identity_never_reaches_assigned_guid_binding(bool emptyTenant)
    {
        var fixture = Create(null);
        var result = await fixture.Service.GetAsync(emptyTenant ? Guid.Empty : Guid.CreateVersion7(), emptyTenant ? Guid.CreateVersion7() : Guid.Empty, default);
        Assert.AreEqual(PaymentErrorCodes.OrderCreationRequestNotFound, result.Error!.Code); await NoQuery(fixture.Queries);
    }

    [TestMethod] [DataRow("none")] [DataRow("tenant")] [DataRow("key")]
    public async Task Missing_or_mismatched_record_never_leaks_another_creation(string mismatch)
    {
        var tenant = Guid.CreateVersion7(); var key = Guid.CreateVersion7();
        var row = mismatch == "none" ? null : Row(mismatch == "tenant" ? Guid.CreateVersion7() : tenant, mismatch == "key" ? Guid.CreateVersion7() : key);
        Assert.AreEqual(PaymentErrorCodes.OrderCreationRequestNotFound, (await Create(row).Service.GetAsync(tenant, key, default)).Error!.Code);
    }

    private static Task NoQuery(IQueryExecutor queries) => queries.DidNotReceive().QuerySingleOrDefaultAsync<PaymentOrderRecord>(
        Arg.Any<SqlStatement>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>());
    private static PaymentOrderRecord Row(Guid tenant, Guid key) => new() { Id = Guid.CreateVersion7(), TenantId = tenant, IdempotencyKey = key,
        MerchantConfigId = Guid.CreateVersion7(), AmountMinor = long.MaxValue, Currency = "CNY", OutTradeNo = "original", TradeStateKey = "provider_unknown", CreatedAtUtc = Now.AddDays(-1), Version = 7 };
    private static (PaymentOrderCreationQueryService Service, IQueryExecutor Queries) Create(PaymentOrderRecord? row, bool host = true)
    {
        var queries = Substitute.For<IQueryExecutor>(); var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(host);
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(Now);
        queries.QuerySingleOrDefaultAsync<PaymentOrderRecord>(PaymentOrderSql.FindByIdempotencyKey, Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult(row));
        return (new(queries, tenant, clock), queries);
    }
}
