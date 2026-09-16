using System.Net.Http.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcContextSwitchBackgroundBindingAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            IdentityOidcProtocolAssertions.Settings);
        await factory.InitializeAsync(cancellationToken);
        await VerifyBackgroundBindingTracksContextSwitchAsync(factory, cancellationToken);
    }

    private static async Task VerifyBackgroundBindingTracksContextSwitchAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var hostClient = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            hostClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        var hostBinding = CreateOidcApplicationBinding(flow.AccessToken);
        await AssertBindingValidAsync(factory, hostBinding, true, cancellationToken);

        var acmeTenant = await GetAcmeTenantAsync(hostClient, flow.AccessToken, cancellationToken);
        using var switchToTenantRequest = CreateContextSwitchRequest(acmeTenant.Id, flow.AccessToken);
        using var switchToTenantResponse = await hostClient.SendAsync(switchToTenantRequest, cancellationToken);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, switchToTenantResponse.StatusCode);
        var tenantToken = await switchToTenantResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(tenantToken);

        await AssertBindingValidAsync(factory, hostBinding, false, cancellationToken);
        var tenantBinding = CreateOidcApplicationBinding(tenantToken.AccessToken);
        await AssertBindingValidAsync(factory, tenantBinding, true, cancellationToken);

        using var switchToHostRequest = CreateContextSwitchRequest(null, tenantToken.AccessToken);
        using var switchToHostResponse = await hostClient.SendAsync(switchToHostRequest, cancellationToken);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, switchToHostResponse.StatusCode);
        var hostToken = await switchToHostResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(hostToken);

        await AssertBindingValidAsync(factory, tenantBinding, false, cancellationToken);
        var restoredHostBinding = CreateOidcApplicationBinding(hostToken.AccessToken);
        await AssertBindingValidAsync(factory, restoredHostBinding, true, cancellationToken);
    }

    private static async Task AssertBindingValidAsync(
        FullNetApiFactory factory,
        SessionBindingSnapshot binding,
        bool expected,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var validator = scope.ServiceProvider.GetRequiredService<IBackgroundSessionBindingValidator>();
        var actual = await validator.IsValidAsync(binding, cancellationToken);
        Assert.AreEqual(
            expected,
            actual,
            $"Expected background OIDC binding validity={expected} for scope '{binding.EffectiveScope}'.");
    }

    private static SessionBindingSnapshot CreateOidcApplicationBinding(string accessToken)
    {
        var userIdText = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(accessToken, "sub");
        var applicationSessionIdText = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
            accessToken,
            FullNetIdentityClaimTypes.ApplicationSessionId);
        var securityStamp = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
            accessToken,
            FullNetIdentityClaimTypes.SecurityStamp);
        var actorScope = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
            accessToken,
            FullNetIdentityClaimTypes.ActorScope);
        var effectiveScope = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
            accessToken,
            FullNetIdentityClaimTypes.Scope);
        var tenantIdText = IdentityOidcRelyingPartyFixture.ReadJwtPayloadValue(
            accessToken,
            FullNetIdentityClaimTypes.TenantId);
        Assert.IsTrue(Guid.TryParse(userIdText, out var userId));
        Assert.IsTrue(Guid.TryParse(applicationSessionIdText, out var applicationSessionId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(securityStamp));
        Assert.IsFalse(string.IsNullOrWhiteSpace(actorScope));
        Assert.IsFalse(string.IsNullOrWhiteSpace(effectiveScope));
        Guid? tenantId = Guid.TryParse(tenantIdText, out var parsedTenantId) ? parsedTenantId : null;
        return new SessionBindingSnapshot(
            userId,
            tenantId,
            applicationSessionId,
            securityStamp!,
            actorScope!,
            effectiveScope!,
            SessionBindingKinds.OidcApplication);
    }

    private static async Task<TenantContextSummary> GetAcmeTenantAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tenancy/available");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
        var available = await response.Content.ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        return available.Single(tenant => tenant.Identifier == "acme");
    }

    private static HttpRequestMessage CreateContextSwitchRequest(Guid? tenantId, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(tenantId)),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }
}