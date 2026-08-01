using Full.NET.Abstractions.Auditing;

namespace Full.NET.Modules.Auditing;

/// <summary>
/// 单条 Endpoint/ActionKey 的审计可靠性分类声明。
/// </summary>
/// <param name="ActionKey">稳定的审计 ActionKey，格式为 <c>{module}.{area}.{action}</c>。</param>
/// <param name="ReliabilityClass">该 ActionKey 承诺的 B0/B1/B2 审计可靠性等级。</param>
public sealed record AuditReliabilityCatalogEntry(
    string ActionKey,
    AuditReliabilityClass ReliabilityClass);

/// <summary>
/// B0/B1/B2 审计可靠性目录：集中声明每个 Endpoint/ActionKey 归属的审计可靠性等级。
/// </summary>
/// <remarks>
/// 目录只接受启动时显式注册的条目；查询未注册的 ActionKey 必须立即抛出异常而不是
/// 静默退化为某个默认等级，避免审计可靠性承诺被隐式削弱。目录以单例形式在模块注册阶段
/// 完成构造，重复 ActionKey 会在构造期抛出，使配置错误在宿主启动阶段即失败，
/// 而不是延迟到运行时某次审计写入才被发现。
/// </remarks>
public sealed class AuditReliabilityCatalog
{
    private readonly IReadOnlyDictionary<string, AuditReliabilityClass> _entriesByActionKey;

    /// <summary>
    /// Full.NET 官方模块当前已知的 Endpoint/ActionKey 可靠性分类清单。
    /// 新增依赖本目录的审计写入路径时必须在此登记对应条目，否则查询会立即失败。
    /// </summary>
    private static readonly AuditReliabilityCatalogEntry[] WellKnownEntries =
    [
        new("tenancy.host_tenant.disable", AuditReliabilityClass.DomainTransactional),
        new("settings.logging-diagnostic-policy.updated", AuditReliabilityClass.DomainTransactional),
        new("auditing.operation_log.write", AuditReliabilityClass.ImportantHttp),
        new("auditing.access_log.write", AuditReliabilityClass.BestEffort),
    ];

    public AuditReliabilityCatalog(IEnumerable<AuditReliabilityCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var map = new Dictionary<string, AuditReliabilityClass>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!map.TryAdd(entry.ActionKey, entry.ReliabilityClass))
            {
                throw new InvalidOperationException(
                    $"Audit reliability catalog has a duplicate ActionKey '{entry.ActionKey}'. "
                    + "Each ActionKey must resolve to exactly one reliability class.");
            }
        }

        _entriesByActionKey = map;
    }

    /// <summary>
    /// 使用 Full.NET 官方模块的已知清单构造默认目录；供模块注册阶段以单例形式装配。
    /// </summary>
    public static AuditReliabilityCatalog CreateDefault() => new(WellKnownEntries);

    /// <summary>
    /// 查询指定 ActionKey 的审计可靠性等级；未注册的 ActionKey 必须让调用方立即失败。
    /// </summary>
    /// <param name="actionKey">稳定的审计 ActionKey。</param>
    /// <exception cref="InvalidOperationException">
    /// ActionKey 未在目录中注册时抛出；调用方不得吞掉该异常并回退到默认可靠性等级。
    /// </exception>
    public AuditReliabilityClass GetRequired(string actionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionKey);
        if (_entriesByActionKey.TryGetValue(actionKey, out var reliabilityClass))
        {
            return reliabilityClass;
        }

        throw new InvalidOperationException(
            $"Unknown audit ActionKey '{actionKey}'. Register it in "
            + $"{nameof(AuditReliabilityCatalog)} before using it; unknown ActionKeys must "
            + "fail fast instead of silently defaulting to a reliability class.");
    }
}
