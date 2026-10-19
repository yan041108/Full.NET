using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Caching.Fusion.Serialization;
using Full.NET.Caching.Abstractions;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Catalogs;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;
using Full.NET.Modules.Settings.Serialization;
using ZiggyCreatures.Caching.Fusion;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Settings.Features.ManageMyGridPreferences;

/// <summary>管理已验证当前用户的 Grid 展示偏好。</summary>
internal sealed class MyGridPreferenceService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IFusionCache cache,
    ICachePolicyRegistry policies,
    IHostEnvironment environment,
    IClock clock,
    IIdGenerator idGenerator,
    ICacheInvalidator invalidator,
    IOptions<CacheOptions> cacheOptions,
    ILogger<MyGridPreferenceService> logger)
{
    public async Task<Result<GridPreferenceResponse>> GetAsync(
        Guid userId,
        string gridKey,
        CancellationToken cancellationToken = default)
    {
        if (!GridPreferenceCatalog.TryGet(gridKey, out var definition))
        {
            return GridNotFound();
        }

        // 策略降为 C0/N0 或运维关闭缓存时不得再命中 HybridCache，避免展示类条目在强一致窗口内漂移。
        if (policies.ResolveAccess(CacheEntryNames.GridPreference).Kind != CacheAccessKind.UseCache)
        {
            return Result<GridPreferenceResponse>.Success(
                await LoadAsync(userId, definition, cancellationToken).ConfigureAwait(false));
        }

        var policy = policies.GetRequired(CacheEntryNames.GridPreference);
        GridPreferenceResponse? loadedFromAuthority = null;
        GridPreferenceResponse response;
        try
        {
            response = await cache.GetOrSetAsync<GridPreferenceResponse>(
                CreateCacheKey(userId, definition),
                async (context, token) =>
                {
                    var fresh = await LoadAsync(userId, definition, token).ConfigureAwait(false);
                    if (ExceedsPayloadBudget(fresh, policy))
                    {
                        // 仅返回本次权威结果并禁止两层回填；SDK 等待者仍可能再次回源，不保证超限结果只加载一次。
                        context.Options.SkipMemoryCacheWrite = true;
                        context.Options.SkipDistributedCacheWrite = true;
                        RecordPayloadRejection(policy, "fill");
                    }
                    loadedFromAuthority = fresh;
                    return fresh;
                },
                options: policies.CreateEntryOptions(CacheEntryNames.GridPreference),
                token: cancellationToken)
            .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is CacheDeserializationLimitException
            or FusionCacheSerializationException { InnerException: CacheDeserializationLimitException })
        {
            // 原始 L2 超限在 JSON 解析前已拒绝；展示缓存退回权威源，不把此拒绝变成请求失败。
            RecordPayloadRejection(policy, "read");
            await InvalidateBestEffortAsync(userId, definition).ConfigureAwait(false);
            return Result<GridPreferenceResponse>.Success(
                await LoadAsync(userId, definition, cancellationToken).ConfigureAwait(false));
        }
        if (!ReferenceEquals(response, loadedFromAuthority) && ExceedsPayloadBudget(response, policy))
        {
            // 配置收紧后旧值可能尚未过期；不向调用方返回超限旧值，也不依赖删除成功才能回源。
            RecordPayloadRejection(policy, "read");
            await InvalidateBestEffortAsync(userId, definition).ConfigureAwait(false);
            response = await LoadAsync(userId, definition, cancellationToken).ConfigureAwait(false);
        }
        return Result<GridPreferenceResponse>.Success(response);
    }

    private static bool ExceedsPayloadBudget(GridPreferenceResponse response, CacheEntryPolicy policy) =>
        JsonSerializer.SerializeToUtf8Bytes(response, SettingsJsonSerializerContext.Default.GridPreferenceResponse)
            .Length > policy.MaxSerializedBytes;

    private static void RecordPayloadRejection(CacheEntryPolicy policy, string operation) =>
        CacheReliabilityTelemetry.RecordPolicyEvent(policy.OwnerModule, policy.ConsistencyClassTag,
            $"payload_{operation}", "oversized");

    public async Task<Result<GridPreferenceResponse>> PutAsync(
        Guid userId,
        string gridKey,
        UpdateGridPreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!GridPreferenceCatalog.TryGet(gridKey, out var definition))
        {
            return GridNotFound();
        }

        var normalized = GridPreferencePolicy.ValidateAndNormalize(definition, request);
        if (!normalized.IsSuccess)
        {
            return Result<GridPreferenceResponse>.Failure(normalized.Error!);
        }

        var result = await transaction.ExecuteAsync(
                token => PutCoreAsync(
                    userId,
                    definition,
                    request.Version,
                    normalized.Value!,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await InvalidateBestEffortAsync(userId, definition).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<Result<GridPreferenceResponse>> DeleteAsync(
        Guid userId,
        string gridKey,
        CancellationToken cancellationToken = default)
    {
        if (!GridPreferenceCatalog.TryGet(gridKey, out var definition))
        {
            return GridNotFound();
        }

        var result = await transaction.ExecuteAsync(
                async token =>
                {
                    await commandExecutor.ExecuteAsync(
                            GridPreferenceSql.Delete,
                            SettingsSqlParameters.Create(
                                ("UserId", userId),
                                ("GridKey", definition.GridKey)
                            ),
                            token)
                        .ConfigureAwait(false);
                    return Result<GridPreferenceResponse>.Success(
                        GridPreferencePolicy.Default(definition));
                },
                cancellationToken)
            .ConfigureAwait(false);
        await InvalidateBestEffortAsync(userId, definition).ConfigureAwait(false);
        return result;
    }

    private async Task InvalidateBestEffortAsync(Guid userId, GridPreferenceDefinition definition)
    {
        using var cleanup = new CancellationTokenSource(cacheOptions.Value.InvalidationTimeout);
        var key = CreateCacheKey(userId, definition);
        foreach (var scope in new[] { CacheInvalidationScope.CurrentNodeOnly, CacheInvalidationScope.AllLayersSynchronous })
        {
            try
            {
                await invalidator.RemoveAsync(CacheEntryNames.GridPreference, key, scope, cleanup.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // 提交后写入或旧值拒绝已经确定，清理故障由 TTL 收敛并告警；不掩盖业务结果。
                logger.LogWarning("Grid 偏好缓存清理失败。Scope={Scope} ExceptionType={ExceptionType}",
                    scope, exception.GetType().Name);
            }
        }
    }

    private async Task<GridPreferenceResponse> LoadAsync(
        Guid userId,
        GridPreferenceDefinition definition,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<GridPreferenceRecord>(
                GridPreferenceSql.FindByUserAndGrid,
                SettingsSqlParameters.Create(
                    ("UserId", userId),
                    ("GridKey", definition.GridKey)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return GridPreferencePolicy.Default(definition);
        }

        try
        {
            var columns = JsonSerializer.Deserialize(
                    record.ColumnsJson,
                    SettingsJsonSerializerContext.Default.GridColumnPreferenceArray)
                ?? [];
            return GridPreferencePolicy.Restore(
                definition,
                record.SchemaVersion,
                record.Version,
                columns);
        }
        catch (JsonException)
        {
            // 持久化数据损坏时只回退本地默认展示，绝不把不可信 JSON 透传给客户端。
            return GridPreferencePolicy.Default(definition);
        }
    }

    private async Task<Result<GridPreferenceResponse>> PutCoreAsync(
        Guid userId,
        GridPreferenceDefinition definition,
        int requestedVersion,
        IReadOnlyList<GridColumnPreference> columns,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<GridPreferenceRecord>(
                GridPreferenceSql.FindByUserAndGrid,
                SettingsSqlParameters.Create(
                    ("UserId", userId),
                    ("GridKey", definition.GridKey)
                ),
                cancellationToken)
            .ConfigureAwait(false);
        var columnsJson = JsonSerializer.Serialize(
            columns.ToArray(),
            SettingsJsonSerializerContext.Default.GridColumnPreferenceArray);
        var now = clock.UtcNow;
        int nextVersion;

        if (existing is null)
        {
            if (requestedVersion != 0)
            {
                return VersionConflict();
            }

            nextVersion = 1;
            try
            {
                await commandExecutor.ExecuteAsync(
                        GridPreferenceSql.Insert,
                        SettingsSqlParameters.Create(
                            ("Id", idGenerator.NewId()),
                            ("UserId", userId),
                            ("GridKey", definition.GridKey),
                            ("SchemaVersion", definition.SchemaVersion),
                            ("ColumnsJson", columnsJson),
                            ("CreatedAtUtc", now),
                            ("Version", nextVersion)
                        ),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DataCommandException exception)
                when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
            {
                return VersionConflict();
            }
        }
        else if (existing.SchemaVersion != definition.SchemaVersion)
        {
            if (requestedVersion != 0)
            {
                return VersionConflict();
            }

            var affected = await commandExecutor.ExecuteAsync(
                    GridPreferenceSql.ReplaceStaleSchema,
                    SettingsSqlParameters.Create(
                        ("UserId", userId),
                        ("GridKey", definition.GridKey),
                        ("SchemaVersion", definition.SchemaVersion),
                        ("ColumnsJson", columnsJson),
                        ("UpdatedAtUtc", now)
                    ),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                return VersionConflict();
            }

            nextVersion = existing.Version + 1;
        }
        else
        {
            if (requestedVersion != existing.Version)
            {
                return VersionConflict();
            }

            var affected = await commandExecutor.ExecuteAsync(
                    GridPreferenceSql.UpdateCurrentSchema,
                    SettingsSqlParameters.Create(
                        ("UserId", userId),
                        ("GridKey", definition.GridKey),
                        ("SchemaVersion", definition.SchemaVersion),
                        ("ColumnsJson", columnsJson),
                        ("UpdatedAtUtc", now),
                        ("Version", requestedVersion)
                    ),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                return VersionConflict();
            }

            nextVersion = existing.Version + 1;
        }

        return Result<GridPreferenceResponse>.Success(
            new GridPreferenceResponse(
                definition.GridKey,
                definition.SchemaVersion,
                columns,
                nextVersion));
    }

    private string CreateCacheKey(
        Guid userId,
        GridPreferenceDefinition definition) =>
        GridPreferenceCacheInvalidator.BuildCacheKey(environment.EnvironmentName, userId, definition);

    private static Result<GridPreferenceResponse> GridNotFound() =>
        Result<GridPreferenceResponse>.Failure(new Error(
            SettingsErrorCodes.GridNotFound,
            "The Grid key is not published by the local catalog.",
            ErrorType.NotFound));

    private static Result<GridPreferenceResponse> VersionConflict() =>
        Result<GridPreferenceResponse>.Failure(new Error(
            SettingsErrorCodes.GridPreferenceVersionConflict,
            "The Grid preference was updated concurrently.",
            ErrorType.Conflict));

}
