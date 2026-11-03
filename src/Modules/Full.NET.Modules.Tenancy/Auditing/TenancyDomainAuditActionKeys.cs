namespace Full.NET.Modules.Tenancy.Auditing;

/// <summary>
/// Tenancy 模块 B0 域内审计使用的稳定 ActionKey 常量；取值必须与
/// <c>Full.NET.Modules.Auditing.AuditReliabilityCatalog</c> 中登记的分类保持一致。
/// </summary>
internal static class TenancyDomainAuditActionKeys
{
    /// <summary>Host 管理员禁用租户。</summary>
    public const string HostTenantDisable = "tenancy.host_tenant.disable";

    /// <summary>Host 管理员重新启用租户。</summary>
    public const string HostTenantEnable = "tenancy.host_tenant.enable";

    /// <summary>更新服务端套餐报价及授权范围。</summary>
    public const string PackageOfferUpdated = "tenancy.package_offer.updated";
    /// <summary>创建不可变的付费订阅报价。</summary>
    public const string SubscriptionInvoiceCreated = "tenancy.subscription.invoice_created";
    /// <summary>已验证付款与权益在本地事务内完成。</summary>
    public const string SubscriptionPaymentApplied = "tenancy.subscription.payment_applied";
    /// <summary>原订阅成功退款并关闭自动授权。</summary>
    public const string SubscriptionRefundApplied = "tenancy.subscription.refund_applied";
}

/// <summary>Tenancy 模块 B0 域内审计记录的固定结果取值。</summary>
internal static class TenancyDomainAuditOutcomes
{
    public const string Success = "success";

    public const string Failure = "failure";
}
