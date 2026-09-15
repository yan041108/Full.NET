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
        var descriptor = IdentityOidcClientDescriptorFactory.BuildFromOptions(client);

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