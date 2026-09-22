namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>租户配额查询、维护与预留的权限码字符串常量。</summary>
/// <remarks>
/// 权限码字符串发布后不可改名或删除；新增权限只能追加到本类末尾，避免破坏既有角色分配与策略缓存。
/// </remarks>
public static class TenancyTenantQuotaPermissions
{
    /// <summary>读取租户配额指标与预留记录；面向管理后台与配额执行链路。</summary>
    public const string Read = "tenancy.tenant_quota.read";

    /// <summary>维护租户配额指标（创建/更新 LimitValue）；影响限流上限。</summary>
    public const string Manage = "tenancy.tenant_quota.manage";

    /// <summary>对配额发起预留、确认与释放；面向业务侧资源占用流程。</summary>
    public const string Reserve = "tenancy.tenant_quota.reserve";

    /// <summary>修复历史预留缺失的 MetricId；Production 手工对账。</summary>
    public const string ReconcileMetricIds = "tenancy.tenant_quota.reconcile_metric_ids";
}

/// <summary>历史预留 MetricId 对账请求。</summary>
/// <param name="DryRun">为 true 时仅统计可修复条数，不写库。</param>
public sealed record ReconcileTenantQuotaMetricIdsRequest(bool DryRun = true);

/// <summary>历史预留 MetricId 对账结果。</summary>
public sealed record ReconcileTenantQuotaMetricIdsResponse(
    int OutstandingCount,
    int RepairedCount,
    int SkippedCount,
    bool DryRun);

/// <summary>租户配额指标编码常量；用于在预留、确认与释放流程中稳定引用指标。</summary>
/// <remarks>
/// 指标编码发布后不可改名或删除；新增指标只能追加到本类末尾，并须同步登记到配额执行模块的策略表。
/// </remarks>
public static class TenantQuotaMetricCodes
{
    /// <summary>身份模块的席位数量指标；用于限制租户可纳入的用户数。</summary>
    public const string IdentitySeats = "identity.seats";

    /// <summary>文件模块的存储字节指标；用于限制租户可使用的存储容量。</summary>
    public const string FilesStorageBytes = "files.storage_bytes";
}

/// <summary>租户配额默认值常量；用于未显式配置时的兜底。</summary>
/// <remarks>
/// 默认值仅用于新租户初始化或未显式配置场景；已存在租户的配额变更须通过 Manage 权限显式写入。
/// </remarks>
public static class TenantQuotaDefaults
{
    /// <summary>默认配额周期键；表示按非周期方式累计。</summary>
    public const string PeriodKey = "default";

    /// <summary>身份席位的默认上限；新租户未显式配置时使用。</summary>
    public const long IdentitySeatsLimit = 100;
}

/// <summary>配额预留状态常量；描述预留的生命周期阶段。</summary>
/// <remarks>
/// 状态字符串值发布后不可改名或删除；新增状态只能追加到本类末尾。终态（Confirmed/Released/Expired）操作必须可重放，不得因重复请求报错。
/// </remarks>
public static class TenantQuotaReservationStatuses
{
    /// <summary>已预留；配额已被占用但未最终确认，到期未确认会自动转入 Expired。</summary>
    public const string Reserved = "Reserved";

    /// <summary>已确认；预留转为实际占用，纳入 UsedValue 统计。</summary>
    public const string Confirmed = "Confirmed";

    /// <summary>已释放；预留被显式或到期回收，配额回归可用池。</summary>
    public const string Released = "Released";

    /// <summary>已过期；预留到达 TTL 未确认，由后台任务回收并释放配额。</summary>
    public const string Expired = "Expired";
}

/// <summary>租户配额指标的只读响应；描述某指标在某周期内的限额与占用情况。</summary>
/// <remarks>
/// 字段顺序发布后不可重排；新增字段只能追加到末尾。ReservedValue 与 UsedValue 的差值即当前可立即分配的额度，调用方不应自行缓存以避免跨实例不一致。
/// </remarks>
/// <param name="Id">配额指标记录的稳定标识。</param>
/// <param name="TenantId">目标租户标识。</param>
/// <param name="MetricCode">指标编码；取值见 <see cref="TenantQuotaMetricCodes"/>。</param>
/// <param name="PeriodKey">配额周期键；用于区分按月/按年等累计维度。</param>
/// <param name="LimitValue">该周期内的总额度上限。</param>
/// <param name="UsedValue">已确认占用的额度。</param>
/// <param name="ReservedValue">已预留但未确认的额度；与 UsedValue 之和不得超过 LimitValue。</param>
/// <param name="Version">乐观并发版本；写操作须回传以避免覆盖并发变更。</param>
public sealed record TenantQuotaMetricResponse(
    Guid Id, Guid TenantId, string MetricCode, string PeriodKey,
    long LimitValue, long UsedValue, long ReservedValue, int Version);

/// <summary>对配额发起预留的请求；预留成功后额度进入 ReservedValue，到期未确认自动回收。</summary>
/// <remarks>
/// OperationId 是幂等键；同一 OperationId 重复提交返回既有预留，不产生新预留。预留必须绑定原始 MetricCode，确认与释放阶段须回传同一 MetricCode 以精确定位。
/// </remarks>
/// <param name="MetricCode">目标指标编码；取值见 <see cref="TenantQuotaMetricCodes"/>。</param>
/// <param name="OperationId">调用方提供的幂等键；同一键重复提交视为同一预留。</param>
/// <param name="Amount">预留数量；不得超过当前可用额度（LimitValue - UsedValue - ReservedValue）。</param>
/// <param name="ExpiresInMinutes">预留 TTL（分钟）；到期未确认自动转入 Expired 并回收额度，默认 30 分钟。</param>
public sealed record ReserveTenantQuotaRequest(
    string MetricCode, string OperationId, long Amount, int ExpiresInMinutes = 30);

/// <summary>配额预留操作的只读响应；描述预留结果与到期时间。</summary>
/// <remarks>
/// 字段顺序发布后不可重排；新增字段只能追加到末尾。ExpiresAtUtc 之后该预留视为过期，调用方不应再据此判定额度可用。
/// </remarks>
/// <param name="ReservationId">预留记录的稳定标识。</param>
/// <param name="TenantId">目标租户标识。</param>
/// <param name="MetricCode">指标编码；与请求一致。</param>
/// <param name="OperationId">幂等键；与请求一致。</param>
/// <param name="Amount">预留数量。</param>
/// <param name="Status">预留状态；取值见 <see cref="TenantQuotaReservationStatuses"/>。</param>
/// <param name="ExpiresAtUtc">预留到期时间（UTC）；早于此时间须确认或释放，否则进入过期回收。</param>
public sealed record ReserveTenantQuotaResponse(
    Guid ReservationId, Guid TenantId, string MetricCode, string OperationId,
    long Amount, string Status, DateTimeOffset ExpiresAtUtc);

/// <summary>确认配额预留的请求；将预留转为实际占用并从 ReservedValue 转入 UsedValue。</summary>
/// <remarks>
/// 确认是终态操作，必须可重放：对已确认的预留再次确认视为幂等成功，不重复扣减额度。MetricCode 省略时仅兼容租户内无歧义的历史操作键，新调用应显式传入以确保跨指标安全。
/// </remarks>
/// <param name="OperationId">与预留阶段一致的幂等键；用于定位待确认预留。</param>
public sealed record ConfirmTenantQuotaRequest(string OperationId)
{
    /// <summary>精确定位指标；省略时仅兼容租户内无歧义的历史操作键。</summary>
    public string? MetricCode { get; init; }
}

/// <summary>释放配额预留的请求；将预留额度归还到可用池，不再计入 ReservedValue。</summary>
/// <remarks>
/// 释放是终态操作，必须可重放：对已释放或已过期的预留再次释放视为幂等成功，不重复归还额度。MetricCode 省略时仅兼容租户内无歧义的历史操作键，新调用应显式传入以确保跨指标安全。
/// </remarks>
/// <param name="OperationId">与预留阶段一致的幂等键；用于定位待释放预留。</param>
public sealed record ReleaseTenantQuotaRequest(string OperationId)
{
    /// <summary>精确定位指标；省略时仅兼容租户内无歧义的历史操作键。</summary>
    public string? MetricCode { get; init; }
}

/// <summary>创建或更新租户配额指标的请求；用于初始化或调整某指标的 LimitValue。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="MetricCode">目标指标编码；取值见 <see cref="TenantQuotaMetricCodes"/>。</param>
/// <param name="PeriodKey">配额周期键；与现有记录不一致时视为新周期记录。</param>
/// <param name="LimitValue">该周期内的总额度上限；不得低于当前 UsedValue + ReservedValue。</param>
public sealed record UpsertTenantQuotaMetricRequest(
    string MetricCode,
    string PeriodKey,
    long LimitValue);

/// <summary>租户配额指标列表的只读响应；用于分页查询结果。</summary>
/// <remarks>
/// 字段顺序发布后不可重排；新增字段只能追加到末尾。Items 顺序由服务端决定，调用方不应假设按 MetricCode 排序。
/// </remarks>
/// <param name="Items">当前页的配额指标响应集合。</param>
public sealed record ListTenantQuotaMetricsResponse(
    IReadOnlyList<TenantQuotaMetricResponse> Items);

/// <summary>提供租户配额的预留、确认与释放能力；面向业务侧资源占用流程。</summary>
/// <remarks>
/// 实现方须保证：同一 OperationId 的预留/确认/释放均幂等；预留、确认、释放均须与原始 MetricCode 绑定；
/// 并发场景下使用 CAS 守卫 LimitValue，避免超额分配；终态操作必须可重放，不得因重复请求报错。
/// </remarks>
public interface ITenantQuotaReservationService
{
    /// <summary>
    /// 对指定租户的配额发起预留；同一 OperationId 重复提交返回既有预留，视为幂等成功。
    /// </summary>
    /// <remarks>
    /// 预留额度进入 ReservedValue，到期未确认由后台任务回收。Amount 超过当前可用额度时返回稳定失败码，不产生部分预留。
    /// </remarks>
    /// <param name="tenantId">目标租户标识；调用方须确保来自已解析的租户上下文，禁止跨租户写入。</param>
    /// <param name="request">预留请求；OperationId 为幂等键，MetricCode 须为已登记指标。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>成功返回预留响应；额度不足或指标不存在时返回稳定失败码。</returns>
    Task<Abstractions.Results.Result<ReserveTenantQuotaResponse>> ReserveAsync(
        Guid tenantId, ReserveTenantQuotaRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 确认指定租户的配额预留；将预留转为实际占用。已确认的预留再次确认视为幂等成功。
    /// </summary>
    /// <remarks>
    /// 确认是终态操作，必须可重放。确认后额度从 ReservedValue 转入 UsedValue；不存在的预留按业务规则返回失败码，不抛异常。
    /// </remarks>
    /// <param name="tenantId">目标租户标识；须与预留阶段的租户一致。</param>
    /// <param name="request">确认请求；OperationId 用于定位预留，MetricCode 用于跨指标安全。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>成功返回更新后的配额指标；预留不存在或状态不兼容时返回稳定失败码。</returns>
    Task<Abstractions.Results.Result<TenantQuotaMetricResponse>> ConfirmAsync(
        Guid tenantId, ConfirmTenantQuotaRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放指定租户的配额预留；将预留额度归还可用池。已释放或已过期的预留再次释放视为幂等成功。
    /// </summary>
    /// <remarks>
    /// 释放是终态操作，必须可重放。释放后额度从 ReservedValue 扣除；不存在的预留按业务规则返回失败码，不抛异常。
    /// </remarks>
    /// <param name="tenantId">目标租户标识；须与预留阶段的租户一致。</param>
    /// <param name="request">释放请求；OperationId 用于定位预留，MetricCode 用于跨指标安全。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>成功返回更新后的配额指标；预留不存在时返回稳定失败码。</returns>
    Task<Abstractions.Results.Result<TenantQuotaMetricResponse>> ReleaseAsync(
        Guid tenantId, ReleaseTenantQuotaRequest request,
        CancellationToken cancellationToken = default);
}