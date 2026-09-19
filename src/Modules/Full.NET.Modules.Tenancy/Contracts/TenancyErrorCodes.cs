namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>
/// 定义 Tenancy 模块对外返回的稳定错误码。
/// </summary>
public static class TenancyErrorCodes
{
    /// <summary>
    /// Tenancy 错误码前缀。
    /// </summary>
    public const string Prefix = "tenancy.";

    /// <summary>已认证租户上下文与请求主机不匹配。</summary>
    public const string ContextMismatch = "tenancy.context_mismatch";

    /// <summary>请求切换到的租户上下文不存在。</summary>
    public const string ContextNotFound = "tenancy.context_not_found";

    /// <summary>租户域名已被占用。</summary>
    public const string DomainExists = "tenancy.domain_exists";

    /// <summary>请求主机没有对应的活动租户。</summary>
    public const string HostNotFound = "tenancy.host_not_found";

    /// <summary>租户标识已被占用。</summary>
    public const string IdentifierExists = "tenancy.identifier_exists";

    /// <summary>当前租户不存在。</summary>
    public const string NotFound = "tenancy.not_found";

    /// <summary>不能禁用最后一个仍处于活动状态的租户。</summary>
    public const string LastActiveTenant = "tenancy.tenant.last_remaining";

    /// <summary>租户记录版本冲突。</summary>
    public const string VersionConflict = "tenancy.tenant.version_conflict";

    /// <summary>租户套餐不存在。</summary>
    public const string PackageNotFound = "tenancy.tenant_package.not_found";

    /// <summary>租户套餐编码已被占用。</summary>
    public const string PackageCodeExists = "tenancy.tenant_package.code_exists";

    /// <summary>租户套餐记录版本冲突。</summary>
    public const string PackageVersionConflict = "tenancy.tenant_package.version_conflict";

    /// <summary>不能为租户分配已禁用的套餐。</summary>
    public const string PackageInactive = "tenancy.tenant_package.inactive";

    /// <summary>仍有租户绑定该套餐时不能禁用。</summary>
    public const string PackageInUse = "tenancy.tenant_package.in_use";

    /// <summary>租户品牌字段校验失败。</summary>
    public const string BrandingInvalid = "tenancy.tenant_branding.invalid";

    /// <summary>租户 Logo 媒体无效或不可用。</summary>
    public const string BrandingLogoInvalid = "tenancy.tenant_branding.logo_invalid";

    /// <summary>租户 Logo 媒体不存在。</summary>
    public const string BrandingLogoNotFound = "tenancy.tenant_branding.logo_not_found";

    /// <summary>租户所有者用户不存在或未激活。</summary>
    public const string OwnerUserNotFound = "tenancy.tenant_lifecycle.owner_not_found";

    /// <summary>租户生命周期状态无效。</summary>
    public const string LifecycleStatusInvalid = "tenancy.tenant_lifecycle.status_invalid";

    /// <summary>Tenancy 设置单例不存在。</summary>
    public const string SettingsNotFound = "tenancy.settings.not_found";

    /// <summary>Tenancy 设置并发版本冲突。</summary>
    public const string SettingsVersionConflict = "tenancy.settings.version_conflict";

    /// <summary>权益编码无效。</summary>
    public const string EntitlementCodeInvalid = "tenancy.entitlements.code_invalid";

    /// <summary>权益编码已存在。</summary>
    public const string EntitlementCodeExists = "tenancy.entitlements.code_exists";

    /// <summary>权益绑定不存在。</summary>
    public const string EntitlementBindingNotFound = "tenancy.entitlements.binding_not_found";

    /// <summary>权益强制执行阶段无效。</summary>
    public const string EntitlementPhaseInvalid = "tenancy.entitlements.phase_invalid";

    /// <summary>Enforced 阶段缺少套餐或有效订阅绑定。</summary>
    public const string EntitlementCommercialBindingRequired = "tenancy.entitlements.commercial_binding_required";

    /// <summary>配额请求无效。</summary>
    public const string QuotaRequestInvalid = "tenancy.quota.request_invalid";

    /// <summary>配额指标不存在。</summary>
    public const string QuotaMetricNotFound = "tenancy.quota.metric_not_found";

    /// <summary>配额预留不存在。</summary>
    public const string QuotaReservationNotFound = "tenancy.quota.reservation_not_found";

    /// <summary>配额已用尽。</summary>
    public const string QuotaExceeded = "tenancy.quota.exceeded";

    /// <summary>订阅状态无效。</summary>
    public const string SubscriptionStatusInvalid = "tenancy.tenant_subscription.status_invalid";

    /// <summary>订阅周期无效。</summary>
    public const string SubscriptionPeriodInvalid = "tenancy.tenant_subscription.period_invalid";

    /// <summary>订阅不存在。</summary>
    public const string SubscriptionNotFound = "tenancy.tenant_subscription.not_found";

    /// <summary>订阅版本冲突。</summary>
    public const string SubscriptionVersionConflict = "tenancy.tenant_subscription.version_conflict";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        ContextMismatch,
        ContextNotFound,
        DomainExists,
        HostNotFound,
        IdentifierExists,
        LastActiveTenant,
        NotFound,
        VersionConflict,
        PackageNotFound,
        PackageCodeExists,
        PackageVersionConflict,
        PackageInactive,
        PackageInUse,
        BrandingInvalid,
        BrandingLogoInvalid,
        BrandingLogoNotFound,
        OwnerUserNotFound,
        LifecycleStatusInvalid,
        SettingsNotFound,
        SettingsVersionConflict,
        EntitlementCodeInvalid,
        EntitlementCodeExists,
        EntitlementBindingNotFound,
        EntitlementPhaseInvalid,
        EntitlementCommercialBindingRequired,
        QuotaRequestInvalid,
        QuotaMetricNotFound,
        QuotaReservationNotFound,
        QuotaExceeded,
        SubscriptionStatusInvalid,
        SubscriptionPeriodInvalid,
        SubscriptionNotFound,
        SubscriptionVersionConflict,
    ]);
}
