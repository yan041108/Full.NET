using Full.NET.Caching.Fusion;

namespace Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;

/// <summary>维护已登记缓存策略与精确失效操作目录，不暴露缓存值或 Redis 凭据。</summary>
internal static class CachePolicyAdminCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<CacheInvalidationOperationSummary>>
        Operations = new Dictionary<string, IReadOnlyList<CacheInvalidationOperationSummary>>(
            StringComparer.OrdinalIgnoreCase)
        {
            [CacheEntryNames.TenantResolution] =
            [
                new CacheInvalidationOperationSummary(
                    "by-tenant",
                    "按租户失效解析缓存",
                    [
                        new CacheInvalidationParameterSummary(
                            "tenantId",
                            CacheInvalidationParameterTypes.Uuid,
                            true),
                        new CacheInvalidationParameterSummary(
                            "domain",
                            CacheInvalidationParameterTypes.Domain,
                            true),
                    ]),
            ],
            [CacheEntryNames.DiagnosticPolicy] =
            [
                new CacheInvalidationOperationSummary(
                    "global",
                    "失效全局诊断策略快照",
                    []),
            ],
            [CacheEntryNames.GridPreference] =
            [
                new CacheInvalidationOperationSummary(
                    "by-user-grid",
                    "按用户与 Grid 失效展示偏好",
                    [
                        new CacheInvalidationParameterSummary(
                            "userId",
                            CacheInvalidationParameterTypes.Uuid,
                            true),
                        new CacheInvalidationParameterSummary(
                            "gridKey",
                            CacheInvalidationParameterTypes.GridKey,
                            true),
                    ]),
            ],
        };

    /// <summary>当前允许通过管理控制面失效的 Grid 键，需与 Settings 模块目录保持一致。</summary>
    public static IReadOnlySet<string> AllowedGridKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "identity.users",
        };

    /// <summary>返回指定条目的登记失效操作；未知条目返回空集合。</summary>
    public static IReadOnlyList<CacheInvalidationOperationSummary> GetOperations(string entryName) =>
        Operations.TryGetValue(entryName, out var operations)
            ? operations
            : Array.Empty<CacheInvalidationOperationSummary>();

    /// <summary>查找指定条目下的登记操作定义。</summary>
    public static CacheInvalidationOperationSummary? FindOperation(
        string entryName,
        string operationKey)
    {
        foreach (var operation in GetOperations(entryName))
        {
            if (string.Equals(operation.OperationKey, operationKey, StringComparison.OrdinalIgnoreCase))
            {
                return operation;
            }
        }

        return null;
    }
}
