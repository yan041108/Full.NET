using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.OAuth;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.Modules.Identity.Features.OAuthFlow;

/// <summary>OAuth 授权、回调与用户绑定编排。</summary>
internal sealed class OAuthFlowService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOidcClient oidcClient,
    OAuthClientSecretProtector clientSecretProtector,
    OAuthReturnUrlValidator returnUrlValidator,
    IdentityOAuthLoginSessionService loginSessionService)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    /// <summary>发起 OAuth 授权并重定向到 IdP。</summary>
    /// <param name="providerKey">提供程序机器码。</param>
    /// <param name="mode">login 或 bind。</param>
    /// <param name="returnUrl">完成后重定向地址。</param>
    /// <param name="requestOrigin">当前请求 origin。</param>
    /// <param name="principal">当前认证主体；bind 模式必须已登录。</param>
    /// <param name="refreshToken">bind 模式下可选的刷新令牌（来自 Cookie）。</param>
    /// <param name="requestScheme">请求 scheme。</param>
    /// <param name="requestHost">请求 host。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>授权 URL 或稳定业务错误。</returns>
    public async Task<Result<string>> BeginAuthorizeAsync(
        string providerKey,
        string mode,
        string? returnUrl,
        string requestOrigin,
        ClaimsPrincipal principal,
        string? refreshToken,
        string requestScheme,
        string requestHost,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidMode(mode))
        {
            return InvalidMode();
        }

        var normalizedReturnUrl = returnUrlValidator.Normalize(returnUrl, requestOrigin);
        if (normalizedReturnUrl is null)
        {
            return InvalidReturnUrl();
        }

        Guid? bindUserId = null;
        if (string.Equals(mode, OAuthAuthorizationModes.Bind, StringComparison.Ordinal))
        {
            bindUserId = await ResolveBindUserIdAsync(principal, refreshToken, cancellationToken)
                .ConfigureAwait(false);
            if (!bindUserId.HasValue)
            {
                return UnauthorizedBind();
            }
        }

        var provider = await queryExecutor.QuerySingleOrDefaultAsync<OAuthProviderRecord>(
                OAuthProviderSql.FindByProviderKey,
                IdentitySqlParameters.Create(("ProviderKey", providerKey.Trim())),
                cancellationToken)
            .ConfigureAwait(false);
        if (provider is null || !provider.IsEnabled)
        {
            return ProviderUnavailable();
        }

        await commandExecutor.ExecuteAsync(
                OAuthAuthorizationStateSql.DeleteExpired,
                IdentitySqlParameters.Create(("NowUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);

        var stateId = idGenerator.NewId();
        var codeVerifier = OAuthPkce.CreateCodeVerifier();
        var codeChallenge = OAuthPkce.CreateCodeChallenge(codeVerifier);
        var nonce = idGenerator.NewId().ToString("N");
        var now = clock.UtcNow;
        var state = new OAuthAuthorizationStateRecord
        {
            Id = stateId,
            ProviderKey = provider.ProviderKey,
            CodeVerifier = codeVerifier,
            Nonce = nonce,
            Mode = mode,
            UserId = bindUserId,
            ReturnUrl = normalizedReturnUrl,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(StateLifetime),
        };
        await commandExecutor.ExecuteAsync(
                OAuthAuthorizationStateSql.Insert,
                state,
                cancellationToken)
            .ConfigureAwait(false);

        var discovery = await oidcClient
            .GetDiscoveryDocumentAsync(provider.Authority, cancellationToken)
            .ConfigureAwait(false);
        var redirectUri = BuildRedirectUri(requestScheme, requestHost, provider.RedirectPath);
        var authorizationUrl = oidcClient.BuildAuthorizationUrl(
            discovery,
            provider.ClientId,
            redirectUri,
            provider.Scopes,
            stateId.ToString("D"),
            nonce,
            codeChallenge);
        return Result<string>.Success(authorizationUrl);
    }

    /// <summary>处理 OAuth 回调并完成 login 或 bind。</summary>
    /// <param name="code">授权码。</param>
    /// <param name="state">OAuth state。</param>
    /// <param name="requestScheme">请求 scheme。</param>
    /// <param name="requestHost">请求 host。</param>
    /// <param name="client">客户端请求上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>回调处理结果。</returns>
    public Task<Result<OAuthCallbackResult>> HandleCallbackAsync(
        string code,
        string state,
        string requestScheme,
        string requestHost,
        ClientRequestContext client,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => HandleCallbackCoreAsync(
                code,
                state,
                requestScheme,
                requestHost,
                client,
                token),
            cancellationToken);

    private async Task<Result<OAuthCallbackResult>> HandleCallbackCoreAsync(
        string code,
        string state,
        string requestScheme,
        string requestHost,
        ClientRequestContext client,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(state, out var stateId))
        {
            return InvalidStateResult("/");
        }

        var authorizationState = await queryExecutor
            .QuerySingleOrDefaultAsync<OAuthAuthorizationStateRecord>(
                OAuthAuthorizationStateSql.FindById,
                IdentitySqlParameters.Create(("StateId", stateId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (authorizationState is null || authorizationState.ExpiresAtUtc <= clock.UtcNow)
        {
            await DeleteStateAsync(stateId, cancellationToken).ConfigureAwait(false);
            return InvalidStateResult("/");
        }

        var returnUrl = authorizationState.ReturnUrl;
        var provider = await queryExecutor.QuerySingleOrDefaultAsync<OAuthProviderRecord>(
                OAuthProviderSql.FindByProviderKey,
                IdentitySqlParameters.Create(("ProviderKey", authorizationState.ProviderKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (provider is null || !provider.IsEnabled)
        {
            await DeleteStateAsync(stateId, cancellationToken).ConfigureAwait(false);
            return ErrorRedirect(returnUrl, OAuthCallbackErrorCodes.ProviderUnavailable);
        }

        OidcExternalIdentityClaims externalClaims;
        try
        {
            var discovery = await oidcClient
                .GetDiscoveryDocumentAsync(provider.Authority, cancellationToken)
                .ConfigureAwait(false);
            var redirectUri = BuildRedirectUri(requestScheme, requestHost, provider.RedirectPath);
            var clientSecret = clientSecretProtector.Unprotect(provider.ClientSecretProtected);
            (_, externalClaims) = await oidcClient.ExchangeCodeAsync(
                    discovery,
                    provider.ClientId,
                    clientSecret,
                    redirectUri,
                    code,
                    authorizationState.CodeVerifier,
                    authorizationState.Nonce,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await DeleteStateAsync(stateId, cancellationToken).ConfigureAwait(false);
            return ErrorRedirect(returnUrl, OAuthCallbackErrorCodes.TokenExchangeFailed);
        }

        await DeleteStateAsync(stateId, cancellationToken).ConfigureAwait(false);

        if (string.Equals(
                authorizationState.Mode,
                OAuthAuthorizationModes.Bind,
                StringComparison.Ordinal))
        {
            return await HandleBindCallbackAsync(
                    authorizationState,
                    provider,
                    externalClaims,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await HandleLoginCallbackAsync(
                returnUrl,
                provider,
                externalClaims,
                client,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OAuthCallbackResult>> HandleLoginCallbackAsync(
        string returnUrl,
        OAuthProviderRecord provider,
        OidcExternalIdentityClaims externalClaims,
        ClientRequestContext client,
        CancellationToken cancellationToken)
    {
        var link = await queryExecutor.QuerySingleOrDefaultAsync<OAuthUserLinkRecord>(
                OAuthUserLinkSql.FindByProviderKeyAndSubject,
                IdentitySqlParameters.Create(
                    ("ProviderKey", provider.ProviderKey),
                    ("Subject", externalClaims.Subject)),
                cancellationToken)
            .ConfigureAwait(false);
        if (link is null)
        {
            return ErrorRedirect(returnUrl, OAuthCallbackErrorCodes.AccountNotLinked);
        }

        var userRecord = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", link.UserId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (userRecord is null || !userRecord.IsActive)
        {
            return ErrorRedirect(returnUrl, OAuthCallbackErrorCodes.AccountNotLinked);
        }

        await commandExecutor.ExecuteAsync(
                OAuthUserLinkSql.UpdateLastUsed,
                IdentitySqlParameters.Create(
                    ("LinkId", link.Id),
                    ("LastUsedAtUtc", clock.UtcNow),
                    ("Version", link.Version)),
                cancellationToken)
            .ConfigureAwait(false);

        var sessionResult = await loginSessionService.IssueSessionAsync(
                link.UserId,
                userRecord.NormalizedUsername,
                client,
                cancellationToken)
            .ConfigureAwait(false);
        if (!sessionResult.IsSuccess)
        {
            return ErrorRedirect(returnUrl, OAuthCallbackErrorCodes.TokenExchangeFailed);
        }

        return Result<OAuthCallbackResult>.Success(new OAuthCallbackResult(
            OAuthReturnUrlValidator.AppendQuery(returnUrl, "oauth", "success"),
            sessionResult.Value!));
    }

    private async Task<Result<OAuthCallbackResult>> HandleBindCallbackAsync(
        OAuthAuthorizationStateRecord authorizationState,
        OAuthProviderRecord provider,
        OidcExternalIdentityClaims externalClaims,
        CancellationToken cancellationToken)
    {
        if (!authorizationState.UserId.HasValue)
        {
            return ErrorRedirect(
                authorizationState.ReturnUrl,
                OAuthCallbackErrorCodes.InvalidState);
        }

        var existingBySubject = await queryExecutor
            .QuerySingleOrDefaultAsync<OAuthUserLinkRecord>(
                OAuthUserLinkSql.FindByProviderKeyAndSubject,
                IdentitySqlParameters.Create(
                    ("ProviderKey", provider.ProviderKey),
                    ("Subject", externalClaims.Subject)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existingBySubject is not null
            && existingBySubject.UserId != authorizationState.UserId.Value)
        {
            return ErrorRedirect(
                authorizationState.ReturnUrl,
                OAuthCallbackErrorCodes.AccountConflict);
        }

        var existingByUser = await queryExecutor.QuerySingleOrDefaultAsync<OAuthUserLinkRecord>(
                OAuthUserLinkSql.FindByUserIdAndProviderKey,
                IdentitySqlParameters.Create(
                    ("UserId", authorizationState.UserId.Value),
                    ("ProviderKey", provider.ProviderKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existingByUser is not null)
        {
            return Result<OAuthCallbackResult>.Success(new OAuthCallbackResult(
                OAuthReturnUrlValidator.AppendQuery(authorizationState.ReturnUrl, "oauth", "success"),
                null));
        }

        if (existingBySubject is not null)
        {
            return Result<OAuthCallbackResult>.Success(new OAuthCallbackResult(
                OAuthReturnUrlValidator.AppendQuery(authorizationState.ReturnUrl, "oauth", "success"),
                null));
        }

        var now = clock.UtcNow;
        var link = new OAuthUserLinkRecord
        {
            Id = idGenerator.NewId(),
            UserId = authorizationState.UserId.Value,
            ProviderKey = provider.ProviderKey,
            Subject = externalClaims.Subject,
            Email = externalClaims.Email,
            EmailVerified = externalClaims.EmailVerified,
            DisplayName = externalClaims.DisplayName,
            LinkedAtUtc = now,
            LastUsedAtUtc = now,
            Version = 1,
        };
        await commandExecutor.ExecuteAsync(
                OAuthUserLinkSql.Insert,
                link,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<OAuthCallbackResult>.Success(new OAuthCallbackResult(
            OAuthReturnUrlValidator.AppendQuery(authorizationState.ReturnUrl, "oauth", "success"),
            null));
    }

    private async Task DeleteStateAsync(Guid stateId, CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                OAuthAuthorizationStateSql.DeleteById,
                IdentitySqlParameters.Create(("StateId", stateId)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveBindUserIdAsync(
        ClaimsPrincipal principal,
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var principalUserId))
        {
            return principalUserId;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionByHash,
                IdentitySqlParameters.Create(("TokenHash", TokenHash.Compute(refreshToken))),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null
            || record.ExpiresAtUtc <= clock.UtcNow
            || record.RevokedAtUtc.HasValue
            || record.ConsumedAtUtc.HasValue
            || !record.IsActive)
        {
            return null;
        }

        return record.UserId;
    }

    private static string BuildRedirectUri(
        string requestScheme,
        string requestHost,
        string redirectPath) =>
        $"{requestScheme}://{requestHost}{redirectPath}";

    private static bool IsValidMode(string mode) =>
        string.Equals(mode, OAuthAuthorizationModes.Login, StringComparison.Ordinal)
        || string.Equals(mode, OAuthAuthorizationModes.Bind, StringComparison.Ordinal);

    private static Result<string> InvalidMode() =>
        Result<string>.Failure(new Error(
            IdentityErrorCodes.OAuthInvalidMode,
            "OAuth mode must be login or bind.",
            ErrorType.Validation));

    private static Result<string> InvalidReturnUrl() =>
        Result<string>.Failure(new Error(
            IdentityErrorCodes.OAuthInvalidReturnUrl,
            "Return URL is not allowed.",
            ErrorType.Validation));

    private static Result<string> UnauthorizedBind() =>
        Result<string>.Failure(new Error(
            IdentityErrorCodes.InvalidCredentials,
            "Bind mode requires an authenticated user.",
            ErrorType.Unauthorized));

    private static Result<string> ProviderUnavailable() =>
        Result<string>.Failure(new Error(
            IdentityErrorCodes.OAuthProviderDisabled,
            "The OAuth provider is not available.",
            ErrorType.NotFound));

    private static Result<OAuthCallbackResult> InvalidStateResult(string returnUrl) =>
        Result<OAuthCallbackResult>.Success(new OAuthCallbackResult(
            OAuthReturnUrlValidator.AppendQuery(returnUrl, "oauth_error", OAuthCallbackErrorCodes.InvalidState),
            null));

    private static Result<OAuthCallbackResult> ErrorRedirect(string returnUrl, string errorCode) =>
        Result<OAuthCallbackResult>.Success(new OAuthCallbackResult(
            OAuthReturnUrlValidator.AppendQuery(returnUrl, "oauth_error", errorCode),
            null));
}

/// <summary>OAuth 回调处理结果。</summary>
/// <param name="RedirectUrl">最终重定向 URL。</param>
/// <param name="LoginSession">login 模式成功时签发的会话；bind 模式为 <see langword="null"/>。</param>
internal sealed record OAuthCallbackResult(
    string RedirectUrl,
    Features.Login.LoginSessionResult? LoginSession);
