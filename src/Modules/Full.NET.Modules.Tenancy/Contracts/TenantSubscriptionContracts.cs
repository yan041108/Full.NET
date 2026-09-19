namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>租户订阅查询与管理的权限码字符串常量。</summary>
/// <remarks>
/// 权限码字符串发布后不可改名或删除；新增权限只能追加到本类末尾。订阅状态变更须与支付履行模块协作，避免状态不一致。
/// </remarks>
public static class TenancyTenantSubscriptionPermissions
{
    /// <summary>读取租户订阅详情与状态；面向管理后台与计费链路。</summary>
    public const string Read = "tenancy.tenant_subscriptions.read";

    /// <summary>管理租户订阅（创建、取消、状态变更）；影响权益与配额生效。</summary>
    public const string Manage = "tenancy.tenant_subscriptions.manage";
}

/// <summary>租户订阅状态常量；描述订阅的生命周期阶段。</summary>
/// <remarks>
/// 状态字符串值发布后不可改名或删除；新增状态只能追加到本类末尾。状态迁移须遵循：Trial 可转 Active 或 PastDue，PastDue 为支付逾期中间态，Cancelled 与 Expired 为终态。
/// </remarks>
public static class TenantSubscriptionStatuses
{
    /// <summary>试用期；未开始付费，TrialEndsAtUtc 之后自动转入 Active 或 Expired。</summary>
    public const string Trial = "Trial";

    /// <summary>活动状态；订阅有效且在计费周期内。</summary>
    public const string Active = "Active";

    /// <summary>逾期未付；当前周期支付失败，进入宽限期，再次成功支付可恢复 Active。</summary>
    public const string PastDue = "PastDue";

    /// <summary>已取消；用户或管理员主动取消，当前周期结束后不再续费。</summary>
    public const string Cancelled = "Cancelled";

    /// <summary>已过期；订阅到期且未续费，权益与配额按套餐配置降级或回收。</summary>
    public const string Expired = "Expired";
}

/// <summary>租户订阅的只读响应；描述订阅状态、周期与套餐绑定。</summary>
/// <remarks>
/// 字段顺序发布后不可重排；新增字段只能追加到末尾。CurrentPeriodStartUtc 与 CurrentPeriodEndUtc 表示当前计费周期边界，调用方据此判断续费窗口。
/// </remarks>
/// <param name="Id">订阅稳定标识。</param>
/// <param name="TenantId">目标租户标识。</param>
/// <param name="PackageId">绑定的套餐标识；<see langword="null"/> 表示自定义订阅。</param>
/// <param name="Status">订阅状态；取值见 <see cref="TenantSubscriptionStatuses"/>。</param>
/// <param name="TrialEndsAtUtc">试用结束时间（UTC）；<see langword="null"/> 表示非试用订阅。</param>
/// <param name="CurrentPeriodStartUtc">当前计费周期起始时间（UTC）。</param>
/// <param name="CurrentPeriodEndUtc">当前计费周期结束时间（UTC）；过期后进入续费或 Expired。</param>
/// <param name="CancelledAtUtc">取消时间（UTC）；<see langword="null"/> 表示未取消。</param>
/// <param name="Version">乐观并发版本；写操作须回传以避免覆盖并发变更。</param>
public sealed record TenantSubscriptionResponse(
    Guid Id,
    Guid TenantId,
    Guid? PackageId,
    string Status,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc,
    DateTimeOffset? CancelledAtUtc,
    int Version);

/// <summary>创建租户订阅的请求；用于初始化订阅状态与计费周期。</summary>
/// <remarks>
/// 创建后须与支付履行模块协作：Trial 状态订阅在 TrialEndsAtUtc 后自动转入计费；PackageId 须为已存在的活动套餐。
/// </remarks>
/// <param name="PackageId">绑定的套餐标识；<see langword="null"/> 表示自定义订阅。</param>
/// <param name="Status">初始订阅状态；取值见 <see cref="TenantSubscriptionStatuses"/>。</param>
/// <param name="TrialEndsAtUtc">试用结束时间（UTC）；<see langword="null"/> 表示非试用订阅。</param>
/// <param name="CurrentPeriodStartUtc">当前计费周期起始时间（UTC）。</param>
/// <param name="CurrentPeriodEndUtc">当前计费周期结束时间（UTC）。</param>
public sealed record CreateTenantSubscriptionRequest(
    Guid? PackageId,
    string Status,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset CurrentPeriodStartUtc,
    DateTimeOffset CurrentPeriodEndUtc);

/// <summary>取消租户订阅的请求；调用方须回传当前 Version 以做 CAS 守卫。</summary>
/// <remarks>
/// 取消是幂等操作：对已取消的订阅再次取消视为成功。取消后当前周期内仍保留权益，周期结束后转入 Expired 并触发权益降级。
/// </remarks>
/// <param name="Version">调用方看到的当前版本；服务端据此拒绝并发覆盖。</param>
public sealed record CancelTenantSubscriptionRequest(int Version);