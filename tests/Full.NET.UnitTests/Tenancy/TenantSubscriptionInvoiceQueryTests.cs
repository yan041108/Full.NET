using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Features.SubscriptionBilling;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>历史回读保持冻结原值和整数精度，不借当前报价或付款资格改写历史。</summary>
[TestClass]
public sealed class TenantSubscriptionInvoiceQueryTests
{
    [TestMethod]
    [DataRow("Pending")]
    [DataRow("Applied")]
    [DataRow("Refunded")]
    public async Task Every_invoice_phase_returns_original_terms_with_exact_int64_strings(string phase)
    {
        var queries = Substitute.For<IQueryExecutor>(); var clock = Substitute.For<IClock>();
        var now = DateTimeOffset.UtcNow; clock.UtcNow.Returns(now);
        var row = Invoice(phase, now);
        queries.QuerySingleOrDefaultAsync<SubscriptionInvoiceRecord>(SubscriptionInvoiceSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        var result = await new TenantSubscriptionInvoiceQueryService(queries, clock).GetAsync(row.TenantId, row.Id, default);
        Assert.IsTrue(result.IsSuccess); var value = result.Value!;
        Assert.AreEqual("9223372036854775807", value.AmountMinor); Assert.AreEqual("9007199254740993", value.SeatLimit);
        Assert.AreEqual("9223372036854775806", value.StorageLimit); Assert.AreEqual("Original product", value.Subject);
        Assert.AreEqual(row.EffectiveFromUtc, value.EffectiveFromUtc); Assert.AreEqual(row.EffectiveToUtc, value.EffectiveToUtc);
        Assert.AreEqual(phase, value.Phase); Assert.AreEqual(row.RenewalOfSubscriptionId, value.RenewalOfSubscriptionId);
        Assert.AreEqual(row.RenewalSourceVersion, value.RenewalSourceVersion); Assert.AreEqual(now, value.ReadAtUtc);
        await queries.Received(1).QuerySingleOrDefaultAsync<SubscriptionInvoiceRecord>(SubscriptionInvoiceSql.Find,
            Arg.Is<object?>(parameters => parameters is Dictionary<string, object?>
                && (Guid)((Dictionary<string, object?>)parameters)["TenantId"]! == row.TenantId
                && (Guid)((Dictionary<string, object?>)parameters)["Id"]! == row.Id), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Ordinary_invoice_keeps_both_source_fields_absent()
    {
        var queries = Substitute.For<IQueryExecutor>(); var clock = Substitute.For<IClock>();
        var row = Invoice("Applied", DateTimeOffset.UtcNow) with { RenewalOfSubscriptionId = null, RenewalSourceVersion = null };
        queries.QuerySingleOrDefaultAsync<SubscriptionInvoiceRecord>(SubscriptionInvoiceSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        var result = await new TenantSubscriptionInvoiceQueryService(queries, clock).GetAsync(row.TenantId, row.Id, default);
        Assert.IsNull(result.Value!.RenewalOfSubscriptionId); Assert.IsNull(result.Value.RenewalSourceVersion);
    }

    [TestMethod]
    public async Task Missing_composite_identity_returns_not_found()
    {
        var queries = Substitute.For<IQueryExecutor>(); var clock = Substitute.For<IClock>();
        var result = await new TenantSubscriptionInvoiceQueryService(queries, clock).GetAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ErrorType.NotFound, result.Error!.Type);
        Assert.AreEqual("tenancy.subscription_billing.not_found", result.Error.Code);
    }

    private static SubscriptionInvoiceRecord Invoice(string phase, DateTimeOffset now) => new(Guid.CreateVersion7(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), "not-for-transport", Guid.CreateVersion7(), 1, 3, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), "provider-transaction", long.MaxValue, "CNY", 9007199254740993L, long.MaxValue - 1,
        now.AddDays(-60), now.AddDays(-30), phase, null, 0, now.AddDays(-61), now, 1, "Original product", Guid.CreateVersion7(), 7);
}
