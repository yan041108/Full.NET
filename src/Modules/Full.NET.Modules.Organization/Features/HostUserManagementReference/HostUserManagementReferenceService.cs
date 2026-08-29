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
    TenantPositionQueryService positionQueries,
    TenantUserPositionQueryService userPositionQueries)
{
    private const int MaxPageSize = 100;

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
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenantResult = await ResolveTenantAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenantResult.IsSuccess)
        {
            return Result<HostUserManagementOrganizationReferenceResponse>.Failure(tenantResult.Error!);
        }

        var tenant = tenantResult.Value!;

        return await HostUserManagementTenantScope.RunAsync(
            currentTenant,
            tenant.Id,
            tenant.Identifier,
            tenant.Name,
            async () =>
            {
                var units = await ListAllUnitsAsync(cancellationToken).ConfigureAwait(false);
                var positions = await ListAllPositionsAsync(cancellationToken).ConfigureAwait(false);
                if (!positions.IsSuccess)
                {
                    return Result<HostUserManagementOrganizationReferenceResponse>.Failure(positions.Error!);
                }

                var userUnits = await ListAllUserUnitsAsync(cancellationToken).ConfigureAwait(false);
                var userPositions = await ListAllUserPositionsAsync(cancellationToken).ConfigureAwait(false);
                if (!userPositions.IsSuccess)
                {
                    return Result<HostUserManagementOrganizationReferenceResponse>.Failure(userPositions.Error!);
                }

                return Result<HostUserManagementOrganizationReferenceResponse>.Success(
                    new HostUserManagementOrganizationReferenceResponse(
                        units,
                        positions.Value!,
                        userUnits,
                        userPositions.Value!));
            }).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<OrganizationUnitResponse>> ListAllUnitsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<OrganizationUnitListRow>(
                HostUserManagementReferenceSql.ListUnits,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(TenantUnitQueryService.Map).ToArray();
    }

    private async Task<Result<IReadOnlyList<OrganizationPositionResponse>>> ListAllPositionsAsync(
        CancellationToken cancellationToken)
    {
        var items = new List<OrganizationPositionResponse>();
        var page = 1;
        while (true)
        {
            var pageResult = await positionQueries.ListAsync(
                    page,
                    MaxPageSize,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!pageResult.IsSuccess)
            {
                return Result<IReadOnlyList<OrganizationPositionResponse>>.Failure(pageResult.Error!);
            }

            var pageValue = pageResult.Value!;
            items.AddRange(pageValue.Items);
            if (page * MaxPageSize >= pageValue.Total)
            {
                break;
            }

            page++;
        }

        return Result<IReadOnlyList<OrganizationPositionResponse>>.Success(items);
    }

    private async Task<IReadOnlyList<OrganizationUserUnitResponse>> ListAllUserUnitsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<OrganizationUserUnitListRow>(
                HostUserManagementReferenceSql.ListUserUnits,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return [];
        }

        var users = await hostUserDirectory.FindHostUsersAsync(
                rows.Select(row => row.UserId).Distinct().ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Where(row => users.ContainsKey(row.UserId))
            .Select(row => TenantUserUnitQueryService.Map(row, users[row.UserId]))
            .ToArray();
    }

    private async Task<Result<IReadOnlyList<OrganizationUserPositionResponse>>> ListAllUserPositionsAsync(
        CancellationToken cancellationToken)
    {
        var items = new List<OrganizationUserPositionResponse>();
        var page = 1;
        while (true)
        {
            var pageResult = await userPositionQueries.ListAsync(
                    page,
                    MaxPageSize,
                    null,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!pageResult.IsSuccess)
            {
                return Result<IReadOnlyList<OrganizationUserPositionResponse>>.Failure(pageResult.Error!);
            }

            var pageValue = pageResult.Value!;
            items.AddRange(pageValue.Items);
            if (page * MaxPageSize >= pageValue.Total)
            {
                break;
            }

            page++;
        }

        return Result<IReadOnlyList<OrganizationUserPositionResponse>>.Success(items);
    }

    internal sealed record HostUserManagementTenantRecord(
        Guid Id,
        string Identifier,
        string Name);
}
