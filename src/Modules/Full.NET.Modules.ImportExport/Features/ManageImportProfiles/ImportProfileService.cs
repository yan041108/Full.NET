using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.ImportExport.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.ImportExport.Features.ManageImportProfiles;

/// <summary>来源权威授权先于本地事务；个人作用域锁只保护本人配置，不参与业务写入。</summary>
internal sealed class ImportProfileService(StaticImportSchemaRegistry registry, ICurrentSessionAuthorization authorization,
    ICurrentTenant tenant, IQueryExecutor queries, ICommandExecutor commands, ICommandTransaction transaction,
    IOptions<DatabaseOptions> database, IClock clock, IIdGenerator ids)
{
    private sealed record Context(string SchemaKey, string WorksheetKey, IReadOnlyList<string> Headers, string Fingerprint,
        AuthorizedSessionActor Actor, ImportProfileStatements Sql)
    {
        internal Dictionary<string, object?> Args(params (string Key, object? Value)[] values)
        {
            var result = ImportExportSqlParameters.Create(("UserId", Actor.UserId), ("SchemaKey", SchemaKey), ("WorksheetKey", WorksheetKey));
            foreach (var (key, value) in values) result.Add(key, value);
            return result;
        }
    }

    internal async Task<Result<IReadOnlyList<ImportProfileSummary>>> ListAsync(string schemaKey, string worksheetKey, CancellationToken token)
    {
        var resolved = await ResolveAsync(schemaKey, worksheetKey, ImportProfilePermissions.Read, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<IReadOnlyList<ImportProfileSummary>>.Failure(resolved.Error!);
        var context = resolved.Value!;
        var rows = await queries.QueryAsync<ImportProfileRecord>(context.Sql.List, context.Args(), token).ConfigureAwait(false);
        return Result<IReadOnlyList<ImportProfileSummary>>.Success(rows.Select(row => Summary(row, context)).ToArray());
    }

    internal async Task<Result<ImportProfileDetail>> GetAsync(string schemaKey, string worksheetKey, Guid id, CancellationToken token)
    {
        if (id == Guid.Empty) return Result<ImportProfileDetail>.Failure(Invalid());
        var resolved = await ResolveAsync(schemaKey, worksheetKey, ImportProfilePermissions.Read, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<ImportProfileDetail>.Failure(resolved.Error!);
        var row = await FindAsync(resolved.Value!, id, token).ConfigureAwait(false);
        return row is null ? Result<ImportProfileDetail>.Failure(NotFound()) : Result<ImportProfileDetail>.Success(Detail(row, resolved.Value!));
    }

    internal async Task<Result<ImportProfileDetail>> SaveAsync(string schemaKey, string worksheetKey, Guid? id, SaveImportProfileRequest request, CancellationToken token)
    {
        var resolved = await ResolveAsync(schemaKey, worksheetKey, id is null ? ImportProfilePermissions.Create : ImportProfilePermissions.Update, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<ImportProfileDetail>.Failure(resolved.Error!);
        var context = resolved.Value!; var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 64 || name.Any(char.IsControl) || id == Guid.Empty
            || (id is null ? request.Version != 0 : request.Version < 1) || request.Configuration?.HeaderColumns is null)
            return Result<ImportProfileDetail>.Failure(Invalid());
        if (!request.Configuration.HeaderColumns.SequenceEqual(context.Headers, StringComparer.Ordinal))
            return Result<ImportProfileDetail>.Failure(new Error(ImportColumnMappingErrorCodes.SchemaChanged, "The target headers have changed. Reload the source catalog.", ErrorType.Conflict));
        if (!StaticImportHeaderMapping.TryNormalize(context.Headers, request.Configuration, out var normalized))
            return Result<ImportProfileDetail>.Failure(Invalid());
        var json = JsonSerializer.Serialize(normalized, ImportExportJsonSerializerContext.Default.ImportColumnMappingRequest);
        return await transaction.ExecuteResultAsync(async cancellation =>
        {
            await LockAsync(context, cancellation).ConfigureAwait(false);
            var profileId = id ?? ids.NewId();
            if (id is null)
            {
                var count = await queries.QuerySingleOrDefaultAsync<long>(context.Sql.Count, context.Args(), cancellation).ConfigureAwait(false);
                if (count >= 32) return Result<ImportProfileDetail>.Failure(new Error(ImportProfileErrorCodes.LimitReached, "The source already has 32 personal import profiles.", ErrorType.Validation));
                await commands.ExecuteAsync(context.Sql.Insert, context.Args(("Id", profileId), ("Name", name), ("Fingerprint", context.Fingerprint),
                    ("ConfigurationJson", json), ("Now", clock.UtcNow)), cancellation).ConfigureAwait(false);
            }
            else
            {
                var existing = await FindAsync(context, profileId, cancellation).ConfigureAwait(false);
                if (existing is null) return Result<ImportProfileDetail>.Failure(NotFound());
                if (existing.Version != request.Version) return Result<ImportProfileDetail>.Failure(Conflict());
                var updated = await commands.ExecuteAsync(context.Sql.Update, context.Args(("Id", profileId), ("Version", request.Version), ("Name", name),
                    ("Fingerprint", context.Fingerprint), ("ConfigurationJson", json), ("Now", clock.UtcNow)), cancellation).ConfigureAwait(false);
                if (updated != 1) return Result<ImportProfileDetail>.Failure(Conflict());
            }
            var row = await FindAsync(context, profileId, cancellation).ConfigureAwait(false)
                ?? throw new InvalidOperationException("个人导入配置写入后未能读取本人记录。");
            return Result<ImportProfileDetail>.Success(Detail(row, context));
        }, token).ConfigureAwait(false);
    }

    internal async Task<Result<bool>> DeleteAsync(string schemaKey, string worksheetKey, Guid id, long version, CancellationToken token)
    {
        if (id == Guid.Empty || version < 1) return Result<bool>.Failure(Invalid());
        var resolved = await ResolveAsync(schemaKey, worksheetKey, ImportProfilePermissions.Delete, token).ConfigureAwait(false);
        if (!resolved.IsSuccess) return Result<bool>.Failure(resolved.Error!);
        var context = resolved.Value!;
        return await transaction.ExecuteResultAsync(async cancellation =>
        {
            await LockAsync(context, cancellation).ConfigureAwait(false);
            var row = await FindAsync(context, id, cancellation).ConfigureAwait(false);
            if (row is null) return Result<bool>.Failure(NotFound());
            if (row.Version != version) return Result<bool>.Failure(Conflict());
            var deleted = await commands.ExecuteAsync(context.Sql.Delete, context.Args(("Id", id), ("Version", version)), cancellation).ConfigureAwait(false);
            return deleted == 1 ? Result<bool>.Success(true) : Result<bool>.Failure(Conflict());
        }, token).ConfigureAwait(false);
    }

    private async Task<Result<Context>> ResolveAsync(string schemaKey, string worksheetKey, string operation, CancellationToken token)
    {
        var definition = registry.TryResolve(schemaKey)?.GetDefinition();
        var worksheet = definition?.Worksheets.SingleOrDefault(item => item.WorksheetKey == worksheetKey);
        if (definition is null || definition.SchemaKey != schemaKey || definition.ScopeKey != StaticImportSchemaScopeKeys.Tenant || worksheet is null)
            return Result<Context>.Failure(new Error(ImportExportErrorCodes.SchemaNotFound, "The static import source was not found.", ErrorType.NotFound));
        var actor = await authorization.AuthorizeAllAsync([operation, ImportExportPermissions.StaticSchemasRead, definition.RequiredPermission], token).ConfigureAwait(false);
        if (actor is null || actor.UserId == Guid.Empty || actor.TenantId is null || actor.TenantId == Guid.Empty || !tenant.IsAvailable || tenant.IsHost || tenant.Id != actor.TenantId)
            return Result<Context>.Failure(new Error(CommonErrorCodes.PermissionDenied, "The current session cannot access this import source.", ErrorType.Forbidden));
        var headers = Array.AsReadOnly(worksheet.HeaderColumns.ToArray());
        return Result<Context>.Success(new(schemaKey, worksheetKey, headers, StaticImportHeaderMapping.Fingerprint(headers), actor, ImportProfileSql.For(database.Value.Provider)));
    }

    private async Task LockAsync(Context context, CancellationToken token)
    {
        await commands.ExecuteAsync(context.Sql.EnsureScope, context.Args(("ScopeId", ids.NewId()), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
        var locked = await queries.QuerySingleOrDefaultAsync<Guid>(context.Sql.LockScope, context.Args(), token).ConfigureAwait(false);
        if (locked == Guid.Empty) throw new InvalidOperationException("个人导入配置作用域锁未建立。");
    }

    private Task<ImportProfileRecord?> FindAsync(Context context, Guid id, CancellationToken token) =>
        queries.QuerySingleOrDefaultAsync<ImportProfileRecord>(context.Sql.Find, context.Args(("Id", id)), token);
    private static ImportProfileSummary Summary(ImportProfileRecord row, Context context) => new(row.Id, row.Name, row.HeaderFingerprint,
        string.Equals(row.HeaderFingerprint, context.Fingerprint, StringComparison.Ordinal), row.CreatedAtUtc, row.UpdatedAtUtc, row.Version);
    private static ImportProfileDetail Detail(ImportProfileRecord row, Context context)
    {
        var summary = Summary(row, context);
        if (!summary.IsCompatible || row.ConfigurationJson is null || Encoding.UTF8.GetByteCount(row.ConfigurationJson) > StaticImportHeaderMapping.MaximumConfigurationBytes)
            return new(summary with { IsCompatible = false }, null);
        try
        {
            var value = JsonSerializer.Deserialize(row.ConfigurationJson, ImportExportJsonSerializerContext.Default.ImportColumnMappingRequest);
            return StaticImportHeaderMapping.TryNormalize(context.Headers, value, out var normalized)
                ? new(summary, normalized) : new(summary with { IsCompatible = false }, null);
        }
        catch (JsonException) { return new(summary with { IsCompatible = false }, null); }
    }

    internal static Error Invalid() => new(ImportProfileErrorCodes.Invalid, "The import profile is invalid.", ErrorType.Validation);
    private static Error NotFound() => new(ImportProfileErrorCodes.NotFound, "The personal import profile was not found.", ErrorType.NotFound);
    private static Error Conflict() => new(ImportProfileErrorCodes.VersionConflict, "The import profile has changed. Reload it before writing.", ErrorType.Conflict);
}
