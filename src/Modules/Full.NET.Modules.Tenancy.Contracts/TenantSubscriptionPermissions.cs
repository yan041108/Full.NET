namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>订阅读取和管理的稳定权限；Payments 的付款和恢复入口复用同一授权边界。</summary>
public static class TenancyTenantSubscriptionPermissions
{
    /// <summary>读取租户订阅及其履约状态。</summary>
    public const string Read = "tenancy.tenant_subscriptions.read";
    /// <summary>管理订阅、创建报价及接续原资金事实。</summary>
    public const string Manage = "tenancy.tenant_subscriptions.manage";
}
