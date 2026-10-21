using Microsoft.Extensions.Options;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;
using Full.NET.Modules.Organization.Features.ManageTenantUnits;
using Full.NET.Modules.Organization.Features.ManageTenantUserPositions;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Features.HostUserManagementReference;

/// <summary>Host 用户管理页按租户读取机构树与隶属参考数据。</summary>
internal sealed class HostUserManagementReferenceService(
    IQueryExecutor queryExecutor,
    ICurrentTenantContextWriter currentTenant,
    IActiveTenantContextResolver tenantResolver,
    IHostUserDisplayDirectory hostUserDirectory,
    IOptions<DatabaseOptions>? databaseOptions = null)
{

    public async Task<Result<HostUserManagementTenantRecord>> ResolveTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await tenantResolver.ResolveActiveByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return Result<HostUserManagementTenantRecord>.Failure(new Error(
                OrganizationErrorCodes.UnitNotFound,
                "未找到可用的租户上下文。",
                ErrorType.NotFound));
        }

        return Result<HostUserManagementTenantRecord>.Success(
            new HostUserManagementTenantRecord(
                tenant.Id,
                tenant.Identifier,
                tenant.Name));
    }

    public async Task<Result<HostUserManagementOrganizationReferenceResponse>> GetReferenceAsync(
        Guid tenantId, CancellationToken cancellationToken = default, IReadOnlyList<Guid>? userIds = null,
        int unitsPage = 1, int positionsPage = 1, string? unitSearch = null, string? positionSearch = null)
    {
        if (userIds is { Count: > 100 }) return BudgetExceeded();
        var selectedUsers = (userIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        var tenantResult = await ResolveTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (!tenantResult.IsSuccess)
            return Result<HostUserManagementOrganizationReferenceResponse>.Failure(tenantResult.Error!);
        var tenant = tenantResult.Value!;
        return await HostUserManagementTenantScope.RunAsync(currentTenant, tenant.Id, tenant.Identifier, tenant.Name,
            async () =>
            {
                var parameters = OrganizationSqlParameters.Create(("UserIds", selectedUsers),
                    ("UnitOffset", ((long)Math.Max(1, unitsPage) - 1) * 100),
                    ("PositionOffset", ((long)Math.Max(1, positionsPage) - 1) * 100),
                    ("UnitSearch", Search(unitSearch)), ("PositionSearch", Search(positionSearch)));
                var mysql = databaseOptions?.Value.Provider == DatabaseProvider.MySql;
                var units = await queryExecutor.QueryAsync<OrganizationUnitListRow>(mysql
                    ? HostUserManagementReferenceSql.ListUnitsPageMySql : HostUserManagementReferenceSql.ListUnitsPageSqlServer,
                    parameters, cancellationToken).ConfigureAwait(false);
                var positions = await queryExecutor.QueryAsync<OrganizationPositionListRow>(mysql
                    ? HostUserManagementReferenceSql.ListPositionsPageMySql : HostUserManagementReferenceSql.ListPositionsPageSqlServer,
                    parameters, cancellationToken).ConfigureAwait(false);
                var unitsTotal = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                    HostUserManagementReferenceSql.CountUnits, parameters, cancellationToken).ConfigureAwait(false);
                var positionsTotal = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                    HostUserManagementReferenceSql.CountPositions, parameters, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<OrganizationUserUnitListRow> userUnits = [];
                IReadOnlyList<OrganizationUserPositionListRow> userPositions = [];
                if (selectedUsers.Length > 0)
                {
                    userUnits = await queryExecutor.QueryAsync<OrganizationUserUnitListRow>(mysql
                        ? HostUserManagementReferenceSql.ListUserUnitsPageMySql : HostUserManagementReferenceSql.ListUserUnitsPageSqlServer,
                        parameters, cancellationToken).ConfigureAwait(false);
                    userPositions = await queryExecutor.QueryAsync<OrganizationUserPositionListRow>(mysql
                        ? HostUserManagementReferenceSql.ListUserPositionsPageMySql : HostUserManagementReferenceSql.ListUserPositionsPageSqlServer,
                        parameters, cancellationToken).ConfigureAwait(false);
                    if (userUnits.Count > 10000 || userPositions.Count > 10000) return BudgetExceeded();
                }
                var users = await hostUserDirectory.FindHostUsersAsync(selectedUsers, cancellationToken).ConfigureAwait(false);
                return Result<HostUserManagementOrganizationReferenceResponse>.Success(new(
                    units.Select(TenantUnitQueryService.Map).ToArray(), positions.Select(TenantPositionQueryService.Map).ToArray(),
                    userUnits.Where(row => users.ContainsKey(row.UserId)).Select(row => TenantUserUnitQueryService.Map(row, users[row.UserId])).ToArray(),
                    userPositions.Where(row => users.ContainsKey(row.UserId)).Select(row => TenantUserPositionQueryService.Map(row, users[row.UserId])).ToArray())
                { UnitsTotal = unitsTotal, PositionsTotal = positionsTotal });
            }).ConfigureAwait(false);
    }

    private static string? Search(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : $"%{value.Trim().Replace("!", "!!", StringComparison.Ordinal).Replace("%", "!%", StringComparison.Ordinal).Replace("_", "!_", StringComparison.Ordinal).Replace("[", "![", StringComparison.Ordinal)}%";

    private static Result<HostUserManagementOrganizationReferenceResponse> BudgetExceeded() =>
        Result<HostUserManagementOrganizationReferenceResponse>.Failure(new Error(ValidationErrorCodes.Failed,
            "Reference requests allow at most 100 users and 10000 assignments. Use the organization assignment pages for larger selections.", ErrorType.Validation));

    internal sealed record HostUserManagementTenantRecord(
        Guid Id,
        string Identifier,
        string Name);
}
