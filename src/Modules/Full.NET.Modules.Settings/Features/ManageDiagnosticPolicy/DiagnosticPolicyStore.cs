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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;

/// <summary>只读权威配置的进程内快照；后台轮询停滞或回源失败时撤销临时放宽。</summary>
internal sealed class DiagnosticPolicyStore(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<DiagnosticPolicyStore> logger,
    IOptions<CacheOptions> options) : IDiagnosticPolicyStore
{
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private DiagnosticPolicySnapshot _snapshot = DiagnosticPolicySnapshot.CreateDefault(clock.UtcNow);

    public DiagnosticPolicySnapshot Current
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            var now = clock.UtcNow;
            // 轮询停滞或时钟回拨时不得无限保留临时放宽；只撤销仍持有同一引用的读者。
            if (!snapshot.IsDefault && (now < snapshot.LoadedAtUtc
                || now - snapshot.LoadedAtUtc >= options.Value.DiagnosticSnapshotMaxAge))
            {
                var fallback = DiagnosticPolicySnapshot.CreateDefault(now);
                Interlocked.CompareExchange(ref _snapshot, fallback, snapshot);
                return Volatile.Read(ref _snapshot);
            }
            return snapshot;
        }
    }

    public ValueTask<DiagnosticPolicySnapshot> GetCurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Current);
    }

    /// <summary>后台轮询入口；minimumVersion=0 只要求读到当前权威行，不触发缓存失效。</summary>
    internal ValueTask PollAuthorityAsync(CancellationToken cancellationToken) => RefreshAsync(0, cancellationToken);

    /// <inheritdoc />
    public async ValueTask RefreshAsync(long minimumVersion, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.Value.InvalidationTimeout);
        var acquired = false;
        try
        {
            await _reloadGate.WaitAsync(budget.Token).ConfigureAwait(false);
            acquired = true;
            var document = await LoadDocumentAsync(budget.Token).ConfigureAwait(false);
            var snapshot = Materialize(document, clock.UtcNow);
            if (snapshot.Version < minimumVersion)
            {
                document = await LoadDocumentAsync(budget.Token).ConfigureAwait(false);
                snapshot = Materialize(document, clock.UtcNow);
                if (snapshot.Version < minimumVersion)
                    throw new InvalidOperationException("Diagnostic policy authority version is behind the committed version.");
            }
            // 直接权威读取已串行化；不向从未读取的 Redis 文档做无效回填或广播。
            Volatile.Write(ref _snapshot, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("诊断策略回源失败；回退生产安全默认值。异常类型={ExceptionType}", exception.GetType().Name);
            Volatile.Write(ref _snapshot, DiagnosticPolicySnapshot.CreateDefault(clock.UtcNow));
        }
        finally
        {
            if (acquired) _reloadGate.Release();
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
