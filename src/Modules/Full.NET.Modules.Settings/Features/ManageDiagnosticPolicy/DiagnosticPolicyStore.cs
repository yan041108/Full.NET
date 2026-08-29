using Full.NET.Abstractions.Tenancy;
using System.Collections.Immutable;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Features.ManageHostConfigEntries;
using Full.NET.Modules.Settings.Persistence;
using Full.NET.Modules.Settings.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;

/// <summary>
/// 诊断策略快照存储：从固定配置键加载 JSON，过期规则剔除后物化为不可变快照。
/// </summary>
internal sealed class DiagnosticPolicyStore(
    IServiceScopeFactory scopeFactory,
    IFusionCache cache,
    IHostEnvironment environment,
    ICachePolicyRegistry policies,
    IClock clock,
    ILogger<DiagnosticPolicyStore> logger) : IDiagnosticPolicyStore
{
    private readonly object _gate = new();
    private DiagnosticPolicySnapshot _snapshot =
        DiagnosticPolicySnapshot.CreateDefault(DateTimeOffset.UtcNow);

    public DiagnosticPolicySnapshot Current => Volatile.Read(ref _snapshot!);

    public ValueTask<DiagnosticPolicySnapshot> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _snapshot!);
        if (!current.IsDefault && current.ActiveRules.All(rule => rule.ExpiresAtUtc > clock.UtcNow))
        {
            return ValueTask.FromResult(current);
        }

        return new ValueTask<DiagnosticPolicySnapshot>(ReloadAsync(minimumVersion: 0, cancellationToken));
    }

    public async ValueTask RefreshAsync(long minimumVersion, CancellationToken cancellationToken)
    {
        await ReloadAsync(minimumVersion, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DiagnosticPolicySnapshot> ReloadAsync(
        long minimumVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = policies.GetRequired(CacheEntryNames.DiagnosticPolicy);
            var key = DiagnosticPolicyCacheInvalidator.BuildCacheKey(environment.EnvironmentName);
            // 显式 Refresh 必须绕过可能残留的 L2/负缓存，直接回源后再回填。
            try
            {
                await cache.RemoveAsync(key, token: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception removeException)
            {
                logger.LogWarning(removeException, "诊断策略缓存删除失败；继续权威回源。");
            }

            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = Materialize(document, clock.UtcNow);
            if (snapshot.Version < minimumVersion)
            {
                document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
                snapshot = Materialize(document, clock.UtcNow);
            }

            var options = policies.CreateEntryOptions(CacheEntryNames.DiagnosticPolicy);
            try
            {
                await cache.SetAsync(key, document, options, token: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception setException)
            {
                logger.LogWarning(setException, "诊断策略缓存回填失败；进程内快照仍已更新。");
            }

            lock (_gate)
            {
                // 恢复到安全默认（Version 0）时必须能覆盖仍驻留的临时策略快照。
                if (snapshot.IsDefault || snapshot.Version >= _snapshot.Version)
                {
                    _snapshot = snapshot;
                }

                return _snapshot;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "诊断策略缓存路径失败；尝试直接回源权威配置。");
            try
            {
                var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
                var snapshot = Materialize(document, clock.UtcNow);
                lock (_gate)
                {
                    if (snapshot.Version >= _snapshot.Version)
                    {
                        _snapshot = snapshot;
                    }

                    return _snapshot;
                }
            }
            catch (Exception loadException)
            {
                logger.LogWarning(loadException, "诊断策略权威回源失败；回退生产安全默认值。");
                var fallback = DiagnosticPolicySnapshot.CreateDefault(clock.UtcNow);
                lock (_gate)
                {
                    if (_snapshot.IsDefault || _snapshot.Version == 0)
                    {
                        _snapshot = fallback;
                    }

                    return _snapshot.IsDefault ? fallback : _snapshot;
                }
            }
        }
    }

    private async Task<DiagnosticPolicyDocument?> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        // 后台/跨作用域回源必须显式进入 Host 上下文：FindByKey 是 HostOnly，
        // 新建 scope 不会继承请求态租户，否则会被 catch 成安全默认并掩盖已持久化策略。
        await using var scope = scopeFactory.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            var queryExecutor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var row = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryRecord>(
                    ConfigEntrySql.FindByKey,
                    SettingsSqlParameters.Create(("ConfigKey", DiagnosticPolicyLimits.ConfigKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (row is null || string.IsNullOrWhiteSpace(row.Value) || !row.IsActive)
            {
                return null;
            }

            return JsonSerializer.Deserialize(
                row.Value,
                SettingsJsonSerializerContext.Default.DiagnosticPolicyDocument);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    internal static DiagnosticPolicySnapshot Materialize(
        DiagnosticPolicyDocument? document,
        DateTimeOffset utcNow)
    {
        // 恢复写入的空文档与缺失配置等价，必须回到生产安全默认快照。
        if (document is null
            || (document.Version == 0
                && document.PressureState == LoggingPressureState.Normal
                && document.Rules.Count == 0))
        {
            return DiagnosticPolicySnapshot.CreateDefault(utcNow);
        }

        var active = document.Rules
            .Where(rule => rule.ExpiresAtUtc > utcNow)
            .ToImmutableArray();
        return new DiagnosticPolicySnapshot(
            document.Version,
            document.PressureState,
            active,
            utcNow,
            IsDefault: false);
    }
}
