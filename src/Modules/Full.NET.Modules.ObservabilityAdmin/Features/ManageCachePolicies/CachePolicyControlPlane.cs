using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Abstractions.Time;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;

/// <summary>提供缓存策略目录查询与登记失效执行，禁止 SCAN、全库清空或任意键写入。</summary>
internal sealed class CachePolicyControlPlane
{
    private readonly ICachePolicyRegistry _policies;
    private readonly IClock _clock;
    private readonly IOptions<CacheOptions> _options;
    private readonly IDiagnosticPolicyStore _diagnosticPolicyStore;
    private readonly IGridPreferenceCacheInvalidator? _gridPreferences;
    private readonly CacheRuntimeObservationState? _observation;

    /// <summary>创建缓存策略控制面。</summary>
    /// <param name="policies">受治理缓存策略注册表。</param>
    /// <param name="clock">快照年龄的同源时钟。</param>
    /// <param name="options">真实快照最大年龄配置。</param>
    /// <param name="diagnosticPolicyStore">实际生效的本机诊断快照边界。</param>
    /// <param name="gridPreferences">可选 Settings 所有者入口；模块缺席时不提供 Grid 失效。</param>
    /// <param name="observation">当前节点的实际 SDK 事件观察生命周期，不代表集群健康。</param>
    public CachePolicyControlPlane(
        ICachePolicyRegistry policies,
        IClock clock,
        IOptions<CacheOptions> options,
        IDiagnosticPolicyStore diagnosticPolicyStore,
        IGridPreferenceCacheInvalidator? gridPreferences = null,
        CacheRuntimeObservationState? observation = null)
    {
        _policies = policies;
        _clock = clock;
        _options = options;
        _diagnosticPolicyStore = diagnosticPolicyStore;
        _gridPreferences = gridPreferences;
        _observation = observation;
    }

    /// <summary>返回有界策略目录，不包含缓存值或连接凭据。</summary>
    public IReadOnlyList<CachePolicySummary> List() =>
        _policies.ListPolicies()
            .Select(ToSummary)
            .ToArray();

    /// <summary>返回单条策略详情；未知条目返回 <see langword="null"/>。</summary>
    public CachePolicySummary? Get(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return null;
        }

        try
        {
            return ToSummary(_policies.GetRequired(entryName.Trim()));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>执行登记的精确失效操作。</summary>
    /// <param name="entryName">目标缓存条目名。</param>
    /// <param name="request">失效请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>失效结果；参数或操作无效时返回 <see langword="null"/>。</returns>
    public async Task<CacheInvalidationResult?> InvalidateAsync(
        string entryName,
        CacheInvalidationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entryName)
            || string.IsNullOrWhiteSpace(request.OperationKey))
        {
            return null;
        }

        var normalizedEntry = entryName.Trim().ToLowerInvariant();
        var summary = Get(normalizedEntry);
        if (summary is null || !summary.CanInvalidate)
        {
            return null;
        }

        var operation = CachePolicyAdminCatalog.FindOperation(
            normalizedEntry,
            request.OperationKey.Trim());
        if (operation is null)
        {
            return null;
        }

        var scope = ParseScope(request.Scope ?? (normalizedEntry == CacheEntryNames.DiagnosticPolicy
            ? CacheInvalidationScopeNames.CurrentNodeOnly : null));
        if (scope is null || (normalizedEntry == CacheEntryNames.DiagnosticPolicy
            && scope != CacheInvalidationScope.CurrentNodeOnly))
        {
            return null;
        }

        var parameters = request.Parameters ?? new Dictionary<string, string>();
        if (!TryValidateParameters(operation, parameters, out var validated))
        {
            return null;
        }

        var invalidatedTargets = normalizedEntry switch
        {
            CacheEntryNames.DiagnosticPolicy => await InvalidateDiagnosticPolicyAsync(
                cancellationToken).ConfigureAwait(false),
            CacheEntryNames.GridPreference => await InvalidateGridPreferenceAsync(
                validated,
                scope.Value,
                cancellationToken).ConfigureAwait(false),
            _ => null,
        };
        if (invalidatedTargets is null)
        {
            return null;
        }

        return new CacheInvalidationResult(
            normalizedEntry,
            operation.OperationKey,
            ToScopeName(scope.Value),
            invalidatedTargets);
    }

    private CachePolicySummary ToSummary(CacheEntryPolicy policy)
    {
        var access = _policies.ResolveAccess(policy.EntryName);
        var operations = string.Equals(policy.EntryName, CacheEntryNames.GridPreference, StringComparison.OrdinalIgnoreCase)
            && _gridPreferences is null
            ? Array.Empty<CacheInvalidationOperationSummary>()
            : CachePolicyAdminCatalog.GetOperations(policy.EntryName);
        var isSnapshot = policy.EntryName == CacheEntryNames.DiagnosticPolicy;
        var snapshot = isSnapshot ? _diagnosticPolicyStore.Current : null;
        var canUseCache = access.Kind == CacheAccessKind.UseCache;
        return new CachePolicySummary(
            policy.EntryName,
            policy.OwnerModule,
            policy.ConsistencyClassTag,
            isSnapshot ? CachePolicyAccessKinds.LocalSnapshot : ToAccessKindName(access.Kind),
            canUseCache && policy.ConsistencyClass != CacheConsistencyClass.SharedL2Only
                ? (long)policy.L1Duration.TotalSeconds
                : null,
            canUseCache ? (long)policy.L2Duration.TotalSeconds : null,
            policy.RequiresDirectInvalidation,
            // 权威读取没有待失效的副本；快照仅提供真实本机刷新。
            operations.Count > 0,
            operations)
        {
            JitterSeconds = canUseCache ? policy.Jitter.TotalSeconds : null,
            NegativeDurationSeconds = canUseCache ? policy.NegativeDuration?.TotalSeconds : null,
            FailSafeEnabled = canUseCache && policy.FailSafeEnabled,
            FailSafeMaxDurationSeconds = canUseCache && policy.FailSafeEnabled
                ? policy.FailSafeMaxDuration?.TotalSeconds : null,
            RequiresVersionRecheck = policy.RequiresVersionRecheck,
            MaxSerializedBytes = canUseCache ? policy.MaxSerializedBytes : null,
            RuntimeStatus = isSnapshot ? snapshot is null ? "unknown" : snapshot.IsDefault ? "safe_default" : "snapshot_loaded"
                : canUseCache ? _observation?.Status ?? "unknown" : "not_applicable",
            SnapshotAgeSeconds = snapshot is null ? null : Math.Max(0, (_clock.UtcNow - snapshot.LoadedAtUtc).TotalSeconds),
            SnapshotMaxAgeSeconds = isSnapshot ? _options.Value.DiagnosticSnapshotMaxAge.TotalSeconds : null,
        };
    }

    private async Task<IReadOnlyList<string>?> InvalidateDiagnosticPolicyAsync(
        CancellationToken cancellationToken)
    {
        // 其他节点由各自权威轮询收敛；本操作不发布无消费者的缓存通知。
        await _diagnosticPolicyStore.RefreshAsync(0, cancellationToken).ConfigureAwait(false);
        return [$"snapshot:{CacheEntryNames.DiagnosticPolicy}:current-node"];
    }

    private async Task<IReadOnlyList<string>?> InvalidateGridPreferenceAsync(
        IReadOnlyDictionary<string, string> parameters,
        CacheInvalidationScope scope,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(parameters["userId"], out var userId) || userId == Guid.Empty)
        {
            return null;
        }

        var gridKey = parameters["gridKey"];
        if (_gridPreferences is null || !await _gridPreferences.InvalidateAsync(userId, gridKey,
                scope == CacheInvalidationScope.AllLayersSynchronous, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        var targets = new List<string>
        {
            $"key:{CacheEntryNames.GridPreference}:by-user-grid",
        };

        return targets;
    }

    private static bool TryValidateParameters(
        CacheInvalidationOperationSummary operation,
        IReadOnlyDictionary<string, string> parameters,
        out IReadOnlyDictionary<string, string> validated)
    {
        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in operation.Parameters)
        {
            if (!parameters.TryGetValue(definition.Name, out var raw)
                || string.IsNullOrWhiteSpace(raw))
            {
                if (definition.Required)
                {
                    validated = normalized;
                    return false;
                }

                continue;
            }

            var value = raw.Trim();
            // 在构造键或访问缓存前拒绝无界、控制字符及分隔符输入。
            if (value.Length == 0 || raw.Length > 1_024 || value.Length > 253
                || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character == ':'))
            {
                validated = normalized;
                return false;
            }
            normalized[definition.Name] = value;
        }

        if (parameters.Keys.Any(key => operation.Parameters.All(parameter => parameter.Name != key)))
        {
            validated = normalized;
            return false;
        }

        validated = normalized;
        return true;
    }

    private static CacheInvalidationScope? ParseScope(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CacheInvalidationScope.AllLayersSynchronous;
        }

        return raw.Trim() switch
        {
            CacheInvalidationScopeNames.CurrentNodeOnly => CacheInvalidationScope.CurrentNodeOnly,
            CacheInvalidationScopeNames.AllLayersSynchronous => CacheInvalidationScope.AllLayersSynchronous,
            _ => null,
        };
    }

    private static string ToScopeName(CacheInvalidationScope scope) =>
        scope switch
        {
            CacheInvalidationScope.CurrentNodeOnly => CacheInvalidationScopeNames.CurrentNodeOnly,
            CacheInvalidationScope.AllLayersSynchronous => CacheInvalidationScopeNames.AllLayersSynchronous,
            _ => CacheInvalidationScopeNames.AllLayersSynchronous,
        };

    private static string ToAccessKindName(CacheAccessKind kind) =>
        kind switch
        {
            CacheAccessKind.AuthorityRead => CachePolicyAccessKinds.AuthorityRead,
            CacheAccessKind.Bypass => CachePolicyAccessKinds.Bypass,
            _ => CachePolicyAccessKinds.UseCache,
        };
}
