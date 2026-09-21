namespace Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>表示一条已登记缓存策略的只读摘要。</summary>
/// <param name="EntryName">稳定缓存条目名。</param>
/// <param name="OwnerModule">所属模块键。</param>
/// <param name="ConsistencyClass">一致性类别标签，例如 s1、s2。</param>
/// <param name="AccessKind">访问语义，例如 use_cache、bypass。</param>
/// <param name="L1DurationSeconds">节点 L1 TTL 秒数；S0-L2 或不可缓存时为 <see langword="null"/>。</param>
/// <param name="L2DurationSeconds">共享 L2 TTL 秒数；不可缓存时为 <see langword="null"/>。</param>
/// <param name="RequiresDirectInvalidation">是否要求提交后直接失效。</param>
/// <param name="CanInvalidate">是否允许通过管理控制面执行登记失效操作。</param>
/// <param name="InvalidationOperations">允许执行的精确失效操作目录。</param>
public sealed record CachePolicySummary(
    string EntryName,
    string OwnerModule,
    string ConsistencyClass,
    string AccessKind,
    long? L1DurationSeconds,
    long? L2DurationSeconds,
    bool RequiresDirectInvalidation,
    bool CanInvalidate,
    IReadOnlyList<CacheInvalidationOperationSummary> InvalidationOperations);

/// <summary>表示一条精确失效操作的参数说明。</summary>
/// <param name="OperationKey">稳定操作键。</param>
/// <param name="DisplayName">面向运维展示的操作名称。</param>
/// <param name="Parameters">受控参数目录，禁止自由键名。</param>
public sealed record CacheInvalidationOperationSummary(
    string OperationKey,
    string DisplayName,
    IReadOnlyList<CacheInvalidationParameterSummary> Parameters);

/// <summary>表示失效操作的一个受控参数。</summary>
/// <param name="Name">参数名。</param>
/// <param name="ValueType">参数类型，例如 uuid、domain、grid_key。</param>
/// <param name="Required">是否必填。</param>
public sealed record CacheInvalidationParameterSummary(
    string Name,
    string ValueType,
    bool Required);

/// <summary>表示一次精确失效请求。</summary>
/// <param name="OperationKey">要执行的登记操作键。</param>
/// <param name="Parameters">操作参数；仅允许目录声明的键。</param>
/// <param name="Scope">失效传播范围，默认全层同步。</param>
public sealed record CacheInvalidationRequest(
    string OperationKey,
    IReadOnlyDictionary<string, string>? Parameters,
    string? Scope);

/// <summary>表示一次精确失效的执行结果，不包含缓存值。</summary>
/// <param name="EntryName">目标缓存条目名。</param>
/// <param name="OperationKey">已执行的操作键。</param>
/// <param name="Scope">实际使用的传播范围。</param>
/// <param name="InvalidatedTargets">已失效的键或标签描述，仅用于审计定位。</param>
public sealed record CacheInvalidationResult(
    string EntryName,
    string OperationKey,
    string Scope,
    IReadOnlyList<string> InvalidatedTargets);

/// <summary>缓存访问语义常量。</summary>
public static class CachePolicyAccessKinds
{
    /// <summary>优先读写缓存层；命中后回填 L1，未命中穿透至权威源。</summary>
    public const string UseCache = "use_cache";

    /// <summary>绕过缓存直接读取权威源；用于一致性问题排查或强制刷新。</summary>
    public const string AuthorityRead = "authority_read";

    /// <summary>本轮读写均绕过缓存；用于短暂降级或重大数据迁移窗口。</summary>
    public const string Bypass = "bypass";
}

/// <summary>失效传播范围对外常量。</summary>
public static class CacheInvalidationScopeNames
{
    /// <summary>仅失效当前节点 L1，不传播到 L2 与其他节点；用于节点本地临时数据。</summary>
    public const string CurrentNodeOnly = "current_node_only";

    /// <summary>同步失效 L1、L2 并通过 Redis Backplane 广播至所有节点；保证最终一致。</summary>
    public const string AllLayersSynchronous = "all_layers_synchronous";
}

/// <summary>失效参数类型常量。</summary>
public static class CacheInvalidationParameterTypes
{
    /// <summary>UUID 标识参数；用于按实体主键精确失效。</summary>
    public const string Uuid = "uuid";

    /// <summary>领域键参数；用于按业务维度（租户、模块）失效。</summary>
    public const string Domain = "domain";

    /// <summary>网格键参数；用于按数据分片或区域失效。</summary>
    public const string GridKey = "grid_key";
}
