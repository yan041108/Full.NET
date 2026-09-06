using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageLdapConnections;

/// <summary>LDAP 连接分页列表与详情只读查询。</summary>
internal sealed class LdapConnectionQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按标识读取 LDAP 连接详情。</summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>连接详情或稳定业务错误。</returns>
    public async Task<Result<LdapConnectionResponse>> GetByIdAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<LdapConnectionRecord>(
                LdapConnectionSql.FindById,
                IdentitySqlParameters.Create(("ConnectionId", connectionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        return Result<LdapConnectionResponse>.Success(MapPublic(row));
    }

    /// <summary>分页查询 LDAP 连接列表。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="tenantId">可选的租户筛选。</param>
    /// <param name="nameContains">可选的名称模糊筛选。</param>
    /// <param name="isEnabled">可选的启用状态筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<LdapConnectionResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? tenantId,
        string? nameContains,
        bool? isEnabled,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = IdentitySqlParameters.Create(
            ("TenantId", tenantId),
            ("NameContains", NormalizeFilter(nameContains)),
            ("IsEnabled", isEnabled),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                LdapConnectionSql.CountSqlServer,
                LdapConnectionSql.ListSqlServer),
            DatabaseProvider.MySql => (
                LdapConnectionSql.CountMySql,
                LdapConnectionSql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<LdapConnectionRecord>(
                listStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(MapPublic).ToArray();
        return Result<PagedResult<LdapConnectionResponse>>.Success(
            new PagedResult<LdapConnectionResponse>(items, page, pageSize, total));
    }

    internal static LdapConnectionResponse MapPublic(LdapConnectionRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.Name,
            row.Host,
            row.Port,
            row.UseTls,
            row.BaseDn,
            row.BindDn,
            row.UserSearchFilter,
            row.UserAccountAttribute,
            row.EmployeeIdAttribute,
            row.DepartmentCodeAttribute,
            row.SyncSearchBaseDn,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<LdapConnectionResponse> NotFound() =>
        Result<LdapConnectionResponse>.Failure(new Error(
            IdentityErrorCodes.LdapConnectionNotFound,
            "The LDAP connection was not found.",
            ErrorType.NotFound));
}
