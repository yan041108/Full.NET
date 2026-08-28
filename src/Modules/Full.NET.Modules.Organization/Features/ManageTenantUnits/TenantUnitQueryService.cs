using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantUnits;

/// <summary>租户机构分页列表与详情只读查询。</summary>
internal sealed class TenantUnitQueryService(
    IQueryExecutor queryExecutor,
    IUserDataScopeResolver dataScopeResolver,
    IDataScopeSqlFilterBuilder dataScopeFilterBuilder,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<OrganizationUnitResponse>>> ListAsync(
        Guid currentUserId,
        bool isSuperAdministrator,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var scope = await dataScopeResolver.ResolveAsync(
                currentUserId,
                isSuperAdministrator,
                cancellationToken)
            .ConfigureAwait(false);
        var filter = dataScopeFilterBuilder.BuildOrganizationUnitFilter(
            scope,
            "Id",
            currentUserId);
        var countStatement = TenantScopedSqlComposer.ApplyDataScopeFilter(
            OrganizationSql.CountUnits,
            filter);
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                TenantScopedSqlComposer.MergeParameters(null, filter),
                cancellationToken)
            .ConfigureAwait(false);
        var baseStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => OrganizationSql.ListUnitsSqlServer,
            DatabaseProvider.MySql => OrganizationSql.ListUnitsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var listStatement = TenantScopedSqlComposer.ApplyDataScopeFilter(baseStatement, filter);
        var queryParameters = TenantScopedSqlComposer.MergeParameters(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Offset"] = offset,
                ["PageSize"] = pageSize,
            },
            filter);
        var rows = await queryExecutor.QueryAsync<OrganizationUnitListRow>(
                listStatement,
                queryParameters,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<OrganizationUnitResponse>>.Success(
            new PagedResult<OrganizationUnitResponse>(items, page, pageSize, total));
    }

    public async Task<Result<OrganizationUnitResponse>> GetByIdForActorAsync(
        Guid unitId,
        Guid currentUserId,
        bool isSuperAdministrator,
        CancellationToken cancellationToken = default)
    {
        var scope = await dataScopeResolver.ResolveAsync(
                currentUserId,
                isSuperAdministrator,
                cancellationToken)
            .ConfigureAwait(false);
        var filter = dataScopeFilterBuilder.BuildOrganizationUnitFilter(
            scope,
            "Id",
            currentUserId);
        var statement = TenantScopedSqlComposer.ApplyDataScopeFilter(
            OrganizationSql.FindUnitById,
            filter);
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                statement,
                TenantScopedSqlComposer.MergeParameters(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["UnitId"] = unitId,
                    },
                    filter),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<OrganizationUnitResponse>.Success(Map(record));
    }

    internal async Task<Result<OrganizationUnitResponse>> FindByIdAsync(
        Guid unitId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitById,
                OrganizationSqlParameters.Create(("UnitId", unitId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<OrganizationUnitResponse>.Success(Map(record));
    }

    internal static OrganizationUnitResponse Map(OrganizationUnitListRow row) =>
        new(
            row.Id,
            row.ParentId,
            row.Code,
            row.Name,
            row.DisplayOrder,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    internal static OrganizationUnitResponse Map(OrganizationUnitRecord record) =>
        new(
            record.Id,
            record.ParentId,
            record.Code,
            record.Name,
            record.DisplayOrder,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<OrganizationUnitResponse> NotFound() =>
        Result<OrganizationUnitResponse>.Failure(new Error(
            OrganizationErrorCodes.UnitNotFound,
            "The organization unit was not found.",
            ErrorType.NotFound));
}
