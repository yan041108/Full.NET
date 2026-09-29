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
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private DiagnosticPolicySnapshot _snapshot =
        DiagnosticPolicySnapshot.CreateDefault(DateTimeOffset.UtcNow);

    public DiagnosticPolicySnapshot Current => Volatile.Read(ref _snapshot!);

    public ValueTask<DiagnosticPolicySnapshot> GetCurrentAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Volatile.Read(ref _snapshot!));

    public async ValueTask RefreshAsync(long minimumVersion, CancellationToken cancellationToken)
    {
        await ReloadAsync(minimumVersion, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask PollAuthorityAsync(CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 周期刷新只读权威配置，不广播缓存失效或重复回填所有节点的缓存。
            var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _snapshot, Materialize(document, clock.UtcNow));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "诊断策略后台回源失败；撤销临时规则。异常类型={ExceptionType}",
                exception.GetType().Name);
            Volatile.Write(ref _snapshot, DiagnosticPolicySnapshot.CreateDefault(clock.UtcNow));
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task<DiagnosticPolicySnapshot> ReloadAsync(
        long minimumVersion,
        CancellationToken cancellationToken)
    {
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReloadCoreAsync(minimumVersion, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private async Task<DiagnosticPolicySnapshot> ReloadCoreAsync(
        long minimumVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = policies.GetRequired(CacheEntryNames.DiagnosticPolicy);
            var key = DiagnosticPolicyCacheInvalidator.BuildCacheKey(environment.EnvironmentName);
            // 提交后失效由管理服务负责；此处直接回源，避免重复广播缓存失效。
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception setException)
            {
                logger.LogWarning(setException, "诊断策略缓存回填失败；进程内快照仍已更新。");
            }

            // 每次直接权威回源已串行化；文档版本在恢复默认后可重新从 1 开始。
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "诊断策略缓存路径失败；尝试直接回源权威配置。");
            try
            {
                var document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
                var snapshot = Materialize(document, clock.UtcNow);
                Volatile.Write(ref _snapshot, snapshot);
                return snapshot;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception loadException)
            {
                logger.LogWarning(loadException, "诊断策略权威回源失败；回退生产安全默认值。");
                var fallback = DiagnosticPolicySnapshot.CreateDefault(clock.UtcNow);
                // 权威配置不可用时撤销临时放宽，避免继续使用已失效的诊断规则。
                Volatile.Write(ref _snapshot, fallback);
                return fallback;
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
