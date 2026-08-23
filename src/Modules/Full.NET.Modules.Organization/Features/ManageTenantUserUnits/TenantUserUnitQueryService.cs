using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits;

/// <summary>租户用户-机构隶属只读查询。</summary>
internal sealed class TenantUserUnitQueryService(
    IQueryExecutor queryExecutor,
    IHostUserDisplayDirectory hostUserDirectory,
    IUserDataScopeResolver dataScopeResolver,
    IDataScopeSqlFilterBuilder dataScopeFilterBuilder,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<OrganizationUserUnitResponse>>> ListAsync(
        Guid currentUserId,
        bool isSuperAdministrator,
        int page,
        int pageSize,
        Guid? userId,
        Guid? unitId,
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
            "unitObject.Id",
            currentUserId);
        var countStatement = TenantScopedSqlComposer.ApplyDataScopeFilter(
            OrganizationSql.CountUserUnits,
            filter,
            TenantScopedSqlComposer.AssignmentTenantWhereAnchor);
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                TenantScopedSqlComposer.MergeParameters(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["UserId"] = userId,
                        ["UnitId"] = unitId,
                    },
                    filter),
                cancellationToken)
            .ConfigureAwait(false);
        var baseStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => OrganizationSql.ListUserUnitsSqlServer,
            DatabaseProvider.MySql => OrganizationSql.ListUserUnitsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var listStatement = TenantScopedSqlComposer.ApplyDataScopeFilter(
            baseStatement,
            filter,
            TenantScopedSqlComposer.AssignmentTenantWhereAnchor);
        var rows = await queryExecutor.QueryAsync<OrganizationUserUnitListRow>(
                listStatement,
                TenantScopedSqlComposer.MergeParameters(
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["UserId"] = userId,
                        ["UnitId"] = unitId,
                        ["Offset"] = offset,
                        ["PageSize"] = pageSize,
                    },
                    filter),
                cancellationToken)
            .ConfigureAwait(false);
        var users = await hostUserDirectory.FindHostUsersAsync(
                rows.Select(row => row.UserId).Distinct().ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows
            .Where(row => users.ContainsKey(row.UserId))
            .Select(row => Map(row, users[row.UserId]))
            .ToArray();
        return Result<PagedResult<OrganizationUserUnitResponse>>.Success(
            new PagedResult<OrganizationUserUnitResponse>(items, page, pageSize, total));
    }

    public async Task<Result<OrganizationUserUnitResponse>> GetByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUserUnitListRow>(
                OrganizationSql.FindUserUnitById,
                new { AssignmentId = assignmentId },
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        var users = await hostUserDirectory.FindHostUsersAsync(
                [row.UserId],
                cancellationToken)
            .ConfigureAwait(false);
        return users.TryGetValue(row.UserId, out var user)
            ? Result<OrganizationUserUnitResponse>.Success(Map(row, user))
            : NotFound();
    }

    internal static OrganizationUserUnitResponse Map(
        OrganizationUserUnitListRow row,
        HostUserDirectoryEntry user) =>
        new(
            row.Id,
            row.UserId,
            user.Username,
            user.DisplayName,
            row.UnitId,
            row.UnitCode,
            row.UnitName,
            row.IsPrimary,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static Result<OrganizationUserUnitResponse> NotFound() =>
        Result<OrganizationUserUnitResponse>.Failure(new Error(
            OrganizationErrorCodes.UserUnitNotFound,
            "The organization user assignment was not found.",
            ErrorType.NotFound));
}
