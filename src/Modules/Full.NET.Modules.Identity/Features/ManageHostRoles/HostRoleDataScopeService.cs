using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostRoles;

/// <summary>Host 角色数据范围读取与更新。</summary>
internal sealed class HostRoleDataScopeService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IIdentityOrganizationUnitDirectory organizationUnitDirectory,
    IClock clock)
{
    public async Task<Result<HostRoleDataScopeResponse>> GetAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                new { RoleId = roleId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var unitIds = await LoadUnitIdsAsync(roleId, cancellationToken).ConfigureAwait(false);
        return Result<HostRoleDataScopeResponse>.Success(Map(record, unitIds));
    }

    public Task<Result<HostRoleDataScopeResponse>> UpdateAsync(
        Guid roleId,
        UpdateHostRoleDataScopeRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(roleId, request, token),
            cancellationToken);

    private async Task<Result<HostRoleDataScopeResponse>> UpdateCoreAsync(
        Guid roleId,
        UpdateHostRoleDataScopeRequest request,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                new { RoleId = roleId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.IsSystem || record.IsSuperAdministrator)
        {
            return SystemLocked();
        }

        var kind = request.DataScopeKind?.Trim() ?? string.Empty;
        if (!RoleDataScopeKinds.AllKinds.Contains(kind, StringComparer.Ordinal))
        {
            return InvalidKind();
        }

        var unitIds = (request.UnitIds ?? [])
            .Distinct()
            .ToArray();
        if (kind == RoleDataScopeKinds.Custom)
        {
            if (request.TenantId is null)
            {
                return TenantContextRequired();
            }

            if (unitIds.Length == 0)
            {
                return CustomUnitsRequired();
            }

            foreach (var unitId in unitIds)
            {
                var unit = await organizationUnitDirectory.FindActiveUnitAsync(
                        request.TenantId.Value,
                        unitId,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (unit is null)
                {
                    return UnitNotFound();
                }
            }
        }
        else if (unitIds.Length > 0)
        {
            return InvalidKind();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateRoleDataScopeKind,
                new
                {
                    RoleId = roleId,
                    DataScopeKind = kind,
                    UpdatedAtUtc = now,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            return VersionConflict();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteRoleDataScopeUnits,
                new { RoleId = roleId },
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var unitId in unitIds)
        {
            await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertRoleDataScopeUnit,
                    new { RoleId = roleId, UnitId = unitId },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await GetAsync(roleId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<Guid>> LoadUnitIdsAsync(
        Guid roleId,
        CancellationToken cancellationToken) =>
        (await queryExecutor.QueryAsync<Guid>(
                IdentitySql.GetRoleDataScopeUnitIds,
                new { RoleId = roleId },
                cancellationToken)
            .ConfigureAwait(false)).ToArray();

    private static HostRoleDataScopeResponse Map(
        IdentityRoleRecord record,
        IReadOnlyList<Guid> unitIds) =>
        new(record.Id, record.DataScopeKind, unitIds, record.Version);

    private static Result<HostRoleDataScopeResponse> NotFound() =>
        Result<HostRoleDataScopeResponse>.Failure(new Error(
            IdentityErrorCodes.RoleNotFound,
            "The host role was not found.",
            ErrorType.NotFound));

    private static Result<HostRoleDataScopeResponse> SystemLocked() =>
        Result<HostRoleDataScopeResponse>.Failure(new Error(
            IdentityErrorCodes.RoleSystemLocked,
            "System roles cannot change data scope.",
            ErrorType.Conflict));

    private static Result<HostRoleDataScopeResponse> InvalidKind() =>
        Result<HostRoleDataScopeResponse>.Failure(new Error(
            IdentityErrorCodes.DataScopeInvalidKind,
            "The data scope kind is invalid.",
            ErrorType.Validation));

    private static Result<HostRoleDataScopeResponse> CustomUnitsRequired() =>
        Result<HostRoleDataScopeResponse>.Failure(new Error(
            IdentityErrorCodes.DataScopeCustomUnitsRequired,
            "Custom data scope requires at least one organization unit.",
            ErrorType.Validation));

    private static Result<HostRoleDataScopeResponse> TenantContextRequired() =>
        Result<HostRoleDataScopeResponse>.Failure(new Error(
            IdentityErrorCodes.DataScopeTenantContextRequired,
            "Custom data scope requires an explicit target tenant.",
            ErrorType.Validation));

    private static Result<HostRoleDataScopeResponse> UnitNotFound() =>
        Result<HostRoleDataScopeResponse>.Failure(new Error(
            IdentityErrorCodes.DataScopeUnitNotFound,
            "One or more organization units were not found.",
            ErrorType.NotFound));

    private static Result<HostRoleDataScopeResponse> VersionConflict() =>
        Result<HostRoleDataScopeResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The host role changed concurrently.",
            ErrorType.Conflict));
}
