using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.ManageOAuthLinks;

/// <summary>当前用户 OAuth 绑定解除。</summary>
internal sealed class OAuthUserLinkManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction)
{
    /// <summary>解除当前用户的指定 OAuth 绑定。</summary>
    /// <param name="principal">当前认证主体。</param>
    /// <param name="linkId">绑定标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除成功或稳定业务错误。</returns>
    public Task<Result<bool>> UnbindAsync(
        ClaimsPrincipal principal,
        Guid linkId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UnbindCoreAsync(principal, linkId, token),
            cancellationToken);

    private async Task<Result<bool>> UnbindCoreAsync(
        ClaimsPrincipal principal,
        Guid linkId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
        {
            return Unauthorized();
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<OAuthUserLinkRecord>(
                OAuthUserLinkSql.FindByIdAndUserId,
                IdentitySqlParameters.Create(
                    ("LinkId", linkId),
                    ("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OAuthUserLinkSql.DeleteByIdAndUserId,
                IdentitySqlParameters.Create(
                    ("LinkId", linkId),
                    ("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return NotFound();
        }

        return Result<bool>.Success(true);
    }

    private static Result<bool> Unauthorized() =>
        Result<bool>.Failure(new Error(
            IdentityErrorCodes.InvalidCredentials,
            "Authentication is required.",
            ErrorType.Unauthorized));

    private static Result<bool> NotFound() =>
        Result<bool>.Failure(new Error(
            IdentityErrorCodes.OAuthUserLinkNotFound,
            "The OAuth user link was not found.",
            ErrorType.NotFound));
}
