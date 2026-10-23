namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>
/// 发送配额维度键闭合集合；每个维度都必须在受控政策表中声明有界额度，缺失配置时发送失败关闭。
/// </summary>
public static class NotificationQuotaDimensions
{
    /// <summary>渠道厂商维度；取值为渠道类型键（如 im.smtp）。</summary>
    public const string Provider = "provider";

    /// <summary>租户维度；取值为租户作用域键。</summary>
    public const string Tenant = "tenant";

    /// <summary>业务生产者维度；取值为 ProducerKey。</summary>
    public const string Producer = "producer";

    /// <summary>业务场景维度；取值为 SceneKey。</summary>
    public const string Scene = "scene";

    /// <summary>通配取值；表示该维度全部取值，作为精确声明缺失时的受控回退。</summary>
    public const string WildcardValue = "*";

    /// <summary>当前版本已登记的配额维度键集合。</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Provider,
        Tenant,
        Producer,
        Scene,
    };
}

/// <summary>发送配额预留状态键闭合集合；Unknown 暂挂状态只能由对账或人工处置收敛。</summary>
public static class NotificationQuotaReservationStates
{
    /// <summary>已预留、等待发送结果。</summary>
    public const string Reserved = "reserved";

    /// <summary>调用成功已核销，额度被真实消耗。</summary>
    public const string Consumed = "consumed";

    /// <summary>确定未发送，额度已释放回窗口。</summary>
    public const string Released = "released";

    /// <summary>调用结果未知，额度暂挂等待可信回执对账。</summary>
    public const string PendingReconciliation = "pending_reconciliation";

    /// <summary>当前版本已登记的预留状态键集合。</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Reserved,
        Consumed,
        Released,
        PendingReconciliation,
    };
}

/// <summary>
/// 发送配额政策详情；声明维度、窗口长度与窗口内发送上限。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">政策唯一标识。</param>
/// <param name="DimensionKey">配额维度键，取值见 <see cref="NotificationQuotaDimensions"/>。</param>
/// <param name="DimensionValue">维度取值；<c>*</c> 表示该维度全部取值。</param>
/// <param name="WindowSeconds">固定窗口长度（秒）。</param>
/// <param name="MaxCount">单窗口发送上限。</param>
/// <param name="IsEnabled">是否启用；停用后不再参与配额解析。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">最近更新时间（UTC）。</param>
/// <param name="Version">CAS 乐观并发版本号。</param>
public sealed record NotificationQuotaPolicyResponse(
    Guid Id,
    string DimensionKey,
    string DimensionValue,
    int WindowSeconds,
    int MaxCount,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>新建发送配额政策；维度、取值、窗口与上限必须构成有界声明。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="DimensionKey">配额维度键。</param>
/// <param name="DimensionValue">维度取值；<c>*</c> 表示该维度全部取值。</param>
/// <param name="WindowSeconds">固定窗口长度（秒）。</param>
/// <param name="MaxCount">单窗口发送上限。</param>
public sealed record CreateNotificationQuotaPolicyRequest(
    string DimensionKey,
    string DimensionValue,
    int WindowSeconds,
    int MaxCount);

/// <summary>更新发送配额政策；维度与取值不可改名。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="WindowSeconds">固定窗口长度（秒）。</param>
/// <param name="MaxCount">单窗口发送上限。</param>
/// <param name="Version">CAS 乐观并发期望值。</param>
public sealed record UpdateNotificationQuotaPolicyRequest(
    int WindowSeconds,
    int MaxCount,
    long Version);

/// <summary>启用或停用发送配额政策。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="IsEnabled">目标启用状态。</param>
/// <param name="Version">CAS 乐观并发期望值。</param>
public sealed record SetNotificationQuotaPolicyEnabledRequest(
    bool IsEnabled,
    long Version);

/// <summary>
/// 配额预留台账的聚合视图；用于定位受配额阻塞或等待对账的受影响范围。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="DimensionKey">配额维度键。</param>
/// <param name="StateKey">预留状态键，取值见 <see cref="NotificationQuotaReservationStates"/>。</param>
/// <param name="Count">该维度与状态下的预留数量。</param>
public sealed record NotificationQuotaOutstandingResponse(
    string DimensionKey,
    string StateKey,
    long Count);
