using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Full.NET.Modules.Organization.Queries;
using Full.NET.Modules.Organization.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits.Queries;

/// <summary>后台用户机构隶属列表；先解析数据范围，再以静态只读 SQL 搜索及分页。</summary>
internal sealed class TenantUserUnitListQueryService(
    IQueryExecutor queryExecutor,
    TenantUserUnitListScopeReader scopeReader,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<OrganizationUserUnitResponse>>> ListAsync(
        Guid currentUserId, bool isSuperAdministrator, int page, int pageSize,
        Guid? userId, Guid? unitId, string? userSearch = null, string? unitSearch = null,
        bool? isActive = null, CancellationToken cancellationToken = default)
    {
        if (!AssignmentListSearch.IsValid(userSearch) || !AssignmentListSearch.IsValid(unitSearch))
        {
            return Result<PagedResult<OrganizationUserUnitResponse>>.Failure(new Error(
                ValidationErrorCodes.Failed, "Search terms must not exceed 100 characters.", ErrorType.Validation));
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var visibleIds = await scopeReader.ReadAsync(currentUserId, isSuperAdministrator, cancellationToken).ConfigureAwait(false);
        // JSON 集合避免大范围授权触及 SQL Server 参数数量上限；租户条件仍由 SQL 独立落实。
        var parameters = OrganizationSqlParameters.Create(
            ("UserId", userId), ("UnitId", unitId),
            ("UserSearch", AssignmentListSearch.Pattern(userSearch)), ("UnitSearch", AssignmentListSearch.Pattern(unitSearch)),
            ("IsActive", isActive), ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize),
            ("Unrestricted", visibleIds is null),
            ("VisibleUnitIds", JsonSerializer.Serialize(visibleIds ?? [], OrganizationJsonSerializerContext.Default.GuidArray)));
        var (count, list) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (TenantUserUnitListSql.CountSqlServer, TenantUserUnitListSql.ListSqlServer),
            DatabaseProvider.MySql => (TenantUserUnitListSql.CountMySql, TenantUserUnitListSql.ListMySql),
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(count, parameters, cancellationToken).ConfigureAwait(false);
        var items = await queryExecutor.QueryAsync<OrganizationUserUnitResponse>(list, parameters, cancellationToken).ConfigureAwait(false);
        return Result<PagedResult<OrganizationUserUnitResponse>>.Success(new(items, page, pageSize, total));
    }
}
