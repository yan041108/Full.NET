using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageOAuthProviders;

/// <summary>OAuth 提供程序分页列表与详情只读查询。</summary>
internal sealed class OAuthProviderQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按标识读取 OAuth 提供程序详情。</summary>
    /// <param name="providerId">提供程序标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提供程序详情或稳定业务错误。</returns>
    public async Task<Result<OAuthProviderResponse>> GetByIdAsync(
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<OAuthProviderRecord>(
                OAuthProviderSql.FindById,
                IdentitySqlParameters.Create(("ProviderId", providerId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        return Result<OAuthProviderResponse>.Success(MapPublic(row));
    }

    /// <summary>分页查询 OAuth 提供程序列表。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="providerKeyContains">可选的机器码模糊筛选。</param>
    /// <param name="displayNameContains">可选的显示名称模糊筛选。</param>
    /// <param name="isEnabled">可选的启用状态筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<OAuthProviderResponse>>> ListAsync(
        int page,
        int pageSize,
        string? providerKeyContains,
        string? displayNameContains,
        bool? isEnabled,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = IdentitySqlParameters.Create(
            ("ProviderKeyContains", NormalizeFilter(providerKeyContains)),
            ("DisplayNameContains", NormalizeFilter(displayNameContains)),
            ("IsEnabled", isEnabled),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                OAuthProviderSql.CountSqlServer,
                OAuthProviderSql.ListSqlServer),
            DatabaseProvider.MySql => (
                OAuthProviderSql.CountMySql,
                OAuthProviderSql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<OAuthProviderRecord>(
                listStatement,
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(MapPublic).ToArray();
        return Result<PagedResult<OAuthProviderResponse>>.Success(
            new PagedResult<OAuthProviderResponse>(items, page, pageSize, total));
    }

    /// <summary>列出已启用的公开提供程序摘要。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>公开提供程序列表。</returns>
    public async Task<Result<IReadOnlyList<PublicOAuthProviderResponse>>> ListEnabledPublicAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<OAuthPublicProviderRecord>(
                OAuthProviderSql.ListEnabledPublic,
                IdentitySqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows
            .Select(row => new PublicOAuthProviderResponse(row.ProviderKey, row.DisplayName))
            .ToArray();
        return Result<IReadOnlyList<PublicOAuthProviderResponse>>.Success(items);
    }

    internal static OAuthProviderResponse MapPublic(OAuthProviderRecord row) =>
        new(
            row.Id,
            row.ProviderKey,
            row.DisplayName,
            row.Authority,
            row.ClientId,
            row.Scopes,
            row.RedirectPath,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result<OAuthProviderResponse> NotFound() =>
        Result<OAuthProviderResponse>.Failure(new Error(
            IdentityErrorCodes.OAuthProviderNotFound,
            "The OAuth provider was not found.",
            ErrorType.NotFound));

}
