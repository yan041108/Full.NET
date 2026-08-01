using System.Collections.Immutable;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Persistence;
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
            var options = policies.CreateEntryOptions(CacheEntryNames.DiagnosticPolicy);
            var document = await cache.GetOrSetAsync<DiagnosticPolicyDocument?>(
                    key,
                    async (_, token) => await LoadDocumentAsync(token).ConfigureAwait(false),
                    options,
                    token: cancellationToken)
                .ConfigureAwait(false);

            var snapshot = Materialize(document, clock.UtcNow);
            if (snapshot.Version < minimumVersion)
            {
                await cache.RemoveAsync(key, token: cancellationToken).ConfigureAwait(false);
                document = await LoadDocumentAsync(cancellationToken).ConfigureAwait(false);
                snapshot = Materialize(document, clock.UtcNow);
            }

            lock (_gate)
            {
                if (snapshot.Version >= _snapshot.Version)
                {
                    _snapshot = snapshot;
                }

                return _snapshot;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "诊断策略加载失败；回退生产安全默认值。");
            var fallback = DiagnosticPolicySnapshot.CreateDefault(clock.UtcNow);
            Volatile.Write(ref _snapshot, fallback);
            return fallback;
        }
    }

    private async Task<DiagnosticPolicyDocument?> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var queryExecutor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryValueRow>(
                ConfigEntrySql.FindByKey,
                new { ConfigKey = DiagnosticPolicyLimits.ConfigKey },
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null || string.IsNullOrWhiteSpace(row.Value) || !row.IsActive)
        {
            return null;
        }

        return JsonSerializer.Deserialize<DiagnosticPolicyDocument>(row.Value, JsonOptions);
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

    private sealed record ConfigEntryValueRow(
        Guid Id,
        string ConfigKey,
        string DisplayName,
        string? Description,
        string ValueKind,
        string Value,
        int DisplayOrder,
        bool IsActive,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        int Version);
}
