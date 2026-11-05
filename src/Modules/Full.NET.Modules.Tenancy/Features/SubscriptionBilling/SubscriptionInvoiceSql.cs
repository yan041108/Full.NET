using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.SubscriptionBilling;

/// <summary>订阅根写入统一先锁租户，再锁报价/账单；资金事实与权益只在本模块事务内收敛。</summary>
internal static class SubscriptionInvoiceSql
{
    private const string Columns = "Id,TenantId,RequestId,RequestHash,PackageId,PackageVersion,OfferVersion,PaymentTenantId,MerchantConfigId,PaymentOperationId,PaymentOrderId,ProviderTransactionId,AmountMinor,Currency,SeatLimit,StorageLimit,EffectiveFromUtc,EffectiveToUtc,Phase,LastRefundId,RefundAmountMinor,CreatedAtUtc,UpdatedAtUtc,Version,Subject,RenewalOfSubscriptionId,RenewalSourceVersion";
    internal static readonly SqlStatement LockTenantSqlServer = new("tenancy.subscription_billing.lock_tenant.sql_server",
        "SELECT Id FROM fn_tenancy_tenant WITH (UPDLOCK,HOLDLOCK) WHERE Id=@TenantId AND (@RequireActive=0 OR LifecycleStatus='Active')", SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockTenantMySql = new("tenancy.subscription_billing.lock_tenant.my_sql",
        "SELECT Id FROM fn_tenancy_tenant WHERE Id=@TenantId AND (@RequireActive=0 OR LifecycleStatus='Active') FOR UPDATE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadActiveTenant = new("tenancy.subscription_billing.read_active_tenant",
        "SELECT Id FROM fn_tenancy_tenant WHERE Id=@TenantId AND LifecycleStatus='Active'", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ExpiredTenantsSqlServer = new("tenancy.subscription_billing.expired_tenants.sql_server", """
        SELECT DISTINCT TOP (100) TenantId FROM fn_tenancy_tenant_subscription
        WHERE Status IN ('Trial','Active','PastDue','Cancelled')
          AND (CurrentPeriodEndUtc<=@Now OR (TrialEndsAtUtc IS NOT NULL AND TrialEndsAtUtc<=@Now)) ORDER BY TenantId
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ExpiredTenantsMySql = new("tenancy.subscription_billing.expired_tenants.my_sql", """
        SELECT DISTINCT TenantId FROM fn_tenancy_tenant_subscription
        WHERE Status IN ('Trial','Active','PastDue','Cancelled')
          AND (CurrentPeriodEndUtc<=@Now OR (TrialEndsAtUtc IS NOT NULL AND TrialEndsAtUtc<=@Now)) ORDER BY TenantId LIMIT 100
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ExpireForTenant = new("tenancy.subscription_billing.expire_for_tenant", """
        UPDATE fn_tenancy_tenant_subscription SET Status='Expired',UpdatedAtUtc=@Now,Version=Version+1
        WHERE TenantId=@TenantId AND Status IN ('Trial','Active','PastDue','Cancelled')
          AND (CurrentPeriodEndUtc<=@Now OR (TrialEndsAtUtc IS NOT NULL AND TrialEndsAtUtc<=@Now))
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Find = new("tenancy.subscription_invoice.find",
        $"SELECT {Columns} FROM fn_tenancy_subscription_invoice WHERE TenantId=@TenantId AND Id=@Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindRequest = new("tenancy.subscription_invoice.find_request",
        $"SELECT {Columns} FROM fn_tenancy_subscription_invoice WHERE TenantId=@TenantId AND RequestId=@RequestId", SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockSqlServer = new("tenancy.subscription_invoice.lock.sql_server",
        $"SELECT {Columns} FROM fn_tenancy_subscription_invoice WITH (UPDLOCK,HOLDLOCK) WHERE TenantId=@TenantId AND Id=@Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockMySql = new("tenancy.subscription_invoice.lock.my_sql",
        $"SELECT {Columns} FROM fn_tenancy_subscription_invoice WHERE TenantId=@TenantId AND Id=@Id FOR UPDATE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement Insert = new("tenancy.subscription_invoice.insert", $"""
        INSERT INTO fn_tenancy_subscription_invoice ({Columns}) VALUES
          (@Id,@TenantId,@RequestId,@RequestHash,@PackageId,@PackageVersion,@OfferVersion,@PaymentTenantId,@MerchantConfigId,
           @PaymentOperationId,NULL,NULL,@AmountMinor,@Currency,@SeatLimit,@StorageLimit,@EffectiveFromUtc,@EffectiveToUtc,
           'Pending',NULL,0,@Now,@Now,1,@Subject,@RenewalOfSubscriptionId,@RenewalSourceVersion)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement InsertEntitlement = new("tenancy.subscription_invoice.insert_entitlement", """
        INSERT INTO fn_tenancy_invoice_entitlement (Id,InvoiceId,EntitlementId,EntitlementCode,CreatedAtUtc)
        VALUES (@Id,@InvoiceId,@EntitlementId,@EntitlementCode,@Now)
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListEntitlements = new("tenancy.subscription_invoice.list_entitlements", """
        SELECT snapshot.Id,snapshot.EntitlementId,snapshot.EntitlementCode
        FROM fn_tenancy_invoice_entitlement snapshot WHERE snapshot.InvoiceId=@Id ORDER BY snapshot.Id
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement ActivateSubscription = new("tenancy.subscription_invoice.activate_subscription", """
        UPDATE fn_tenancy_tenant_subscription SET Status='Active',TrialEndsAtUtc=NULL,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND Status='PastDue' AND CancelledAtUtc IS NULL
          AND CurrentPeriodStartUtc=@EffectiveFromUtc AND CurrentPeriodEndUtc=@EffectiveToUtc
          AND CurrentPeriodStartUtc<=@Now AND CurrentPeriodEndUtc>@Now
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement GrantEntitlement = new("tenancy.subscription_invoice.grant_entitlement", """
        INSERT INTO fn_tenancy_tenant_entitlement_binding
          (Id,TenantId,EntitlementId,EffectiveFromUtc,EffectiveToUtc,SourcePackageId,SourceSubscriptionId,CreatedAtUtc,UpdatedAtUtc,Version)
        SELECT @BindingId,@TenantId,catalog.Id,@EffectiveFromUtc,@EffectiveToUtc,@PackageId,@Id,@Now,@Now,1
        FROM fn_tenancy_entitlement_catalog catalog WHERE catalog.Id=@EntitlementId AND catalog.Code=@EntitlementCode
          AND catalog.IsActive=1 AND catalog.EntitlementType='Feature'
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Apply = new("tenancy.subscription_invoice.apply", """
        UPDATE fn_tenancy_subscription_invoice SET Phase='Applied',PaymentOrderId=@PaymentOrderId,
          ProviderTransactionId=@ProviderTransactionId,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND Phase='Pending' AND Version=@Version
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement Refund = new("tenancy.subscription_invoice.refund", """
        UPDATE fn_tenancy_subscription_invoice SET Phase='Refunded',PaymentOrderId=@PaymentOrderId,
          ProviderTransactionId=@ProviderTransactionId,LastRefundId=@RefundId,RefundAmountMinor=@RefundAmountMinor,
          UpdatedAtUtc=@Now,Version=Version+1 WHERE Id=@Id AND TenantId=@TenantId AND Version=@Version
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement MarkPastDue = new("tenancy.subscription_invoice.mark_past_due", """
        UPDATE fn_tenancy_tenant_subscription SET Status='PastDue',UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@Id AND TenantId=@TenantId AND Status IN ('Active','Trial')
        """, SqlDataScope.HostOnly);
    internal static readonly SqlStatement UpdateQuota = new("tenancy.subscription_invoice.update_quota", """
        UPDATE fn_tenancy_quota_metric SET LimitValue=@LimitValue,SourceSubscriptionId=@Id,UpdatedAtUtc=@Now,Version=Version+1
        WHERE Id=@MetricId AND TenantId=@TenantId AND MetricCode=@MetricCode AND PeriodKey=@PeriodKey AND Version=@MetricVersion
        """, SqlDataScope.HostOnly);
}

/// <summary>账单固定列序；请求摘要和资金身份不可被后续报价更新覆盖。</summary>
internal sealed record SubscriptionInvoiceRecord(Guid Id, Guid TenantId, Guid RequestId, string RequestHash,
    Guid PackageId, int PackageVersion, int OfferVersion, Guid PaymentTenantId, Guid MerchantConfigId,
    Guid PaymentOperationId, Guid? PaymentOrderId, string? ProviderTransactionId, long AmountMinor, string Currency,
    long SeatLimit, long StorageLimit, DateTimeOffset EffectiveFromUtc, DateTimeOffset EffectiveToUtc, string Phase,
    Guid? LastRefundId, long RefundAmountMinor, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, int Version, string Subject,
    Guid? RenewalOfSubscriptionId = null, int? RenewalSourceVersion = null)
{
    internal TenantSubscriptionPaymentTerms Terms() => new(Id, TenantId, PaymentTenantId, PaymentOperationId,
        MerchantConfigId, PackageId, OfferVersion, AmountMinor, Currency, EffectiveFromUtc, EffectiveToUtc, Subject)
        { RenewalOfSubscriptionId = RenewalOfSubscriptionId, RenewalSourceVersion = RenewalSourceVersion };
}

/// <summary>订阅实际获得的目录快照；授权仍要求当前目录有效。</summary>
internal sealed record InvoiceEntitlementRecord(Guid Id, Guid EntitlementId, string EntitlementCode);
