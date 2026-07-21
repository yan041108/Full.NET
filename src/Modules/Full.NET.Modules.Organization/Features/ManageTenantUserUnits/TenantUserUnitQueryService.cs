using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits;

/// <summary>租户用户-机构隶属只读查询。</summary>
internal sealed class TenantUserUnitQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<OrganizationUserUnitResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? userId,
        Guid? unitId,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                OrganizationSql.CountUserUnits,
                new { UserId = userId, UnitId = unitId },
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => OrganizationSql.ListUserUnitsSqlServer,
            DatabaseProvider.MySql => OrganizationSql.ListUserUnitsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<OrganizationUserUnitListRow>(
                statement,
                new
                {
                    UserId = userId,
                    UnitId = unitId,
                    Offset = offset,
                    PageSize = pageSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
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

        return Result<OrganizationUserUnitResponse>.Success(Map(row));
    }

    internal static OrganizationUserUnitResponse Map(OrganizationUserUnitListRow row) =>
        new(
            row.Id,
            row.UserId,
            row.Username,
            row.DisplayName,
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
