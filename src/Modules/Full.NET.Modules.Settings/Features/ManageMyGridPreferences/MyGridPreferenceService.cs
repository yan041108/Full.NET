using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Catalogs;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;
using Full.NET.Modules.Settings.Serialization;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Settings.Features.ManageMyGridPreferences;

/// <summary>管理已验证当前用户的 Grid 展示偏好。</summary>
internal sealed class MyGridPreferenceService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HybridCache cache,
    IHostEnvironment environment,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = TimeSpan.FromDays(7),
        LocalCacheExpiration = TimeSpan.FromMinutes(15),
    };

    public async Task<Result<GridPreferenceResponse>> GetAsync(
        Guid userId,
        string gridKey,
        CancellationToken cancellationToken = default)
    {
        if (!GridPreferenceCatalog.TryGet(gridKey, out var definition))
        {
            return GridNotFound();
        }

        var response = await cache.GetOrCreateAsync<
                GridPreferenceLoadState,
                GridPreferenceResponse>(
                CreateCacheKey(userId, definition),
                new GridPreferenceLoadState(this, userId, definition),
                static async (state, token) =>
                    await state.Service.LoadAsync(
                            state.UserId,
                            state.Definition,
                            token)
                        .ConfigureAwait(false),
                CacheOptions,
                tags: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Result<GridPreferenceResponse>.Success(response);
    }

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
            await cache.RemoveAsync(
                    CreateCacheKey(userId, definition),
                    CancellationToken.None)
                .ConfigureAwait(false);
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
                            new { UserId = userId, GridKey = definition.GridKey },
                            token)
                        .ConfigureAwait(false);
                    return Result<GridPreferenceResponse>.Success(
                        GridPreferencePolicy.Default(definition));
                },
                cancellationToken)
            .ConfigureAwait(false);
        await cache.RemoveAsync(
                CreateCacheKey(userId, definition),
                CancellationToken.None)
            .ConfigureAwait(false);
        return result;
    }

    private async Task<GridPreferenceResponse> LoadAsync(
        Guid userId,
        GridPreferenceDefinition definition,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<GridPreferenceRecord>(
                GridPreferenceSql.FindByUserAndGrid,
                new { UserId = userId, GridKey = definition.GridKey },
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
                new { UserId = userId, GridKey = definition.GridKey },
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
                        new
                        {
                            Id = idGenerator.NewId(),
                            UserId = userId,
                            GridKey = definition.GridKey,
                            SchemaVersion = definition.SchemaVersion,
                            ColumnsJson = columnsJson,
                            CreatedAtUtc = now,
                            Version = nextVersion,
                        },
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
                    new
                    {
                        UserId = userId,
                        GridKey = definition.GridKey,
                        SchemaVersion = definition.SchemaVersion,
                        ColumnsJson = columnsJson,
                        UpdatedAtUtc = now,
                    },
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
                    new
                    {
                        UserId = userId,
                        GridKey = definition.GridKey,
                        SchemaVersion = definition.SchemaVersion,
                        ColumnsJson = columnsJson,
                        UpdatedAtUtc = now,
                        Version = requestedVersion,
                    },
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
        CacheKeyBuilder.ForGlobal(
            environment.EnvironmentName,
            "settings",
            "grid-preference",
            $"{userId:N}:{definition.GridKey}",
            $"v{definition.SchemaVersion}");

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

    private sealed record GridPreferenceLoadState(
        MyGridPreferenceService Service,
        Guid UserId,
        GridPreferenceDefinition Definition);
}
