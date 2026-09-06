using System.Diagnostics;
using Full.NET.Caching.Abstractions;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.ObservabilityAdmin.Features.ManageCachePolicies;

/// <summary>提供缓存策略目录查询与登记失效执行，禁止 SCAN、全库清空或任意键写入。</summary>
internal sealed class CachePolicyControlPlane
{
    private readonly ICachePolicyRegistry _policies;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IHostEnvironment _environment;

    /// <summary>创建缓存策略控制面。</summary>
    /// <param name="policies">受治理缓存策略注册表。</param>
    /// <param name="cacheInvalidator">受治理缓存失效边界。</param>
    /// <param name="environment">宿主环境，用于构造环境隔离键。</param>
    public CachePolicyControlPlane(
        ICachePolicyRegistry policies,
        ICacheInvalidator cacheInvalidator,
        IHostEnvironment environment)
    {
        _policies = policies;
        _cacheInvalidator = cacheInvalidator;
        _environment = environment;
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

        var normalizedEntry = entryName.Trim();
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

        var scope = ParseScope(request.Scope);
        if (scope is null)
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
            CacheEntryNames.TenantResolution => await InvalidateTenantResolutionAsync(
                validated,
                scope.Value,
                cancellationToken).ConfigureAwait(false),
            CacheEntryNames.DiagnosticPolicy => await InvalidateDiagnosticPolicyAsync(
                scope.Value,
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
        var operations = CachePolicyAdminCatalog.GetOperations(policy.EntryName);
        var canUseCache = access.Kind == CacheAccessKind.UseCache;
        return new CachePolicySummary(
            policy.EntryName,
            policy.OwnerModule,
            policy.ConsistencyClassTag,
            ToAccessKindName(access.Kind),
            canUseCache && policy.ConsistencyClass != CacheConsistencyClass.SharedL2Only
                ? (long)policy.L1Duration.TotalSeconds
                : null,
            canUseCache ? (long)policy.L2Duration.TotalSeconds : null,
            policy.RequiresDirectInvalidation,
            canUseCache && operations.Count > 0,
            operations);
    }

    private async Task<IReadOnlyList<string>?> InvalidateTenantResolutionAsync(
        IReadOnlyDictionary<string, string> parameters,
        CacheInvalidationScope scope,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(parameters["tenantId"], out var tenantId) || tenantId == Guid.Empty)
        {
            return null;
        }

        var domain = parameters["domain"];
        var environmentName = _environment.EnvironmentName;
        var targets = new List<string>
        {
            $"key:{CacheEntryNames.TenantResolution}:by-id",
            $"key:{CacheEntryNames.TenantResolution}:by-domain",
            $"tag:{CacheEntryNames.TenantResolution}:tenant",
            $"tag:{CacheEntryNames.TenantResolution}:domain",
        };

        await ExecuteInvalidationAsync(
                CacheEntryNames.TenantResolution,
                "tenancy",
                scope,
                async token =>
                {
                    await _cacheInvalidator.RemoveAsync(
                            CacheEntryNames.TenantResolution,
                            CacheKeyBuilder.TenantResolutionById(environmentName, tenantId),
                            scope,
                            token)
                        .ConfigureAwait(false);
                    await _cacheInvalidator.RemoveAsync(
                            CacheEntryNames.TenantResolution,
                            CacheKeyBuilder.TenantResolutionByDomain(environmentName, domain),
                            scope,
                            token)
                        .ConfigureAwait(false);
                    await _cacheInvalidator.RemoveByTagAsync(
                            CacheEntryNames.TenantResolution,
                            CacheKeyBuilder.TenantTag(tenantId),
                            scope,
                            token)
                        .ConfigureAwait(false);
                    await _cacheInvalidator.RemoveByTagAsync(
                            CacheEntryNames.TenantResolution,
                            CacheKeyBuilder.DomainTag(domain),
                            scope,
                            token)
                        .ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return targets;
    }

    private async Task<IReadOnlyList<string>?> InvalidateDiagnosticPolicyAsync(
        CacheInvalidationScope scope,
        CancellationToken cancellationToken)
    {
        var targets = new List<string>
        {
            $"key:{CacheEntryNames.DiagnosticPolicy}:global",
        };

        await ExecuteInvalidationAsync(
                CacheEntryNames.DiagnosticPolicy,
                "settings",
                scope,
                token => _cacheInvalidator.RemoveAsync(
                    CacheEntryNames.DiagnosticPolicy,
                    CacheKeyBuilder.ForGlobal(
                        _environment.EnvironmentName,
                        "settings",
                        "diagnostic-policy",
                        "current",
                        "v1"),
                    scope,
                    token),
                cancellationToken)
            .ConfigureAwait(false);

        return targets;
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
        if (!CachePolicyAdminCatalog.AllowedGridKeys.Contains(gridKey))
        {
            return null;
        }

        var schemaVersion = gridKey switch
        {
            "identity.users" => 2,
            _ => 0,
        };
        if (schemaVersion <= 0)
        {
            return null;
        }

        var key = CacheKeyBuilder.ForGlobal(
            _environment.EnvironmentName,
            "settings",
            "grid-preference",
            $"{userId:N}:{gridKey}",
            $"v{schemaVersion}");
        var targets = new List<string>
        {
            $"key:{CacheEntryNames.GridPreference}:by-user-grid",
        };

        await ExecuteInvalidationAsync(
                CacheEntryNames.GridPreference,
                "settings",
                scope,
                token => _cacheInvalidator.RemoveAsync(
                    CacheEntryNames.GridPreference,
                    key,
                    scope,
                    token),
                cancellationToken)
            .ConfigureAwait(false);

        return targets;
    }

    private static async Task ExecuteInvalidationAsync(
        string entryName,
        string ownerModule,
        CacheInvalidationScope scope,
        Func<CancellationToken, ValueTask> execute,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await execute(cancellationToken).ConfigureAwait(false);
            RecordInvalidation(ownerModule, scope, Stopwatch.GetElapsedTime(startedAt), succeeded: true);
        }
        catch
        {
            RecordInvalidation(ownerModule, scope, Stopwatch.GetElapsedTime(startedAt), succeeded: false);
            throw;
        }
    }

    private static void RecordInvalidation(
        string ownerModule,
        CacheInvalidationScope scope,
        TimeSpan duration,
        bool succeeded)
    {
        if (scope == CacheInvalidationScope.AllLayersSynchronous)
        {
            CacheReliabilityTelemetry.RecordDistributedInvalidation(duration, succeeded);
        }
        else
        {
            CacheReliabilityTelemetry.RecordLocalInvalidation(duration, succeeded);
        }

        CacheReliabilityTelemetry.RecordPolicyEvent(
            ownerModule,
            "admin",
            scope == CacheInvalidationScope.AllLayersSynchronous
                ? "invalidate_admin_distributed"
                : "invalidate_admin_local",
            succeeded ? "success" : "failure");
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

            normalized[definition.Name] = definition.ValueType switch
            {
                CacheInvalidationParameterTypes.Domain => raw.Trim().TrimEnd('.').ToLowerInvariant(),
                CacheInvalidationParameterTypes.GridKey => raw.Trim(),
                _ => raw.Trim(),
            };
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
