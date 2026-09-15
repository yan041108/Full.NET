using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcSessionAssertions
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
        await VerifyTenantMismatchRejectedAsync(factory, cancellationToken);
        await VerifyWrongAudienceRejectedAsync(factory, cancellationToken);
        await VerifyUnknownIssuerRejectedAsync(factory, cancellationToken);
        await VerifyLegacyPrincipalRejectedByOidcValidatorAsync(factory, cancellationToken);
        await VerifySessionLifecycleAsync(factory, cancellationToken);
    }

    private static async Task VerifyTenantMismatchRejectedAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var tenantId = Guid.CreateVersion7();
        var (applicationSessionId, userId) = await SeedApplicationSessionAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var validator = scope.ServiceProvider.GetRequiredService<IdentityOidcAccessSessionValidator>();
        var identityOptions = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var principal = CreateOidcPrincipal(
            applicationSessionId,
            userId,
            "host",
            "host",
            identityOptions.Audience,
            tenantId);

        Assert.IsFalse(await validator.IsValidAsync(principal, cancellationToken));
    }

    private static async Task VerifyWrongAudienceRejectedAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var (applicationSessionId, userId) = await SeedApplicationSessionAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var validator = scope.ServiceProvider.GetRequiredService<IdentityOidcAccessSessionValidator>();
        var principal = CreateOidcPrincipal(
            applicationSessionId,
            userId,
            "host",
            "host",
            "https://evil.example/resources",
            null);

        Assert.IsFalse(await validator.IsValidAsync(principal, cancellationToken));
    }

    private static async Task VerifyUnknownIssuerRejectedAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var (applicationSessionId, userId) = await SeedApplicationSessionAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var validator = scope.ServiceProvider.GetRequiredService<IdentityOidcAccessSessionValidator>();
        var identityOptions = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var principal = CreateOidcPrincipal(
            applicationSessionId,
            userId,
            "host",
            "host",
            identityOptions.Audience,
            null,
            issuer: "https://evil.example/identity");

        Assert.IsFalse(await validator.IsValidAsync(principal, cancellationToken));
    }

    private static async Task VerifyLegacyPrincipalRejectedByOidcValidatorAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var validator = scope.ServiceProvider.GetRequiredService<IdentityOidcAccessSessionValidator>();
        var identityOptions = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString("D")),
                new Claim(IdentityClaimTypes.SessionId, Guid.CreateVersion7().ToString("D")),
                new Claim(IdentityClaimTypes.SecurityStamp, "stamp"),
                new Claim(IdentityClaimTypes.ActorScope, "host"),
                new Claim(IdentityClaimTypes.Scope, "host"),
                new Claim(JwtRegisteredClaimNames.Iss, "https://localhost/identity"),
                new Claim(JwtRegisteredClaimNames.Aud, identityOptions.Audience),
                new Claim(FullNetIdentityClaimTypes.TokenUse, IdentityOidcPrincipalFactory.TokenUseAccess),
            ],
            "integration-test"));

        Assert.IsFalse(await validator.IsValidAsync(principal, cancellationToken));
    }

    private static async Task VerifySessionLifecycleAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        var (applicationSessionId, userId) = await SeedApplicationSessionAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var sessionService = scope.ServiceProvider.GetRequiredService<IdentityOidcSessionService>();
        var validator = scope.ServiceProvider.GetRequiredService<IdentityOidcAccessSessionValidator>();
        var identityOptions = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var principal = CreateOidcPrincipal(
            applicationSessionId,
            userId,
            "host",
            "host",
            identityOptions.Audience,
            null);

        Assert.IsTrue(await validator.IsValidAsync(principal, cancellationToken));
        Assert.IsTrue(await sessionService.RevokeApplicationSessionAsync(applicationSessionId, cancellationToken));
        Assert.IsFalse(await validator.IsValidAsync(principal, cancellationToken));
    }

    private static async Task<(Guid ApplicationSessionId, Guid UserId)> SeedApplicationSessionAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var sessionService = scope.ServiceProvider.GetRequiredService<IdentityOidcSessionService>();
        var queryExecutor = scope.ServiceProvider.GetRequiredService<Full.NET.Data.Abstractions.IQueryExecutor>();
        var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
            IdentitySql.FindUserByScopeAndUsername,
            IdentitySqlParameters.Create(
                ("ScopeKey", "host"),
                ("NormalizedUsername", "ADMIN")),
            cancellationToken);
        Assert.IsNotNull(user);
        var application = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationRow>(
            IdentityOidcSql.FindApplicationByClientId,
            IdentitySqlParameters.Create(("ClientId", "integration-oidc-client")),
            cancellationToken);
        if (application is null)
        {
            var applicationStore = scope.ServiceProvider
                .GetRequiredService<IOpenIddictApplicationStore<IdentityOidcApplication>>();
            var entity = await applicationStore.InstantiateAsync(cancellationToken);
            await applicationStore.SetClientIdAsync(entity, "integration-oidc-client", cancellationToken);
            await applicationStore.SetDisplayNameAsync(entity, "Integration OIDC Client", cancellationToken);
            await applicationStore.CreateAsync(entity, cancellationToken);
            application = await queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationRow>(
                IdentityOidcSql.FindApplicationByClientId,
                IdentitySqlParameters.Create(("ClientId", "integration-oidc-client")),
                cancellationToken);
        }

        Assert.IsNotNull(application);
        var now = DateTimeOffset.UtcNow;
        var center = await sessionService.CreateCenterSessionAsync(
            user!.Id,
            user.SecurityStamp,
            now.AddHours(8),
            cancellationToken);
        var applicationSession = await sessionService.CreateApplicationSessionAsync(
            center.Id,
            application!.Id,
            "integration-oidc-client",
            user.Id,
            "host",
            "host",
            null,
            now.AddHours(1),
            cancellationToken);
        return (applicationSession.Id, user.Id);
    }

    private static ClaimsPrincipal CreateOidcPrincipal(
        Guid applicationSessionId,
        Guid userId,
        string actorScope,
        string effectiveScope,
        string audience,
        Guid? tenantId,
        string issuer = "https://localhost/identity")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
            new(JwtRegisteredClaimNames.Iss, issuer),
            new(JwtRegisteredClaimNames.Aud, audience),
            new(FullNetIdentityClaimTypes.TokenUse, IdentityOidcPrincipalFactory.TokenUseAccess),
            new(FullNetIdentityClaimTypes.ApplicationSessionId, applicationSessionId.ToString("D")),
            new(IdentityClaimTypes.ActorScope, actorScope),
            new(IdentityClaimTypes.Scope, effectiveScope),
        };
        if (tenantId.HasValue)
        {
            claims.Add(new Claim(IdentityClaimTypes.TenantId, tenantId.Value.ToString("D")));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "integration-test"));
    }

    internal static IReadOnlyDictionary<string, string?> Settings => OidcSettings;
}
