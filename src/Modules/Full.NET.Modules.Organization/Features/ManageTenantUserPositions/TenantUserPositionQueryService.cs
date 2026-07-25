using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserPositions;

/// <summary>租户用户-职位隶属只读查询（目录级，不按机构数据范围裁剪）。</summary>
internal sealed class TenantUserPositionQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<OrganizationUserPositionResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? userId,
        Guid? positionId,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                OrganizationSql.CountUserPositions,
                new { UserId = userId, PositionId = positionId },
                cancellationToken)
            .ConfigureAwait(false);
        var listStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => OrganizationSql.ListUserPositionsSqlServer,
            DatabaseProvider.MySql => OrganizationSql.ListUserPositionsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<OrganizationUserPositionListRow>(
                listStatement,
                new
                {
                    UserId = userId,
                    PositionId = positionId,
                    Offset = offset,
                    PageSize = pageSize,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<OrganizationUserPositionResponse>>.Success(
            new PagedResult<OrganizationUserPositionResponse>(items, page, pageSize, total));
    }

    public async Task<Result<OrganizationUserPositionResponse>> GetByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUserPositionListRow>(
                OrganizationSql.FindUserPositionById,
                new { AssignmentId = assignmentId },
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        return Result<OrganizationUserPositionResponse>.Success(Map(row));
    }

    internal static OrganizationUserPositionResponse Map(OrganizationUserPositionListRow row) =>
        new(
            row.Id,
            row.UserId,
            row.Username,
            row.DisplayName,
            row.PositionId,
            row.PositionCode,
            row.PositionName,
            row.IsPrimary,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static Result<OrganizationUserPositionResponse> NotFound() =>
        Result<OrganizationUserPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.UserPositionNotFound,
            "The organization user position assignment was not found.",
            ErrorType.NotFound));
}
