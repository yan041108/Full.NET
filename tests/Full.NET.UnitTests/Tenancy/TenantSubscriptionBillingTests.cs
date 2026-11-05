using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Tenancy.Auditing;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using Full.NET.Modules.Tenancy.Features.SubscriptionBilling;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>资金归属必须逐项匹配；失败结果和审计故障不能留下部分授权。</summary>
[TestClass]
public sealed class TenantSubscriptionBillingTests
{
    [TestMethod]
    [DataRow("beneficiary")]
    [DataRow("seller")]
    [DataRow("merchant")]
    [DataRow("amount")]
    [DataRow("currency")]
    [DataRow("operation")]
    public async Task Mismatched_receipt_never_activates_or_grants(string mismatch)
    {
        var f = new Fixture();
        var receipt = mismatch switch
        {
            "beneficiary" => f.Receipt with { TenantId = Guid.CreateVersion7() },
            "seller" => f.Receipt with { PaymentTenantId = Guid.CreateVersion7() },
            "merchant" => f.Receipt with { MerchantConfigId = Guid.CreateVersion7() },
            "amount" => f.Receipt with { AmountMinor = 1 },
            "currency" => f.Receipt with { Currency = "USD" },
            _ => f.Receipt with { PaymentOperationId = Guid.CreateVersion7() }
        };
        Assert.IsFalse((await f.Service.ApplyVerifiedPaymentAsync(receipt)).IsSuccess);
        await f.Commands.DidNotReceive().ExecuteAsync(SubscriptionInvoiceSql.ActivateSubscription, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, f.Coordinator.CommitCount);
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
    }

    [TestMethod]
    [DataRow("Applied", true)]
    [DataRow("Refunded", false)]
    public async Task Terminal_receipt_replay_does_not_regrant(string phase, bool success)
    {
        var f = new Fixture(phase);
        Assert.AreEqual(success, (await f.Service.ApplyVerifiedPaymentAsync(f.Receipt)).IsSuccess);
        await f.Commands.DidNotReceive().ExecuteAsync(SubscriptionInvoiceSql.ActivateSubscription, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Audit.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
    }

    [TestMethod]
    public async Task Failed_quota_write_rolls_back_prior_subscription_and_feature_grant()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(SubscriptionInvoiceSql.UpdateQuota, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse((await f.Service.ApplyVerifiedPaymentAsync(f.Receipt)).IsSuccess);
        await f.Commands.Received(1).ExecuteAsync(SubscriptionInvoiceSql.ActivateSubscription, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(SubscriptionInvoiceSql.GrantEntitlement, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Domain_audit_failure_rolls_back_fulfillment()
    {
        var f = new Fixture();
        f.Audit.WriteAsync(Arg.Any<TenancyDomainAuditWrite>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("audit storage unavailable"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Service.ApplyVerifiedPaymentAsync(f.Receipt));
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Refund_before_paid_application_closes_invoice_without_any_grant()
    {
        var f = new Fixture();
        var result = await f.Service.ApplyVerifiedRefundAsync(new(f.Receipt, Guid.CreateVersion7(), f.Receipt.AmountMinor));
        Assert.IsTrue(result.IsSuccess);
        await f.Commands.Received(1).ExecuteAsync(SubscriptionInvoiceSql.Refund, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(SubscriptionInvoiceSql.GrantEntitlement, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(SubscriptionInvoiceSql.UpdateQuota, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("missing-version")]
    [DataRow("missing-source")]
    [DataRow("empty-source")]
    [DataRow("zero-version")]
    public async Task Renewal_requires_a_complete_nonempty_source_pair(string invalid)
    {
        var f = new Fixture();
        var request = new CreatePaidTenantSubscriptionRequest(f.Invoice.PackageId, 1, f.Invoice.EffectiveToUtc, Guid.CreateVersion7())
        {
            RenewalOfSubscriptionId = invalid == "missing-source" ? null : invalid == "empty-source" ? Guid.Empty : f.Invoice.Id,
            RenewalSourceVersion = invalid == "missing-version" ? null : invalid == "zero-version" ? 0 : 1
        };
        var result = await f.Service.CreateAsync(f.Invoice.TenantId, request, default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual("tenancy.subscription_billing.invalid", result.Error!.Code);
        await f.Commands.DidNotReceive().ExecuteAsync(SubscriptionInvoiceSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("Active", "Applied", true)]
    [DataRow("Cancelled", "Applied", true)]
    [DataRow("Trial", "Applied", false)]
    [DataRow("PastDue", "Applied", false)]
    [DataRow("Expired", "Applied", false)]
    [DataRow("Active", "Pending", false)]
    [DataRow("Cancelled", "Refunded", false)]
    public async Task Only_current_paid_sources_are_eligible(string status, string phase, bool expected)
    {
        var f = new Fixture(phase);
        f.Queries.QuerySingleOrDefaultAsync<TenantEntitlementEnforcementRecord>(TenantEntitlementSql.GetEnforcementPhase,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new TenantEntitlementEnforcementRecord(TenantEntitlementEnforcementPhases.Enforced, 1));
        f.Queries.QuerySingleOrDefaultAsync<TenantSubscriptionRecord>(TenantSubscriptionSql.FindById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new TenantSubscriptionRecord(f.Invoice.Id, f.Invoice.TenantId,
                f.Invoice.PackageId, status, null, f.Invoice.EffectiveFromUtc, f.Invoice.EffectiveToUtc, null, 2));
        Assert.AreEqual(expected, (await f.Service.ReadRenewalSourceAsync(f.Invoice.TenantId, f.Invoice.Id, default)).IsSuccess);
    }

    private sealed class Fixture
    {
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly ITransactionalDomainAuditWriter<TenancyDomainAuditWrite> Audit = Substitute.For<ITransactionalDomainAuditWriter<TenancyDomainAuditWrite>>();
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal readonly TenantSubscriptionBillingService Service;
        internal readonly TenantSubscriptionPaymentReceipt Receipt;
        internal readonly IQueryExecutor Queries;
        internal readonly SubscriptionInvoiceRecord Invoice;
        internal Fixture(string phase = "Pending")
        {
            var now = DateTimeOffset.UtcNow;
            var tenant = Guid.CreateVersion7();
            var invoice = new SubscriptionInvoiceRecord(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(), "request-hash",
                Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, null,
                12500, "CNY", 5, 10000, now.AddMinutes(-1), now.AddDays(30), phase, null, 0, now, now, 1, "Frozen Plan");
            Receipt = new(invoice.Id, tenant, invoice.PaymentTenantId, invoice.PaymentOperationId, Guid.CreateVersion7(),
                invoice.MerchantConfigId, invoice.AmountMinor, invoice.Currency, "transaction-1", now);
            var queries = Substitute.For<IQueryExecutor>();
            Queries = queries; Invoice = invoice;
            queries.QuerySingleOrDefaultAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(tenant);
            queries.QuerySingleOrDefaultAsync<SubscriptionInvoiceRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(invoice);
            queries.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new TenantPackageIdentityRecord(invoice.PackageId, "plan", "Plan", null, true, 1));
            queries.QuerySingleOrDefaultAsync<TenantResolutionRecord>(TenantSql.FindById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new TenantResolutionRecord(tenant, "tenant", "Tenant", "tenant.localhost", true, 1, "zh-CN"));
            queries.QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new TenantQuotaMetricRecord(Guid.CreateVersion7(), tenant, TenantQuotaMetricCodes.IdentitySeats, TenantQuotaDefaults.PeriodKey, 100, 2, 1, 1));
            queries.QueryAsync<InvoiceEntitlementRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new[] { new InvoiceEntitlementRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), "feature.plan") });
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            var db = Options.Create(new DatabaseOptions());
            Service = new TenantSubscriptionBillingService(queries, Commands, new DapperCommandTransaction(Coordinator), clock, ids, db,
                new TenantSubscriptionWriteBoundary(queries, Commands, db, clock), new TenantHostPackageBinder(queries, Commands, clock), Audit);
        }
    }
}
