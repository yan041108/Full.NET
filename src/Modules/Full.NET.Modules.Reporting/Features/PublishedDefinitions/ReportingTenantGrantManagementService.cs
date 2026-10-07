using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.PublishedDefinitions;

/// <summary>Host 对活动租户授予精确发布版本；撤销无需租户仍处于活动状态。</summary>
internal sealed class ReportingTenantGrantManagementService(
    ICurrentTenant tenant, IActiveTenantContextResolver tenants, ReportingDefinitionQueryService definitions,
    ICommandExecutor commands, IIdGenerator ids, IClock clock, IOptions<DatabaseOptions> database,
    IQueryExecutor queries)
{
    /// <summary>分页保留所有存量授权；停用租户仍可被 Host 查看并显式撤销。</summary>
    public async Task<Result<PagedResult<Guid>>> ListAsync(Guid definitionId, int versionNumber,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!tenant.IsHost || definitionId == Guid.Empty || versionNumber <= 0)
            return Result<PagedResult<Guid>>.Failure(new(CommonErrorCodes.PermissionDenied,
                "Host version grant permission is required.", ErrorType.Forbidden));
        if (page < 1 || pageSize is < 1 or > 200)
            return Result<PagedResult<Guid>>.Failure(new(Contracts.ReportingErrorCodes.DefinitionGrantPaginationInvalid,
                "Page must be positive and page size must be between 1 and 200.", ErrorType.Validation));
        var version = await definitions.GetVersionAsync(definitionId, versionNumber, cancellationToken).ConfigureAwait(false);
        if (!version.IsSuccess) return Result<PagedResult<Guid>>.Failure(version.Error!);
        var parameters = ReportingSqlParameters.Create(("DefinitionId", definitionId), ("VersionNumber", versionNumber),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        var total = await queries.QuerySingleOrDefaultAsync<long>(ReportingTenantGrantSql.CountGrants, parameters, cancellationToken).ConfigureAwait(false);
        var rows = await queries.QueryAsync<Guid>(ReportingTenantGrantSql.ListGrants(database.Value.Provider), parameters, cancellationToken).ConfigureAwait(false);
        return Result<PagedResult<Guid>>.Success(new(rows, page, pageSize, total));
    }

    public async Task<Result<bool>> SetAsync(Guid definitionId, int versionNumber, Guid tenantId, Guid actorId,
        bool grant, CancellationToken cancellationToken)
    {
        if (!tenant.IsHost || tenantId == Guid.Empty || actorId == Guid.Empty || versionNumber <= 0)
            return Result<bool>.Failure(new(CommonErrorCodes.PermissionDenied, "Host version grant permission is required.", ErrorType.Forbidden));
        if (grant)
        {
            if (await tenants.ResolveActiveByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
                return Result<bool>.Failure(new(CommonErrorCodes.PermissionDenied, "The target tenant is unavailable.", ErrorType.Forbidden));
            var definition = await definitions.GetByIdAsync(definitionId, cancellationToken).ConfigureAwait(false);
            var version = await definitions.GetVersionAsync(definitionId, versionNumber, cancellationToken).ConfigureAwait(false);
            if (!definition.IsSuccess || definition.Value?.IsEnabled != true || !version.IsSuccess)
                return Result<bool>.Failure(new(CommonErrorCodes.PermissionDenied, "The published version is unavailable.", ErrorType.Forbidden));
        }
        await commands.ExecuteAsync(grant ? ReportingTenantGrantSql.Grant(database.Value.Provider) : ReportingTenantGrantSql.Revoke,
            ReportingSqlParameters.Create(("Id", ids.NewId()), ("TargetTenantId", tenantId), ("DefinitionId", definitionId),
                ("VersionNumber", versionNumber), ("GrantedByUserId", actorId), ("CreatedAtUtc", clock.UtcNow)), cancellationToken).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }
}
