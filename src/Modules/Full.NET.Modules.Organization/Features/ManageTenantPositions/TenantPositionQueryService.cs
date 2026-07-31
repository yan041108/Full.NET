using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantPositions;

/// <summary>租户职位分页列表与详情只读查询（目录级，不按机构数据范围裁剪）。</summary>
internal sealed class TenantPositionQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<OrganizationPositionResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                PositionSql.Count,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        var listStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => PositionSql.ListSqlServer,
            DatabaseProvider.MySql => PositionSql.ListMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<OrganizationPositionListRow>(
                listStatement,
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<OrganizationPositionResponse>>.Success(
            new PagedResult<OrganizationPositionResponse>(items, page, pageSize, total));
    }

    public async Task<Result<OrganizationPositionResponse>> GetByIdAsync(
        Guid positionId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(
                PositionSql.FindById,
                new { PositionId = positionId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<OrganizationPositionResponse>.Success(Map(record));
    }

    internal async Task<Result<OrganizationPositionResponse>> FindByIdAsync(
        Guid positionId,
        CancellationToken cancellationToken = default) =>
        await GetByIdAsync(positionId, cancellationToken).ConfigureAwait(false);

    internal static OrganizationPositionResponse Map(OrganizationPositionListRow row) =>
        new(
            row.Id,
            row.Code,
            row.Name,
            row.UnitId,
            row.UnitCode,
            row.UnitName,
            row.PositionLevelId,
            row.PositionLevelCode,
            row.PositionLevelName,
            row.DisplayOrder,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    internal static OrganizationPositionResponse Map(OrganizationPositionRecord record) =>
        new(
            record.Id,
            record.Code,
            record.Name,
            record.UnitId,
            record.UnitCode,
            record.UnitName,
            record.PositionLevelId,
            record.PositionLevelCode,
            record.PositionLevelName,
            record.DisplayOrder,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<OrganizationPositionResponse> NotFound() =>
        Result<OrganizationPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.PositionNotFound,
            "The organization position was not found.",
            ErrorType.NotFound));
}
