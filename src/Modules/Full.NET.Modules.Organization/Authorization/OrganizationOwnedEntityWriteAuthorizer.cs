using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Authorization;

/// <summary>
/// 组织归属写入授权：要求目标机构活动且 actor 具备有效用户-机构隶属。
/// </summary>
internal sealed class OrganizationOwnedEntityWriteAuthorizer(
    ITenantOrganizationUnitDirectory unitDirectory,
    IQueryExecutor queryExecutor) : IOrganizationOwnedEntityWriteAuthorizer
{
    public async Task<Result<bool>> EnsureCanWriteAsync(
        Guid tenantId,
        Guid organizationUnitId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var unit = await unitDirectory
            .FindActiveUnitAsync(tenantId, organizationUnitId, cancellationToken)
            .ConfigureAwait(false);
        if (unit is null)
        {
            return Result<bool>.Failure(new Error(
                OrganizationErrorCodes.UnitNotFound,
                "The organization unit was not found.",
                ErrorType.NotFound));
        }

        var assignment = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(
                OrganizationSql.FindUserUnitByTenantUserAndUnit,
                OrganizationSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("UserId", actorUserId),
                    ("UnitId", organizationUnitId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (assignment is null || !assignment.IsActive)
        {
            return Result<bool>.Failure(new Error(
                OrganizationErrorCodes.WriteAccessDenied,
                "Write access to the organization unit was denied.",
                ErrorType.Forbidden));
        }

        return Result<bool>.Success(true);
    }
}