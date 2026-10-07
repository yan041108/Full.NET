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
    ICommandExecutor commands, IIdGenerator ids, IClock clock, IOptions<DatabaseOptions> database)
{
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
