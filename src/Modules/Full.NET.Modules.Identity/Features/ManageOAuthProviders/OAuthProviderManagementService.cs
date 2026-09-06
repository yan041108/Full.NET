using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Features.ManageOAuthProviders;

/// <summary>OAuth 提供程序创建、更新与删除。</summary>
internal sealed class OAuthProviderManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    OAuthProviderQueryService queries,
    OAuthClientSecretProtector clientSecretProtector,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly Regex ProviderKeyPattern = new(
        "^[a-z][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>显示名称允许的最大字符数。</summary>
    internal const int MaxDisplayNameLength = 128;

    /// <summary>Authority 允许的最大字符数。</summary>
    internal const int MaxAuthorityLength = 512;

    /// <summary>客户端标识允许的最大字符数。</summary>
    internal const int MaxClientIdLength = 256;

    /// <summary>Scopes 允许的最大字符数。</summary>
    internal const int MaxScopesLength = 512;

    /// <summary>回调路径允许的最大字符数。</summary>
    internal const int MaxRedirectPathLength = 256;

    /// <summary>默认授权范围。</summary>
    internal const string DefaultScopes = "openid profile email";

    /// <summary>默认回调路径。</summary>
    internal const string DefaultRedirectPath = "/api/v1/identity/oauth/callback";

    /// <summary>创建 OAuth 提供程序。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建结果或稳定业务错误。</returns>
    public Task<Result<OAuthProviderResponse>> CreateAsync(
        CreateOAuthProviderRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    /// <summary>更新 OAuth 提供程序。</summary>
    /// <param name="providerId">提供程序标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的提供程序或稳定业务错误。</returns>
    public Task<Result<OAuthProviderResponse>> UpdateAsync(
        Guid providerId,
        UpdateOAuthProviderRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(providerId, request, token),
            cancellationToken);

    /// <summary>删除 OAuth 提供程序。</summary>
    /// <param name="providerId">提供程序标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除成功或稳定业务错误。</returns>
    public Task<Result<bool>> DeleteAsync(
        Guid providerId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(providerId, token),
            cancellationToken);

    private async Task<Result<OAuthProviderResponse>> CreateCoreAsync(
        CreateOAuthProviderRequest request,
        CancellationToken cancellationToken)
    {
        var metadataValidation = ValidateMetadata(
            request.ProviderKey,
            request.DisplayName,
            request.Authority,
            request.ClientId,
            request.Scopes,
            request.RedirectPath);
        if (!metadataValidation.IsSuccess)
        {
            return Result<OAuthProviderResponse>.Failure(metadataValidation.Error!);
        }

        if (string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return Result<OAuthProviderResponse>.Failure(new Error(
                IdentityErrorCodes.OAuthProviderClientSecretRequired,
                "Client secret is required when creating an OAuth provider.",
                ErrorType.Validation));
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<OAuthProviderRecord>(
                OAuthProviderSql.FindByProviderKey,
                IdentitySqlParameters.Create(("ProviderKey", metadataValidation.Value!.ProviderKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return KeyExists();
        }

        var now = clock.UtcNow;
        var providerId = idGenerator.NewId();
        var record = new OAuthProviderRecord
        {
            Id = providerId,
            ProviderKey = metadataValidation.Value.ProviderKey,
            DisplayName = metadataValidation.Value.DisplayName,
            Authority = metadataValidation.Value.Authority,
            ClientId = metadataValidation.Value.ClientId,
            ClientSecretProtected = clientSecretProtector.Protect(request.ClientSecret.Trim()),
            Scopes = metadataValidation.Value.Scopes,
            RedirectPath = metadataValidation.Value.RedirectPath,
            IsEnabled = request.IsEnabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = null,
            Version = 1,
        };
        await commandExecutor.ExecuteAsync(
                OAuthProviderSql.Insert,
                record,
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(providerId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OAuthProviderResponse>> UpdateCoreAsync(
        Guid providerId,
        UpdateOAuthProviderRequest request,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<OAuthProviderRecord>(
                OAuthProviderSql.FindById,
                IdentitySqlParameters.Create(("ProviderId", providerId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFound();
        }

        var metadataValidation = ValidateMetadata(
            current.ProviderKey,
            request.DisplayName,
            request.Authority,
            request.ClientId,
            request.Scopes,
            request.RedirectPath);
        if (!metadataValidation.IsSuccess)
        {
            return Result<OAuthProviderResponse>.Failure(metadataValidation.Error!);
        }

        var protectedSecret = string.IsNullOrWhiteSpace(request.ClientSecret)
            ? current.ClientSecretProtected
            : clientSecretProtector.Protect(request.ClientSecret.Trim());
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                OAuthProviderSql.Update,
                IdentitySqlParameters.Create(
                    ("ProviderId", providerId),
                    ("DisplayName", metadataValidation.Value!.DisplayName),
                    ("Authority", metadataValidation.Value.Authority),
                    ("ClientId", metadataValidation.Value.ClientId),
                    ("ClientSecretProtected", protectedSecret),
                    ("Scopes", metadataValidation.Value.Scopes),
                    ("RedirectPath", metadataValidation.Value.RedirectPath),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(providerId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid providerId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<OAuthProviderRecord>(
                OAuthProviderSql.FindById,
                IdentitySqlParameters.Create(("ProviderId", providerId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.OAuthProviderNotFound,
                "The OAuth provider was not found.",
                ErrorType.NotFound));
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OAuthProviderSql.Delete,
                IdentitySqlParameters.Create(("ProviderId", providerId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.OAuthProviderNotFound,
                "The OAuth provider was not found.",
                ErrorType.NotFound));
        }

        return Result<bool>.Success(true);
    }

    internal static Result<ValidatedMetadata> ValidateMetadata(
        string? providerKey,
        string? displayName,
        string? authority,
        string? clientId,
        string? scopes,
        string? redirectPath)
    {
        if (string.IsNullOrWhiteSpace(providerKey)
            || !ProviderKeyPattern.IsMatch(providerKey.Trim()))
        {
            return Result<ValidatedMetadata>.Failure(new Error(
                IdentityErrorCodes.OAuthProviderInvalidKey,
                "Provider key must be a lowercase machine code.",
                ErrorType.Validation));
        }

        if (string.IsNullOrWhiteSpace(displayName)
            || displayName.Trim().Length > MaxDisplayNameLength
            || string.IsNullOrWhiteSpace(authority)
            || authority.Trim().Length > MaxAuthorityLength
            || !Uri.TryCreate(authority.Trim(), UriKind.Absolute, out _)
            || string.IsNullOrWhiteSpace(clientId)
            || clientId.Trim().Length > MaxClientIdLength)
        {
            return Result<ValidatedMetadata>.Failure(new Error(
                IdentityErrorCodes.OAuthProviderInvalidMetadata,
                "OAuth provider metadata is invalid.",
                ErrorType.Validation));
        }

        var normalizedScopes = string.IsNullOrWhiteSpace(scopes)
            ? DefaultScopes
            : scopes.Trim();
        if (normalizedScopes.Length > MaxScopesLength)
        {
            return Result<ValidatedMetadata>.Failure(new Error(
                IdentityErrorCodes.OAuthProviderInvalidMetadata,
                "OAuth provider metadata is invalid.",
                ErrorType.Validation));
        }

        var normalizedRedirectPath = string.IsNullOrWhiteSpace(redirectPath)
            ? DefaultRedirectPath
            : redirectPath.Trim();
        if (!normalizedRedirectPath.StartsWith("/", StringComparison.Ordinal)
            || normalizedRedirectPath.Length > MaxRedirectPathLength)
        {
            return Result<ValidatedMetadata>.Failure(new Error(
                IdentityErrorCodes.OAuthProviderInvalidMetadata,
                "OAuth provider metadata is invalid.",
                ErrorType.Validation));
        }

        return Result<ValidatedMetadata>.Success(new ValidatedMetadata(
            providerKey.Trim(),
            displayName.Trim(),
            authority.Trim().TrimEnd('/'),
            clientId.Trim(),
            normalizedScopes,
            normalizedRedirectPath));
    }

    internal sealed record ValidatedMetadata(
        string ProviderKey,
        string DisplayName,
        string Authority,
        string ClientId,
        string Scopes,
        string RedirectPath);

    private static Result<OAuthProviderResponse> NotFound() =>
        Result<OAuthProviderResponse>.Failure(new Error(
            IdentityErrorCodes.OAuthProviderNotFound,
            "The OAuth provider was not found.",
            ErrorType.NotFound));

    private static Result<OAuthProviderResponse> VersionConflict() =>
        Result<OAuthProviderResponse>.Failure(new Error(
            IdentityErrorCodes.OAuthProviderVersionConflict,
            "The OAuth provider was updated by another request.",
            ErrorType.Conflict));

    private static Result<OAuthProviderResponse> KeyExists() =>
        Result<OAuthProviderResponse>.Failure(new Error(
            IdentityErrorCodes.OAuthProviderKeyExists,
            "An OAuth provider with the same key already exists.",
            ErrorType.Conflict));
}
