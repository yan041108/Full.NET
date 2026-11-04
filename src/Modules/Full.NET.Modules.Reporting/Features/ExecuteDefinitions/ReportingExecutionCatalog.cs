using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ExecuteDefinitions;

/// <summary>执行侧读取授权发布版本；保留 Host 管理边界，租户路径始终通过本模块授权台账 JOIN。</summary>
internal sealed class ReportingExecutionCatalog(IQueryExecutor queries, ReportingDefinitionQueryService hostDefinitions,
    ICurrentTenant current, IOptions<DatabaseOptions> database)
{
    internal async Task<Result<ReportingDefinitionResponse>> GetByIdAsync(Guid id, CancellationToken token = default)
    {
        if (current.IsAvailable && current.IsHost) return await hostDefinitions.GetByIdAsync(id, token).ConfigureAwait(false);
        if (!TenantAvailable()) return Result<ReportingDefinitionResponse>.Failure(ScopeDenied());
        var row = await queries.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(ReportingDefinitionGrantSql.PublishedDefinition,
            ReportingSqlParameters.Create(("DefinitionId", id), ("NameContains", null)), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return row is null ? Result<ReportingDefinitionResponse>.Failure(NotFound())
            : Result<ReportingDefinitionResponse>.Success(ReportingDefinitionMapper.MapDefinition(row));
    }

    internal async Task<Result<ReportingDefinitionVersionResponse>> GetVersionAsync(Guid id, int version, CancellationToken token = default)
    {
        if (current.IsAvailable && current.IsHost) return await hostDefinitions.GetVersionAsync(id, version, token).ConfigureAwait(false);
        if (!TenantAvailable()) return Result<ReportingDefinitionVersionResponse>.Failure(ScopeDenied());
        var row = await queries.QuerySingleOrDefaultAsync<ReportingDefinitionVersionRecord>(ReportingDefinitionGrantSql.PublishedVersion,
            ReportingSqlParameters.Create(("DefinitionId", id), ("VersionNumber", version)), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return row is null ? Result<ReportingDefinitionVersionResponse>.Failure(NotFound())
            : Result<ReportingDefinitionVersionResponse>.Success(ReportingDefinitionMapper.MapVersion(row));
    }

    internal Task<ReportingDataSourceRecord?> GetDataSourceAsync(Guid definition, int version, Guid source, CancellationToken token)
    {
        // Host 使用原 HostOnly 查询；租户数据源只能由同一固定版本和有效租户授权解析。
        var statement = current.IsAvailable && current.IsHost ? ReportingDataSourceSql.FindById : ReportingDefinitionGrantSql.PublishedDataSource;
        return queries.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(statement,
            ReportingSqlParameters.Create(("DefinitionId", definition), ("VersionNumber", version), ("DataSourceId", source)), token);
    }

    internal async Task<Result<PagedResult<ReportingPublishedDefinitionResponse>>> ListAsync(string? name, int page, int pageSize, CancellationToken token)
    {
        if (!TenantAvailable()) return Result<PagedResult<ReportingPublishedDefinitionResponse>>.Failure(ScopeDenied());
        if (name?.Length > 128) return Result<PagedResult<ReportingPublishedDefinitionResponse>>.Failure(new(ReportingErrorCodes.PublishedInvalidFilter, "The filter is too long.", ErrorType.Validation));
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var normalized = string.IsNullOrWhiteSpace(name) ? null : "%" + name.Trim().Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
        var parameters = ReportingSqlParameters.Create(("NameContains", normalized), ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        var total = await queries.QuerySingleOrDefaultAsync<long>(ReportingDefinitionGrantSql.PublishedCount, parameters, token).ConfigureAwait(false);
        var rows = await queries.QueryAsync<ReportingDefinitionRecord>(database.Value.Provider == DatabaseProvider.MySql
            ? ReportingDefinitionGrantSql.PublishedListMySql : ReportingDefinitionGrantSql.PublishedListSqlServer, parameters, token).ConfigureAwait(false);
        return Result<PagedResult<ReportingPublishedDefinitionResponse>>.Success(new(rows.Select(row => Published(ReportingDefinitionMapper.MapDefinition(row))).ToArray(), page, pageSize, total));
    }

    internal static ReportingPublishedDefinitionResponse Published(ReportingDefinitionResponse value) =>
        new(value.Id, value.DefinitionKey, value.Name, value.LatestPublishedVersionNumber, value.ParameterSchema);
    private bool TenantAvailable() => current.IsAvailable && !current.IsHost && current.Id is not null;
    private static Error ScopeDenied() => new(ReportingErrorCodes.ExportTenantRequired, "Tenant context is required.", ErrorType.Forbidden);
    private static Error NotFound() => new(ReportingErrorCodes.PublishedNotFound, "The authorized published definition was not found.", ErrorType.NotFound);
}
