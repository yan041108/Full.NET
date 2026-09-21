using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageOpenAccessClients;

/// <summary>OpenAccess 接入方应用分页列表与详情只读查询。</summary>
internal sealed class OpenAccessClientQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按标识读取接入方应用详情。</summary>
    /// <param name="clientId">接入方应用标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>应用详情或稳定业务错误。</returns>
    public async Task<Result<OpenAccessClientResponse>> GetByIdAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OpenAccessClientDetailRow>(
                OpenAccessClientSql.FindById,
                IdentitySqlParameters.Create(("ClientId", clientId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        return Result<OpenAccessClientResponse>.Success(Map(row));
    }

    /// <summary>分页查询接入方应用列表。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="userId">可选的绑定用户标识筛选。</param>
    /// <param name="usernameContains">可选的绑定用户登录名模糊筛选。</param>
    /// <param name="nameContains">可选的应用名称模糊筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<OpenAccessClientResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? userId,
        string? usernameContains,
        string? nameContains,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = IdentitySqlParameters.Create(
            ("UserId", userId),
            ("UsernameContains", NormalizeFilter(usernameContains)),
            ("NameContains", NormalizeFilter(nameContains)),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                OpenAccessClientSql.CountSqlServer,
                OpenAccessClientSql.ListSqlServer),
            DatabaseProvider.MySql => (
                OpenAccessClientSql.CountMySql,
                OpenAccessClientSql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<OpenAccessClientDetailRow>(
                listStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<OpenAccessClientResponse>>.Success(
            new PagedResult<OpenAccessClientResponse>(items, page, pageSize, total));
    }

    internal static OpenAccessClientResponse Map(OpenAccessClientDetailRow row) =>
        new(
            row.Id,
            row.ApiKeyId,
            row.UserId,
            row.Username,
            row.Name,
            row.Description,
            row.Remark,
            row.AccessKeyId,
            ApiKeyAuthenticationService.DeserializePermissions(row.PermissionsJson),
            row.ExpiresAtUtc,
            row.DailyRequestQuota,
            row.IsActive,
            row.LastUsedAtUtc,
            row.CreatedAtUtc,
            row.Version);

    private static string? NormalizeFilter(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<OpenAccessClientResponse> NotFound() =>
        Result<OpenAccessClientResponse>.Failure(new Error(
            IdentityErrorCodes.OpenAccessClientNotFound,
            "The OpenAccess client was not found.",
            ErrorType.NotFound));
}
