using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;
using Full.NET.Modules.Settings.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Settings.Features.ManageQuerySchemes;

/// <summary>管理偏好事实；先完成业务权威授权，再在 Settings 自己的事务中串行化个人写入。</summary>
internal sealed class QuerySchemeService(QuerySchemeCatalog catalog, ICurrentSessionAuthorization authorization,
    ICurrentTenant tenant, IQueryExecutor queries, ICommandExecutor commands, ICommandTransaction transaction,
    IOptions<DatabaseOptions> database, IIdGenerator ids, IClock clock)
{
    private sealed record Context(QuerySchemeDefinition Schema, AuthorizedSessionActor Actor, string ScopeKey, QuerySchemeStatements Sql)
    {
        internal Dictionary<string, object?> Args(params (string Name, object? Value)[] values)
        {
            var args = SettingsSqlParameters.Create(("UserId", Actor.UserId), ("ScopeKey", ScopeKey), ("GridKey", Schema.GridKey));
            foreach (var value in values) args[value.Name] = value.Value;
            return args;
        }
    }

    internal async Task<Result<QuerySchemeDefinition>> GetSchemaAsync(string gridKey, CancellationToken token)
    {
        var context = await ResolveAsync(gridKey, QuerySchemePermissions.Read, token).ConfigureAwait(false);
        return context.IsSuccess ? Result<QuerySchemeDefinition>.Success(context.Value!.Schema) : Result<QuerySchemeDefinition>.Failure(context.Error!);
    }

    internal async Task<Result<CursorPage<QuerySchemeSummary>>> ListAsync(string gridKey, bool shared, int pageSize, string? cursor, CancellationToken token)
    {
        var resolved = await ResolveAsync(gridKey, QuerySchemePermissions.Read, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<CursorPage<QuerySchemeSummary>>.Failure(resolved.Error!);
        var context = resolved.Value!;
        if (shared && context.Actor.TenantId is null) return Result<CursorPage<QuerySchemeSummary>>.Failure(Denied());
        var cursorContext = $"settings.query_scheme.v1|{context.ScopeKey}|{context.Actor.UserId:N}|{gridKey}|{shared}";
        TimeIdQueryPosition? before = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TimeIdQueryCursor.TryDecode(cursor, cursorContext, out var position)) return Result<CursorPage<QuerySchemeSummary>>.Failure(QuerySchemePolicy.InvalidError());
            before = position;
        }
        pageSize = Math.Clamp(pageSize, 1, 100);
        var statement = (shared, before is not null) switch
        {
            (false, false) => context.Sql.MineFirst, (false, true) => context.Sql.MineAfter,
            (true, false) => context.Sql.SharedFirst, _ => context.Sql.SharedAfter,
        };
        var rows = await queries.QueryAsync<QuerySchemeRecord>(statement,
            context.Args(("Take", pageSize + 1), ("BeforeCreatedAtUtc", before?.CreatedAtUtc.UtcDateTime), ("BeforeId", before?.Id)), token).ConfigureAwait(false);
        var more = rows.Count > pageSize;
        var items = rows.Take(pageSize).Select(row => Summary(row, context.Schema)).ToArray();
        var next = more ? TimeIdQueryCursor.Encode(new(items[^1].CreatedAtUtc, items[^1].Id), cursorContext) : null;
        return Result<CursorPage<QuerySchemeSummary>>.Success(new(items, next, more));
    }

    internal async Task<Result<QuerySchemeDetail>> GetAsync(string gridKey, Guid id, CancellationToken token)
    {
        if (id == Guid.Empty) return Result<QuerySchemeDetail>.Failure(QuerySchemePolicy.InvalidError());
        var resolved = await ResolveAsync(gridKey, QuerySchemePermissions.Read, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<QuerySchemeDetail>.Failure(resolved.Error!);
        var row = await FindAsync(resolved.Value!, id, token).ConfigureAwait(false);
        return row is null ? Result<QuerySchemeDetail>.Failure(NotFound()) : Result<QuerySchemeDetail>.Success(Detail(row, resolved.Value!.Schema));
    }

    internal async Task<Result<QuerySchemeDetail>> SaveAsync(string gridKey, Guid? id, SaveQuerySchemeRequest request, CancellationToken token)
    {
        var resolved = await ResolveAsync(gridKey, id is null ? QuerySchemePermissions.Create : QuerySchemePermissions.Update, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<QuerySchemeDetail>.Failure(resolved.Error!);
        var context = resolved.Value!; var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 64 || name.Any(char.IsControl) || id == Guid.Empty || (id is null ? request.Version != 0 : request.Version < 1))
            return Result<QuerySchemeDetail>.Failure(QuerySchemePolicy.InvalidError());
        var configuration = QuerySchemePolicy.Normalize(context.Schema, request.SchemaVersion, request.Configuration);
        if (!configuration.IsSuccess) return Result<QuerySchemeDetail>.Failure(configuration.Error!);
        var json = JsonSerializer.Serialize(configuration.Value!, SettingsJsonSerializerContext.Default.QuerySchemeConfiguration);
        return await transaction.ExecuteResultAsync(async cancellation =>
        {
            await LockAsync(context, cancellation).ConfigureAwait(false);
            if (id is null)
            {
                var count = await queries.QuerySingleOrDefaultAsync<long>(context.Sql.CountOwned, context.Args(), cancellation).ConfigureAwait(false);
                if (count >= QuerySchemePolicy.MaximumOwnedSchemes)
                    return Result<QuerySchemeDetail>.Failure(new(SettingsErrorCodes.QuerySchemeLimitReached, "The personal query scheme limit has been reached.", ErrorType.Validation));
            }
            else
            {
                var existing = await FindAsync(context, id.Value, cancellation).ConfigureAwait(false);
                if (existing?.OwnerUserId != context.Actor.UserId) return Result<QuerySchemeDetail>.Failure(NotFound());
                if (existing.Version != request.Version) return Result<QuerySchemeDetail>.Failure(Conflict());
            }
            var schemeId = id ?? ids.NewId();
            var changed = await commands.ExecuteAsync(id is null ? context.Sql.Insert : context.Sql.Update,
                context.Args(("Id", schemeId), ("Name", name), ("SchemaVersion", context.Schema.SchemaVersion),
                    ("ConfigurationJson", json), ("Version", request.Version), ("Now", clock.UtcNow.UtcDateTime)), cancellation).ConfigureAwait(false);
            if (changed != 1) return Result<QuerySchemeDetail>.Failure(Conflict());
            var saved = await FindAsync(context, schemeId, cancellation).ConfigureAwait(false);
            return Result<QuerySchemeDetail>.Success(Detail(saved!, context.Schema));
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<QuerySchemeDetail>> ShareAsync(string gridKey, Guid id, ShareQuerySchemeRequest request, CancellationToken token)
    {
        var resolved = await ResolveAsync(gridKey, QuerySchemePermissions.Share, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<QuerySchemeDetail>.Failure(resolved.Error!);
        var context = resolved.Value!;
        if (context.Actor.TenantId is null) return Result<QuerySchemeDetail>.Failure(Denied());
        if (request.Version < 1 || id == Guid.Empty) return Result<QuerySchemeDetail>.Failure(QuerySchemePolicy.InvalidError());
        return await transaction.ExecuteResultAsync(async cancellation =>
        {
            await LockAsync(context, cancellation).ConfigureAwait(false);
            var row = await FindAsync(context, id, cancellation).ConfigureAwait(false);
            if (row?.OwnerUserId != context.Actor.UserId) return Result<QuerySchemeDetail>.Failure(NotFound());
            if (row.Version != request.Version) return Result<QuerySchemeDetail>.Failure(Conflict());
            if (request.IsShared && !Detail(row, context.Schema).Summary.IsCompatible) return Result<QuerySchemeDetail>.Failure(QuerySchemePolicy.InvalidError());
            if (row.IsShared == request.IsShared) return Result<QuerySchemeDetail>.Success(Detail(row, context.Schema));
            var changed = await commands.ExecuteAsync(context.Sql.Share, context.Args(("Id", id), ("Version", request.Version),
                ("IsShared", request.IsShared), ("Now", clock.UtcNow.UtcDateTime)), cancellation).ConfigureAwait(false);
            return changed == 1 ? Result<QuerySchemeDetail>.Success(Detail((await FindAsync(context, id, cancellation).ConfigureAwait(false))!, context.Schema))
                : Result<QuerySchemeDetail>.Failure(Conflict());
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<bool>> DeleteAsync(string gridKey, Guid id, long version, CancellationToken token)
    {
        var resolved = await ResolveAsync(gridKey, QuerySchemePermissions.Delete, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<bool>.Failure(resolved.Error!);
        if (version < 1 || id == Guid.Empty) return Result<bool>.Failure(QuerySchemePolicy.InvalidError());
        var context = resolved.Value!;
        return await transaction.ExecuteResultAsync(async cancellation =>
        {
            await LockAsync(context, cancellation).ConfigureAwait(false);
            var row = await FindAsync(context, id, cancellation).ConfigureAwait(false);
            if (row?.OwnerUserId != context.Actor.UserId) return Result<bool>.Failure(NotFound());
            if (row.Version != version) return Result<bool>.Failure(Conflict());
            if (await commands.ExecuteAsync(context.Sql.Delete, context.Args(("Id", id), ("Version", version)), cancellation).ConfigureAwait(false) != 1)
                return Result<bool>.Failure(Conflict());
            // 仅更新本人的默认；其他用户的共享默认在读取时判为失效，不跨用户改偏好。
            await commands.ExecuteAsync(context.Sql.ClearOwnDefault, context.Args(("Id", id)), cancellation).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<DefaultQuerySchemeResponse>> GetDefaultAsync(string gridKey, CancellationToken token)
    {
        var resolved = await ResolveAsync(gridKey, QuerySchemePermissions.Read, token).ConfigureAwait(false);
        return resolved.IsSuccess ? Result<DefaultQuerySchemeResponse>.Success(await ReadDefaultAsync(resolved.Value!, token).ConfigureAwait(false))
            : Result<DefaultQuerySchemeResponse>.Failure(resolved.Error!);
    }

    internal async Task<Result<DefaultQuerySchemeResponse>> SetDefaultAsync(string gridKey, SetDefaultQuerySchemeRequest request, CancellationToken token)
    {
        var resolved = await ResolveAsync(gridKey, QuerySchemePermissions.SetDefault, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<DefaultQuerySchemeResponse>.Failure(resolved.Error!);
        if (request.Version < 0 || request.SchemeId == Guid.Empty) return Result<DefaultQuerySchemeResponse>.Failure(QuerySchemePolicy.InvalidError());
        var context = resolved.Value!;
        return await transaction.ExecuteResultAsync(async cancellation =>
        {
            var state = await LockAsync(context, cancellation).ConfigureAwait(false);
            if (state.Version != request.Version) return Result<DefaultQuerySchemeResponse>.Failure(Conflict());
            if (request.SchemeId is { } id)
            {
                var row = await FindAsync(context, id, cancellation).ConfigureAwait(false);
                if (row is null) return Result<DefaultQuerySchemeResponse>.Failure(NotFound());
                if (!Detail(row, context.Schema).Summary.IsCompatible) return Result<DefaultQuerySchemeResponse>.Failure(QuerySchemePolicy.InvalidError());
            }
            if (state.DefaultSchemeId != request.SchemeId
                && await commands.ExecuteAsync(context.Sql.UpdateDefault, context.Args(("SchemeId", request.SchemeId), ("Version", request.Version)), cancellation).ConfigureAwait(false) != 1)
                return Result<DefaultQuerySchemeResponse>.Failure(Conflict());
            return Result<DefaultQuerySchemeResponse>.Success(await ReadDefaultAsync(context, cancellation).ConfigureAwait(false));
        }, token).ConfigureAwait(false);
    }

    private async Task<DefaultQuerySchemeResponse> ReadDefaultAsync(Context context, CancellationToken token)
    {
        var state = await queries.QuerySingleOrDefaultAsync<QuerySchemeDefaultRecord>(context.Sql.FindDefault, context.Args(), token).ConfigureAwait(false);
        if (state?.DefaultSchemeId is not { } id) return new(null, false, state?.Version ?? 0);
        var row = await FindAsync(context, id, token).ConfigureAwait(false);
        var detail = row is null ? null : Detail(row, context.Schema);
        return new(detail?.Summary.IsCompatible == true ? detail : null, detail?.Summary.IsCompatible != true, state.Version);
    }

    private async Task<QuerySchemeDefaultRecord> LockAsync(Context context, CancellationToken token)
    {
        await commands.ExecuteAsync(context.Sql.EnsureDefault, context.Args(("DefaultId", ids.NewId()), ("Now", clock.UtcNow.UtcDateTime)), token).ConfigureAwait(false);
        return (await queries.QuerySingleOrDefaultAsync<QuerySchemeDefaultRecord>(context.Sql.LockDefault, context.Args(), token).ConfigureAwait(false))!;
    }

    private Task<QuerySchemeRecord?> FindAsync(Context context, Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<QuerySchemeRecord>(context.Sql.FindVisible, context.Args(("Id", id)), token);

    private async Task<Result<Context>> ResolveAsync(string gridKey, string permission, CancellationToken token)
    {
        if (!catalog.TryGet(gridKey, out var schema)) return Result<Context>.Failure(NotFound());
        // 跨模块权威读取在进入本地事务之前完成；方案不能提升业务权限或切换可信作用域。
        var actor = await authorization.AuthorizeAllAsync([permission, schema.ReadPermission], token).ConfigureAwait(false);
        if (actor is null || actor.UserId == Guid.Empty || actor.TenantId == Guid.Empty || (tenant.IsHost
                ? actor.TenantId is not null || !schema.SupportsHost
                : !tenant.IsAvailable || actor.TenantId != tenant.Id || !schema.SupportsTenant)) return Result<Context>.Failure(Denied());
        return Result<Context>.Success(new(schema, actor, actor.TenantId is { } tenantId ? $"tenant:{tenantId:N}" : "host", QuerySchemeSql.For(tenant.IsHost, database.Value.Provider)));
    }

    private static QuerySchemeSummary Summary(QuerySchemeRecord row, QuerySchemeDefinition schema) =>
        new(row.Id, row.Name, row.OwnerUserId, row.SchemaVersion, row.IsShared, row.SchemaVersion == schema.SchemaVersion, row.CreatedAtUtc, row.UpdatedAtUtc, row.Version);

    private static QuerySchemeDetail Detail(QuerySchemeRecord row, QuerySchemeDefinition schema)
    {
        var summary = Summary(row, schema);
        if (!summary.IsCompatible || string.IsNullOrWhiteSpace(row.ConfigurationJson) || Encoding.UTF8.GetByteCount(row.ConfigurationJson) > QuerySchemePolicy.MaximumConfigurationBytes)
            return new(summary with { IsCompatible = false }, null);
        try
        {
            var configuration = JsonSerializer.Deserialize(row.ConfigurationJson, SettingsJsonSerializerContext.Default.QuerySchemeConfiguration);
            var checkedValue = QuerySchemePolicy.Normalize(schema, row.SchemaVersion, configuration);
            return checkedValue.IsSuccess ? new(summary, checkedValue.Value) : new(summary with { IsCompatible = false }, null);
        }
        catch (JsonException) { return new(summary with { IsCompatible = false }, null); }
    }

    private static Error NotFound() => new(SettingsErrorCodes.QuerySchemeNotFound, "The query scheme or business schema was not found.", ErrorType.NotFound);
    private static Error Conflict() => new(SettingsErrorCodes.QuerySchemeVersionConflict, "The query scheme has changed. Reload it before trying again.", ErrorType.Conflict);
    private static Error Denied() => new(CommonErrorCodes.PermissionDenied, "The current session cannot access this business query schema.", ErrorType.Forbidden);
}
