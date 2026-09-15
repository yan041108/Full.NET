using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Security;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Abstractions.OpenIddictExceptions;

namespace Full.NET.Modules.Identity.Features.ManageOidcClients;

/// <summary>OIDC 客户端创建、更新、停用与密钥轮换；明文密钥只在创建/轮换响应中返回一次。</summary>
internal sealed class OidcClientManagementService(
    IOpenIddictApplicationManager applicationManager,
    OidcClientQueryService queries,
    IRandomTokenGenerator tokenGenerator,
    IdentityOidcGrantRevocationService grantRevocationService,
    IdentityOidcSessionService sessionService)
{
    internal const int MaxClientIdLength = 128;
    internal const int MaxDisplayNameLength = 128;
    internal const int MaxResourceAudienceLength = 256;
    private const string SecretPrefix = "fnoc_";

    public async Task<Result<CreateOidcClientResponse>> CreateAsync(
        CreateOidcClientRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreateRequest(request);
        if (!validation.IsSuccess)
        {
            return Result<CreateOidcClientResponse>.Failure(validation.Error!);
        }

        var normalized = validation.Value!;
        if (await applicationManager.FindByClientIdAsync(normalized.ClientId, cancellationToken)
                .ConfigureAwait(false) is not null)
        {
            return Result<CreateOidcClientResponse>.Failure(new Error(
                IdentityErrorCodes.OidcClientIdConflict,
                "The OIDC client_id already exists.",
                ErrorType.Conflict));
        }

        string? plainSecret = null;
        string? storedSecret = null;
        if (request.IsConfidential)
        {
            plainSecret = $"{SecretPrefix}{tokenGenerator.Generate(32)}";
            storedSecret = plainSecret;
        }

        var descriptor = IdentityOidcClientDescriptorFactory.Build(
            new IdentityOidcClientDescriptorFactory.Input(
                normalized.ClientId,
                storedSecret,
                normalized.DisplayName,
                normalized.RedirectUris,
                normalized.PostLogoutRedirectUris,
                normalized.Scopes,
                request.IsFirstParty,
                normalized.ResourceAudience));
        await applicationManager.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        var created = await applicationManager.FindByClientIdAsync(normalized.ClientId, cancellationToken)
            .ConfigureAwait(false);
        if (created is null)
        {
            return Result<CreateOidcClientResponse>.Failure(new Error(
                IdentityErrorCodes.OidcClientNotFound,
                "The OIDC client was not found after creation.",
                ErrorType.Unexpected));
        }

        var id = Guid.Parse(
            await applicationManager.GetIdAsync(created, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("OIDC application id missing."));
        var response = await queries.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return Result<CreateOidcClientResponse>.Failure(response.Error!);
        }

        return Result<CreateOidcClientResponse>.Success(
            new CreateOidcClientResponse(response.Value!, plainSecret));
    }

    public async Task<Result<OidcClientResponse>> UpdateAsync(
        Guid clientId,
        UpdateOidcClientRequest request,
        CancellationToken cancellationToken = default)
    {
        var application = await FindActiveApplicationAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (!application.IsSuccess)
        {
            return Result<OidcClientResponse>.Failure(application.Error!);
        }

        var validation = ValidateUpdateRequest(request);
        if (!validation.IsSuccess)
        {
            return Result<OidcClientResponse>.Failure(validation.Error!);
        }

        if (application.Value!.Version != request.Version)
        {
            return VersionConflict();
        }

        var normalized = validation.Value!;
        var descriptor = await PopulateDescriptorAsync(application.Value!.Entity, cancellationToken)
            .ConfigureAwait(false);
        descriptor.DisplayName = normalized.DisplayName;
        descriptor.RedirectUris.Clear();
        foreach (var redirectUri in normalized.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(redirectUri, UriKind.Absolute));
        }

        descriptor.PostLogoutRedirectUris.Clear();
        foreach (var postLogoutRedirectUri in normalized.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(postLogoutRedirectUri, UriKind.Absolute));
        }

        var allowedScopes = new HashSet<string>(normalized.Scopes, StringComparer.OrdinalIgnoreCase);
        allowedScopes.Add(Scopes.OpenId);
        allowedScopes.Add(Scopes.Profile);
        allowedScopes.Add(Scopes.OfflineAccess);
        descriptor.Permissions.RemoveWhere(permission =>
            permission.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal));
        foreach (var scope in allowedScopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        IdentityOidcClientMetadata.Write(
            descriptor,
            request.IsFirstParty,
            normalized.ResourceAudience,
            false);
        try
        {
            await applicationManager.UpdateAsync(application.Value!.Entity, descriptor, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ConcurrencyException)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(clientId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<OidcClientResponse>> DisableAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var application = await FindApplicationAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (!application.IsSuccess)
        {
            return Result<OidcClientResponse>.Failure(application.Error!);
        }

        if (application.Value!.IsDisabled)
        {
            return DisabledFailure<OidcClientResponse>();
        }

        var descriptor = await PopulateDescriptorAsync(application.Value!.Entity, cancellationToken)
            .ConfigureAwait(false);
        IdentityOidcClientMetadata.Write(
            descriptor,
            application.Value!.IsFirstParty,
            application.Value!.ResourceAudience,
            true);
        try
        {
            await applicationManager.UpdateAsync(application.Value!.Entity, descriptor, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ConcurrencyException)
        {
            return VersionConflict();
        }

        var oauthClientId = await applicationManager.GetClientIdAsync(
                application.Value!.Entity,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(oauthClientId))
        {
            await grantRevocationService.RevokeByApplicationIdAsync(clientId, cancellationToken)
                .ConfigureAwait(false);
            await sessionService.RevokeAllActiveApplicationSessionsByClientIdAsync(
                    oauthClientId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await queries.GetByIdAsync(clientId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RotateOidcClientSecretResponse>> RotateAsync(
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        var application = await FindActiveApplicationAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (!application.IsSuccess)
        {
            return Result<RotateOidcClientSecretResponse>.Failure(application.Error!);
        }

        if (!string.Equals(
                application.Value!.ClientType,
                ClientTypes.Confidential,
                StringComparison.Ordinal))
        {
            return Result<RotateOidcClientSecretResponse>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Only confidential OIDC clients support secret rotation.",
                ErrorType.Validation));
        }

        var plainSecret = $"{SecretPrefix}{tokenGenerator.Generate(32)}";
        var descriptor = await PopulateDescriptorAsync(application.Value!.Entity, cancellationToken)
            .ConfigureAwait(false);
        descriptor.ClientSecret = plainSecret;
        try
        {
            await applicationManager.UpdateAsync(application.Value!.Entity, descriptor, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ConcurrencyException)
        {
            return Result<RotateOidcClientSecretResponse>.Failure(VersionConflict().Error!);
        }

        var response = await queries.GetByIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return Result<RotateOidcClientSecretResponse>.Failure(response.Error!);
        }

        return Result<RotateOidcClientSecretResponse>.Success(
            new RotateOidcClientSecretResponse(response.Value!, plainSecret));
    }

    internal static Result<ValidatedCreateRequest> ValidateCreateRequest(CreateOidcClientRequest request)
    {
        var clientId = request.ClientId?.Trim() ?? string.Empty;
        if (clientId.Length is < 1 or > MaxClientIdLength)
        {
            return ValidationCreateFailure("Client id is invalid.");
        }

        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > MaxDisplayNameLength)
        {
            return ValidationCreateFailure("Display name is invalid.");
        }

        var redirectUris = NormalizeUris(request.RedirectUris);
        if (redirectUris.Count == 0)
        {
            return ValidationCreateFailure("At least one redirect URI is required.");
        }

        var postLogoutRedirectUris = NormalizeUris(request.PostLogoutRedirectUris ?? []);
        var scopes = NormalizeScopes(request.Scopes);
        if (!scopes.Contains(Scopes.OpenId, StringComparer.Ordinal))
        {
            return ValidationCreateFailure("The openid scope is required.");
        }

        var resourceAudience = NormalizeResourceAudience(request.ResourceAudience);
        if (!resourceAudience.IsSuccess)
        {
            return Result<ValidatedCreateRequest>.Failure(resourceAudience.Error!);
        }

        return Result<ValidatedCreateRequest>.Success(new ValidatedCreateRequest(
            clientId,
            displayName,
            redirectUris,
            postLogoutRedirectUris,
            scopes,
            resourceAudience.Value));
    }

    internal static Result<ValidatedUpdateRequest> ValidateUpdateRequest(UpdateOidcClientRequest request)
    {
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 1 or > MaxDisplayNameLength)
        {
            return ValidationUpdateFailure("Display name is invalid.");
        }

        var redirectUris = NormalizeUris(request.RedirectUris);
        if (redirectUris.Count == 0)
        {
            return ValidationUpdateFailure("At least one redirect URI is required.");
        }

        var postLogoutRedirectUris = NormalizeUris(request.PostLogoutRedirectUris ?? []);
        var scopes = NormalizeScopes(request.Scopes);
        if (!scopes.Contains(Scopes.OpenId, StringComparer.Ordinal))
        {
            return ValidationUpdateFailure("The openid scope is required.");
        }

        var resourceAudience = NormalizeResourceAudience(request.ResourceAudience);
        if (!resourceAudience.IsSuccess)
        {
            return Result<ValidatedUpdateRequest>.Failure(resourceAudience.Error!);
        }

        return Result<ValidatedUpdateRequest>.Success(new ValidatedUpdateRequest(
            displayName,
            redirectUris,
            postLogoutRedirectUris,
            scopes,
            resourceAudience.Value));
    }

    private async Task<Result<LoadedApplication>> FindApplicationAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var row = await queries.GetByIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (!row.IsSuccess)
        {
            return Result<LoadedApplication>.Failure(row.Error!);
        }

        var entity = await applicationManager.FindByIdAsync(clientId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            return Result<LoadedApplication>.Failure(NotFound().Error!);
        }

        return Result<LoadedApplication>.Success(new LoadedApplication(
            entity,
            row.Value!.ClientType,
            row.Value!.IsFirstParty,
            row.Value!.ResourceAudience,
            row.Value!.IsDisabled,
            row.Value!.Version));
    }

    private async Task<Result<LoadedApplication>> FindActiveApplicationAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var application = await FindApplicationAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (!application.IsSuccess)
        {
            return application;
        }

        if (application.Value!.IsDisabled)
        {
            return Result<LoadedApplication>.Failure(DisabledFailure<LoadedApplication>().Error!);
        }

        return application;
    }

    private async Task<OpenIddictApplicationDescriptor> PopulateDescriptorAsync(
        object application,
        CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application, cancellationToken)
            .ConfigureAwait(false);
        return descriptor;
    }

    private static IReadOnlyList<string> NormalizeUris(IReadOnlyList<string> uris)
    {
        return uris
            .Where(uri => !string.IsNullOrWhiteSpace(uri))
            .Select(uri => uri.Trim())
            .Where(IdentityOidcRedirectUriPolicy.IsExactAbsoluteUri)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(uri => uri, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeScopes(IReadOnlyList<string> scopes) =>
        scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static Result<string?> NormalizeResourceAudience(string? resourceAudience)
    {
        if (string.IsNullOrWhiteSpace(resourceAudience))
        {
            return Result<string?>.Success(null);
        }

        var normalized = resourceAudience.Trim();
        if (normalized.Length > MaxResourceAudienceLength)
        {
            return Result<string?>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Resource audience exceeds the allowed limit.",
                ErrorType.Validation));
        }

        return Result<string?>.Success(normalized);
    }

    private static Result<ValidatedCreateRequest> ValidationCreateFailure(string message) =>
        Result<ValidatedCreateRequest>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<ValidatedUpdateRequest> ValidationUpdateFailure(string message) =>
        Result<ValidatedUpdateRequest>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<OidcClientResponse> NotFound() =>
        Result<OidcClientResponse>.Failure(new Error(
            IdentityErrorCodes.OidcClientNotFound,
            "The OIDC client was not found.",
            ErrorType.NotFound));

    private static Result<T> DisabledFailure<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.OidcClientDisabled,
            "The OIDC client is disabled.",
            ErrorType.Conflict));

    private static Result<OidcClientResponse> VersionConflict() =>
        Result<OidcClientResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "The OIDC client was modified by another request.",
            ErrorType.Conflict));

    internal sealed record ValidatedCreateRequest(
        string ClientId,
        string DisplayName,
        IReadOnlyList<string> RedirectUris,
        IReadOnlyList<string> PostLogoutRedirectUris,
        IReadOnlyList<string> Scopes,
        string? ResourceAudience);

    internal sealed record ValidatedUpdateRequest(
        string DisplayName,
        IReadOnlyList<string> RedirectUris,
        IReadOnlyList<string> PostLogoutRedirectUris,
        IReadOnlyList<string> Scopes,
        string? ResourceAudience);

    private sealed record LoadedApplication(
        object Entity,
        string ClientType,
        bool IsFirstParty,
        string? ResourceAudience,
        bool IsDisabled,
        int Version);
}