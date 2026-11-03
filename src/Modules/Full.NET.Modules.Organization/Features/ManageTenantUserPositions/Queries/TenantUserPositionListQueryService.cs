using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Full.NET.Modules.Organization.Queries;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserPositions.Queries;

/// <summary>后台用户职位隶属列表；保留租户内目录级读取，不额外施加机构数据范围。</summary>
internal sealed class TenantUserPositionListQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<OrganizationUserPositionResponse>>> ListAsync(
        int page, int pageSize, Guid? userId, Guid? positionId,
        string? userSearch = null, string? positionSearch = null, bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        if (!AssignmentListSearch.IsValid(userSearch) || !AssignmentListSearch.IsValid(positionSearch))
        {
            return Result<PagedResult<OrganizationUserPositionResponse>>.Failure(new Error(
                ValidationErrorCodes.Failed, "Search terms must not exceed 100 characters.", ErrorType.Validation));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var parameters = OrganizationSqlParameters.Create(
            ("UserId", userId), ("PositionId", positionId),
            ("UserSearch", AssignmentListSearch.Pattern(userSearch)), ("PositionSearch", AssignmentListSearch.Pattern(positionSearch)),
            ("IsActive", isActive), ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        var (count, list) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (TenantUserPositionListSql.CountSqlServer, TenantUserPositionListSql.ListSqlServer),
            DatabaseProvider.MySql => (TenantUserPositionListSql.CountMySql, TenantUserPositionListSql.ListMySql),
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(count, parameters, cancellationToken).ConfigureAwait(false);
        var items = await queryExecutor.QueryAsync<OrganizationUserPositionResponse>(list, parameters, cancellationToken).ConfigureAwait(false);
        return Result<PagedResult<OrganizationUserPositionResponse>>.Success(new(items, page, pageSize, total));
    }
}
