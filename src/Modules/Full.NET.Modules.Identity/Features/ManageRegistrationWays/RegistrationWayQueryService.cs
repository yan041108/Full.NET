using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageRegistrationWays;

/// <summary>注册方式分页列表与详情只读查询。</summary>
internal sealed class RegistrationWayQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按标识读取注册方式详情。</summary>
    /// <param name="wayId">注册方式标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>注册方式详情或稳定业务错误。</returns>
    public async Task<Result<RegistrationWayResponse>> GetByIdAsync(
        Guid wayId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<RegistrationWayRecord>(
                RegistrationWaySql.FindById,
                IdentitySqlParameters.Create(("WayId", wayId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        return Result<RegistrationWayResponse>.Success(Map(row));
    }

    /// <summary>分页查询注册方式列表。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="tenantId">可选的租户筛选。</param>
    /// <param name="nameContains">可选的名称模糊筛选。</param>
    /// <param name="isEnabled">可选的启用状态筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<RegistrationWayResponse>>> ListAsync(
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
                RegistrationWaySql.CountSqlServer,
                RegistrationWaySql.ListSqlServer),
            DatabaseProvider.MySql => (
                RegistrationWaySql.CountMySql,
                RegistrationWaySql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<RegistrationWayRecord>(
                listStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<RegistrationWayResponse>>.Success(
            new PagedResult<RegistrationWayResponse>(items, page, pageSize, total));
    }

    internal static RegistrationWayResponse Map(RegistrationWayRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.Name,
            row.Code,
            row.IsEnabled,
            row.RoleId,
            row.OrganizationUnitId,
            row.PositionId,
            row.SortOrder,
            row.Remark,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<RegistrationWayResponse> NotFound() =>
        Result<RegistrationWayResponse>.Failure(new Error(
            IdentityErrorCodes.RegistrationWayNotFound,
            "The registration way was not found.",
            ErrorType.NotFound));
}
