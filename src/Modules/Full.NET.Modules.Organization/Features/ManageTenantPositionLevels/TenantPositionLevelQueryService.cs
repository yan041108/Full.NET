using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantPositionLevels;

/// <summary>租户职级分页列表与详情只读查询。</summary>
internal sealed class TenantPositionLevelQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<OrganizationPositionLevelResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                PositionLevelSql.Count,
                null,
                cancellationToken)
            .ConfigureAwait(false);
        var listStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => PositionLevelSql.ListSqlServer,
            DatabaseProvider.MySql => PositionLevelSql.ListMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<OrganizationPositionLevelRecord>(
                listStatement,
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<OrganizationPositionLevelResponse>>.Success(
            new PagedResult<OrganizationPositionLevelResponse>(
                items,
                page,
                pageSize,
                total));
    }

    public async Task<Result<OrganizationPositionLevelResponse>> GetByIdAsync(
        Guid positionLevelId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionLevelRecord>(
                PositionLevelSql.FindById,
                new { PositionLevelId = positionLevelId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<OrganizationPositionLevelResponse>.Success(Map(record));
    }

    internal Task<Result<OrganizationPositionLevelResponse>> FindByIdAsync(
        Guid positionLevelId,
        CancellationToken cancellationToken = default) =>
        GetByIdAsync(positionLevelId, cancellationToken);

    private static OrganizationPositionLevelResponse Map(
        OrganizationPositionLevelRecord record) =>
        new(
            record.Id,
            record.Code,
            record.Name,
            record.DisplayOrder,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<OrganizationPositionLevelResponse> NotFound() =>
        Result<OrganizationPositionLevelResponse>.Failure(new Error(
            OrganizationErrorCodes.PositionLevelNotFound,
            "The organization position level was not found.",
            ErrorType.NotFound));
}
