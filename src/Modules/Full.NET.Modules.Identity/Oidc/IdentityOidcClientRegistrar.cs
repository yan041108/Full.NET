using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>启动时将配置中的固定 OIDC 客户端同步到 OpenIddict 应用存储。</summary>
internal sealed class IdentityOidcClientRegistrar(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityOidcOptions> options,
    ILogger<IdentityOidcClientRegistrar> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enable)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var applicationManager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider
            .GetRequiredService<IOpenIddictScopeManager>();
        await EnsureScopesAsync(scopeManager, cancellationToken).ConfigureAwait(false);
        foreach (var client in options.Value.Clients)
        {
            await SyncClientAsync(applicationManager, client, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureScopesAsync(
        IOpenIddictScopeManager scopeManager,
        CancellationToken cancellationToken)
    {
        foreach (var scopeName in new[] { Scopes.OpenId, Scopes.Profile, Scopes.OfflineAccess })
        {
            if (await scopeManager.FindByNameAsync(scopeName, cancellationToken)
                    .ConfigureAwait(false) is not null)
            {
                continue;
            }

            var descriptor = new OpenIddictScopeDescriptor
            {
                Name = scopeName,
                DisplayName = scopeName,
            };
            await scopeManager.CreateAsync(descriptor, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task SyncClientAsync(
        IOpenIddictApplicationManager applicationManager,
        IdentityOidcClientOptions client,
        CancellationToken cancellationToken)
    {
        var existing = await applicationManager.FindByClientIdAsync(
                client.ClientId,
                cancellationToken)
            .ConfigureAwait(false);
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            DisplayName = client.ClientId,
            ClientType = string.IsNullOrWhiteSpace(client.ClientSecret)
                ? ClientTypes.Public
                : ClientTypes.Confidential,
        };
        if (!string.IsNullOrWhiteSpace(client.ClientSecret))
        {
            descriptor.ClientSecret = client.ClientSecret;
        }

        foreach (var redirectUri in client.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(redirectUri, UriKind.Absolute));
        }

        foreach (var postLogoutRedirectUri in client.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(
                new Uri(postLogoutRedirectUri, UriKind.Absolute));
        }

        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
        descriptor.Permissions.Add(Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
        var allowedScopes = new HashSet<string>(
            client.Scopes.Length > 0 ? client.Scopes : ["openid", "profile"],
            StringComparer.OrdinalIgnoreCase);
        allowedScopes.Add(Scopes.OpenId);
        allowedScopes.Add(Scopes.Profile);
        // 服务端已注册 offline_access；同步授予 scope 与 refresh_token 权限，是否签发仍由授权请求 scope 决定。
        allowedScopes.Add(Scopes.OfflineAccess);
        foreach (var scope in allowedScopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        descriptor.Permissions.Add(Permissions.GrantTypes.RefreshToken);

        if (existing is null)
        {
            await applicationManager.CreateAsync(descriptor, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation("Registered OIDC client {ClientId}.", client.ClientId);
            return;
        }

        await applicationManager.UpdateAsync(existing, descriptor, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Updated OIDC client {ClientId}.", client.ClientId);
    }
}