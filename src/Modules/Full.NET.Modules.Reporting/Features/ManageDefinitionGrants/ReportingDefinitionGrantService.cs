using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageDefinitionGrants;

/// <summary>Host 显式发布到租户；目标校验在本模块事务外，授权写入以定义行锁和版本串行化。</summary>
internal sealed class ReportingDefinitionGrantService(IQueryExecutor queries, ICommandExecutor commands,
    ICommandTransaction transaction, ReportingDefinitionQueryService definitions, IActiveTenantContextResolver tenants,
    ICurrentTenant current, IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database)
{
    internal async Task<Result<PagedResult<ReportingDefinitionTenantGrantResponse>>> ListAsync(Guid definitionId,
        int page, int pageSize, CancellationToken token)
    {
        if (!current.IsAvailable || !current.IsHost) return Result<PagedResult<ReportingDefinitionTenantGrantResponse>>.Failure(ScopeDenied());
        var definition = await definitions.GetByIdAsync(definitionId, token).ConfigureAwait(false);
        if (!definition.IsSuccess) return Result<PagedResult<ReportingDefinitionTenantGrantResponse>>.Failure(definition.Error!);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = ReportingSqlParameters.Create(("DefinitionId", definitionId), ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        var total = await queries.QuerySingleOrDefaultAsync<long>(ReportingDefinitionGrantSql.Count, parameters, token).ConfigureAwait(false);
        var rows = await queries.QueryAsync<ReportingDefinitionTenantGrantRecord>(database.Value.Provider == DatabaseProvider.MySql
            ? ReportingDefinitionGrantSql.ListMySql : ReportingDefinitionGrantSql.ListSqlServer, parameters, token).ConfigureAwait(false);
        return Result<PagedResult<ReportingDefinitionTenantGrantResponse>>.Success(new(rows.Select(Map).ToArray(), page, pageSize, total));
    }

    internal async Task<Result<ReportingDefinitionTenantGrantResponse>> SetAsync(Guid definitionId, Guid tenantId,
        SetReportingDefinitionTenantGrantRequest request, Guid actor, CancellationToken token)
    {
        if (!current.IsAvailable || !current.IsHost) return Result<ReportingDefinitionTenantGrantResponse>.Failure(ScopeDenied());
        if (definitionId == Guid.Empty || tenantId == Guid.Empty || actor == Guid.Empty || request.ExpectedVersion < 0)
            return Result<ReportingDefinitionTenantGrantResponse>.Failure(new(ReportingErrorCodes.GrantInvalid, "The grant request is invalid.", ErrorType.Validation));
        // 撤销既有授权必须允许处理已停用租户；启用则重新验证当前活动租户。
        if (request.IsEnabled && await tenants.ResolveActiveByIdAsync(tenantId, token).ConfigureAwait(false) is null)
            return Result<ReportingDefinitionTenantGrantResponse>.Failure(new(ReportingErrorCodes.GrantTenantNotFound, "The active tenant was not found.", ErrorType.NotFound));
        return await transaction.ExecuteResultAsync(async local =>
        {
            var parameters = ReportingSqlParameters.Create(("Id", ids.NewId()), ("DefinitionId", definitionId), ("TargetTenantId", tenantId),
                ("ActorUserId", actor), ("IsEnabled", request.IsEnabled), ("ExpectedVersion", request.ExpectedVersion), ("Now", clock.UtcNow));
            var locked = await queries.QuerySingleOrDefaultAsync<Guid>(database.Value.Provider == DatabaseProvider.MySql
                ? ReportingDefinitionGrantSql.LockDefinitionMySql : ReportingDefinitionGrantSql.LockDefinitionSqlServer, parameters, local).ConfigureAwait(false);
            if (locked == Guid.Empty) return Result<ReportingDefinitionTenantGrantResponse>.Failure(NotFound());
            if (request.IsEnabled)
            {
                var published = await definitions.GetByIdAsync(definitionId, local).ConfigureAwait(false);
                if (!published.IsSuccess || published.Value is not { IsEnabled: true, LatestPublishedVersionNumber: > 0 })
                    return Result<ReportingDefinitionTenantGrantResponse>.Failure(new(ReportingErrorCodes.DefinitionNotPublished,
                        "An enabled published definition is required before granting access.", ErrorType.Validation));
                var version = await definitions.GetVersionAsync(definitionId, published.Value.LatestPublishedVersionNumber, local).ConfigureAwait(false);
                if (!version.IsSuccess) return Result<ReportingDefinitionTenantGrantResponse>.Failure(version.Error!);
                var source = await queries.QuerySingleOrDefaultAsync<ReportingDataSourceRecord>(ReportingDataSourceSql.FindById,
                    ReportingSqlParameters.Create(("DataSourceId", version.Value!.DataSourceId)), local).ConfigureAwait(false);
                if (source is not { IsEnabled: true } || source.TenantId is { } owner && owner != tenantId)
                    return Result<ReportingDefinitionTenantGrantResponse>.Failure(new(ReportingErrorCodes.GrantSourceUnavailable,
                        "The published data source is unavailable to the target tenant.", ErrorType.Validation));
            }
            var prior = await queries.QuerySingleOrDefaultAsync<ReportingDefinitionTenantGrantRecord>(ReportingDefinitionGrantSql.Find, parameters, local).ConfigureAwait(false);
            if (prior is null)
            {
                if (request.ExpectedVersion != 0) return Result<ReportingDefinitionTenantGrantResponse>.Failure(Conflict());
                if (!request.IsEnabled) return Result<ReportingDefinitionTenantGrantResponse>.Failure(NotFound());
                if (await commands.ExecuteAsync(ReportingDefinitionGrantSql.Insert, parameters, local).ConfigureAwait(false) != 1)
                    return Result<ReportingDefinitionTenantGrantResponse>.Failure(Conflict());
            }
            else
            {
                if (prior.Version != request.ExpectedVersion) return Result<ReportingDefinitionTenantGrantResponse>.Failure(Conflict());
                if (prior.IsEnabled == request.IsEnabled) return Result<ReportingDefinitionTenantGrantResponse>.Success(Map(prior));
                if (prior.Version == int.MaxValue || await commands.ExecuteAsync(ReportingDefinitionGrantSql.Update, parameters, local).ConfigureAwait(false) != 1)
                    return Result<ReportingDefinitionTenantGrantResponse>.Failure(Conflict());
            }
            var saved = await queries.QuerySingleOrDefaultAsync<ReportingDefinitionTenantGrantRecord>(ReportingDefinitionGrantSql.Find, parameters, local).ConfigureAwait(false);
            return Result<ReportingDefinitionTenantGrantResponse>.Success(Map(saved!));
        }, token).ConfigureAwait(false);
    }

    private static ReportingDefinitionTenantGrantResponse Map(ReportingDefinitionTenantGrantRecord value) =>
        new(value.Id, value.DefinitionId, value.TenantId, value.IsEnabled, value.Version, value.CreatedAtUtc, value.UpdatedAtUtc);
    private static Error Conflict() => new(ReportingErrorCodes.GrantVersionConflict, "The grant has changed. Reload before updating.", ErrorType.Conflict);
    private static Error NotFound() => new(ReportingErrorCodes.GrantNotFound, "The definition or grant was not found.", ErrorType.NotFound);
    private static Error ScopeDenied() => new(ReportingErrorCodes.GrantHostRequired, "Host context is required to manage grants.", ErrorType.Forbidden);
}
