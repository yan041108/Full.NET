using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictExceptions;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcStoreAssertions
{
    private static readonly IReadOnlyDictionary<string, string?> OidcSettings = new Dictionary<string, string?>
    {
        ["Identity:Oidc:Enable"] = "true",
        ["Identity:Oidc:Issuer"] = "https://localhost/identity",
        ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "true",
        ["Identity:Oidc:Clients:0:ClientId"] = "integration-oidc-client",
        ["Identity:Oidc:Clients:0:RedirectUris:0"] = "https://localhost/signin-oidc",
    };

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        await VerifyParallelAuthorizationCodeRedeemAsync(factory, cancellationToken);
        await VerifyRevokedAuthorizationBlocksRedeemAsync(factory, cancellationToken);
    }

    private static async Task VerifyParallelAuthorizationCodeRedeemAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var (tokenId, _) = await SeedAuthorizationCodeTokenAsync(factory, cancellationToken);
        var attempts = Enumerable.Range(0, 8)
            .Select(_ => TryRedeemAuthorizationCodeAsync(factory, tokenId, cancellationToken))
            .ToArray();
        var results = await Task.WhenAll(attempts);
        Assert.AreEqual(1, results.Count(result => result));
    }

    private static async Task VerifyRevokedAuthorizationBlocksRedeemAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var (tokenId, applicationId) = await SeedAuthorizationCodeTokenAsync(factory, cancellationToken);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var authorizationStore = scope.ServiceProvider
                .GetRequiredService<IOpenIddictAuthorizationStore<IdentityOidcAuthorization>>();
            await authorizationStore.RevokeByApplicationIdAsync(applicationId, cancellationToken);
        }

        Assert.IsFalse(await TryRedeemAuthorizationCodeAsync(factory, tokenId, cancellationToken));

        await using var freshScope = factory.Services.CreateAsyncScope();
        freshScope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var freshTokenStore = freshScope.ServiceProvider
            .GetRequiredService<IOpenIddictTokenStore<IdentityOidcToken>>();
        var freshToken = await freshTokenStore.FindByIdAsync(tokenId, cancellationToken);
        Assert.IsNotNull(freshToken);
        Assert.IsNull(await freshTokenStore.GetRedemptionDateAsync(freshToken, cancellationToken));
    }

    private static async Task<(string TokenId, string ApplicationId)> SeedAuthorizationCodeTokenAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var clientId = $"integration-oidc-{Guid.CreateVersion7():N}";
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var applicationStore = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationStore<IdentityOidcApplication>>();
        var authorizationStore = scope.ServiceProvider
            .GetRequiredService<IOpenIddictAuthorizationStore<IdentityOidcAuthorization>>();
        var tokenStore = scope.ServiceProvider
            .GetRequiredService<IOpenIddictTokenStore<IdentityOidcToken>>();

        var application = await applicationStore.InstantiateAsync(cancellationToken);
        await applicationStore.SetClientIdAsync(application, clientId, cancellationToken);
        await applicationStore.CreateAsync(application, cancellationToken);
        var applicationId = await applicationStore.GetIdAsync(application, cancellationToken);
        Assert.IsNotNull(applicationId);

        var authorization = await authorizationStore.InstantiateAsync(cancellationToken);
        await authorizationStore.SetApplicationIdAsync(authorization, applicationId, cancellationToken);
        await authorizationStore.SetSubjectAsync(authorization, "integration-user", cancellationToken);
        await authorizationStore.SetStatusAsync(authorization, OpenIddictConstants.Statuses.Valid, cancellationToken);
        await authorizationStore.SetTypeAsync(authorization, OpenIddictConstants.AuthorizationTypes.Permanent, cancellationToken);
        await authorizationStore.CreateAsync(authorization, cancellationToken);
        var authorizationId = await authorizationStore.GetIdAsync(authorization, cancellationToken);
        Assert.IsNotNull(authorizationId);

        var token = await tokenStore.InstantiateAsync(cancellationToken);
        await tokenStore.SetApplicationIdAsync(token, applicationId, cancellationToken);
        await tokenStore.SetAuthorizationIdAsync(token, authorizationId, cancellationToken);
        await tokenStore.SetSubjectAsync(token, "integration-user", cancellationToken);
        await tokenStore.SetStatusAsync(token, OpenIddictConstants.Statuses.Valid, cancellationToken);
        await tokenStore.SetTypeAsync(token, OpenIddictConstants.TokenTypeIdentifiers.Private.AuthorizationCode, cancellationToken);
        await tokenStore.SetReferenceIdAsync(token, Guid.NewGuid().ToString("N"), cancellationToken);
        await tokenStore.CreateAsync(token, cancellationToken);
        var tokenId = await tokenStore.GetIdAsync(token, cancellationToken);
        Assert.IsNotNull(tokenId);
        return (tokenId, applicationId!);
    }

    private static async Task<bool> TryRedeemAuthorizationCodeAsync(
        FullNetApiFactory factory,
        string tokenId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var tokenStore = scope.ServiceProvider
            .GetRequiredService<IOpenIddictTokenStore<IdentityOidcToken>>();
        var token = await tokenStore.FindByIdAsync(tokenId, cancellationToken);
        if (token is null)
        {
            return false;
        }

        try
        {
            await tokenStore.SetRedemptionDateAsync(token, DateTimeOffset.UtcNow, cancellationToken);
            await tokenStore.UpdateAsync(token, cancellationToken);
            return true;
        }
        catch (ConcurrencyException)
        {
            return false;
        }
    }
}