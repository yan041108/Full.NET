using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.ManageOAuthLinks;

/// <summary>当前用户 OAuth 绑定只读查询。</summary>
internal sealed class OAuthUserLinkQueryService(IQueryExecutor queryExecutor)
{
    /// <summary>列出当前用户的 OAuth 绑定。</summary>
    /// <param name="principal">当前认证主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>绑定列表或稳定业务错误。</returns>
    public async Task<Result<IReadOnlyList<OAuthUserLinkResponse>>> ListForCurrentUserAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
        {
            return Unauthorized();
        }

        var rows = await queryExecutor.QueryAsync<OAuthUserLinkWithProviderRecord>(
                OAuthUserLinkSql.ListByUserId,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<IReadOnlyList<OAuthUserLinkResponse>>.Success(items);
    }

    private static OAuthUserLinkResponse Map(OAuthUserLinkWithProviderRecord row) =>
        new(
            row.Id,
            row.ProviderKey,
            row.ProviderDisplayName,
            row.Subject,
            row.Email,
            row.EmailVerified,
            row.DisplayName,
            row.LinkedAtUtc,
            row.LastUsedAtUtc);

    private static Result<IReadOnlyList<OAuthUserLinkResponse>> Unauthorized() =>
        Result<IReadOnlyList<OAuthUserLinkResponse>>.Failure(new Error(
            IdentityErrorCodes.InvalidCredentials,
            "Authentication is required.",
            ErrorType.Unauthorized));
}
