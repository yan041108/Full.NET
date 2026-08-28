using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostRoles;

/// <summary>Host 角色分页列表与详情只读查询。</summary>
internal sealed class HostRoleQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostRoleResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountHostRoles,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IdentitySql.ListHostRolesSqlServer,
            DatabaseProvider.MySql => IdentitySql.ListHostRolesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = (await queryExecutor.QueryAsync<HostRoleListRow>(
                statement,
                IdentitySqlParameters.Create(("Offset", offset), ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false)).ToArray();
        var permissionCodesByRole = await LoadPermissionCodesByRoleIdsAsync(
                rows.Select(row => row.Id).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows
            .Select(row => Map(
                row,
                permissionCodesByRole.TryGetValue(row.Id, out var codes)
                    ? codes
                    : []))
            .ToArray();
        return Result<PagedResult<HostRoleResponse>>.Success(
            new PagedResult<HostRoleResponse>(items, page, pageSize, total));
    }

    public async Task<Result<HostRoleResponse>> GetByIdAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var permissionCodes = await LoadPermissionCodesAsync(roleId, cancellationToken)
            .ConfigureAwait(false);
        return Result<HostRoleResponse>.Success(Map(record, permissionCodes));
    }

    internal async Task<IReadOnlyList<string>> LoadPermissionCodesAsync(
        Guid roleId,
        CancellationToken cancellationToken) =>
        (await queryExecutor.QueryAsync<string>(
                IdentitySql.GetRolePermissionCodes,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false)).ToArray();

    internal async Task<IReadOnlyDictionary<Guid, string[]>> LoadPermissionCodesByRoleIdsAsync(
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return new Dictionary<Guid, string[]>();
        }

        var rows = await queryExecutor.QueryAsync<IdentityRolePermission>(
                IdentitySql.ListRolePermissionsByRoleIds,
                IdentitySqlParameters.Create(("RoleIds", roleIds)),
                cancellationToken)
            .ConfigureAwait(false);
        return rows
            .GroupBy(row => row.RoleId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(item => item.PermissionCode)
                    .OrderBy(code => code, StringComparer.Ordinal)
                    .ToArray());
    }

    internal static HostRoleResponse Map(
        HostRoleListRow row,
        IReadOnlyList<string> permissionCodes) =>
        new(
            row.Id,
            row.Code,
            row.Name,
            row.IsSystem,
            row.IsActive,
            row.IsSuperAdministrator,
            permissionCodes,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    internal static HostRoleResponse Map(
        IdentityRoleRecord record,
        IReadOnlyList<string> permissionCodes) =>
        new(
            record.Id,
            record.Code,
            record.Name,
            record.IsSystem,
            record.IsActive,
            record.IsSuperAdministrator,
            permissionCodes,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<HostRoleResponse> NotFound() =>
        Result<HostRoleResponse>.Failure(new Error(
            IdentityErrorCodes.RoleNotFound,
            "The host role was not found.",
            ErrorType.NotFound));
}
