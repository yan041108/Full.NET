using System.Net;
using System.Net.Http.Headers;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcSessionStateFaultAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        var faultGate = new SessionValidationFaultGate();
        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            IdentityOidcProtocolAssertions.Settings,
            configureTestServices: services =>
            {
                services.AddSingleton(faultGate);
                DecorateQueryExecutor(services);
            });
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        using var healthyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        healthyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var healthyResponse = await client.SendAsync(healthyRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, healthyResponse.StatusCode);

        var binding = CreateOidcApplicationBinding(flow.AccessToken);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var validator = scope.ServiceProvider.GetRequiredService<IBackgroundSessionBindingValidator>();
            Assert.IsTrue(
                await validator.IsValidAsync(binding, cancellationToken),
                "Background OIDC session binding must validate while session state is healthy.");
        }

        faultGate.SimulateOutage = true;
        using var faultedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        faultedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var faultedResponse = await client.SendAsync(faultedRequest, cancellationToken);
        Assert.IsTrue(
            (int)faultedResponse.StatusCode is >= 400 and < 500,
            $"Session state outage must fail closed, got {(int)faultedResponse.StatusCode}.");
        Assert.AreEqual(
            HttpStatusCode.Unauthorized,
            faultedResponse.StatusCode,
            "Session state outage must reject protected API access.");
        var faultedBody = await faultedResponse.Content.ReadAsStringAsync(cancellationToken);
        IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(
            faultedBody,
            "Protected API rejection during session state outage");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var validator = scope.ServiceProvider.GetRequiredService<IBackgroundSessionBindingValidator>();
            Assert.IsFalse(
                await validator.IsValidAsync(binding, cancellationToken),
                "Background OIDC session binding must fail closed during session state outage.");
        }
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
        Assert.IsTrue(Guid.TryParse(userIdText, out var userId));
        Assert.IsTrue(Guid.TryParse(applicationSessionIdText, out var applicationSessionId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(securityStamp));
        Assert.IsFalse(string.IsNullOrWhiteSpace(actorScope));
        Assert.IsFalse(string.IsNullOrWhiteSpace(effectiveScope));
        return new SessionBindingSnapshot(
            userId,
            null,
            applicationSessionId,
            securityStamp!,
            actorScope!,
            effectiveScope!,
            SessionBindingKinds.OidcApplication);
    }

    private static void DecorateQueryExecutor(IServiceCollection services)
    {
        var descriptor = services.LastOrDefault(item => item.ServiceType == typeof(IQueryExecutor))
            ?? throw new InvalidOperationException("Integration host is missing IQueryExecutor registration.");
        services.Remove(descriptor);
        services.AddScoped<IQueryExecutor>(provider =>
            new FaultInjectingQueryExecutor(
                CreateOriginalQueryExecutor(provider, descriptor),
                provider.GetRequiredService<SessionValidationFaultGate>()));
    }

    private static IQueryExecutor CreateOriginalQueryExecutor(
        IServiceProvider provider,
        ServiceDescriptor descriptor)
    {
        var service = descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? (descriptor.ImplementationType is { } implementationType
                ? ActivatorUtilities.CreateInstance(provider, implementationType)
                : null);
        return service as IQueryExecutor
            ?? throw new InvalidOperationException("Unable to create the original IQueryExecutor.");
    }

    private sealed class SessionValidationFaultGate
    {
        public bool SimulateOutage { get; set; }
    }

    private sealed class FaultInjectingQueryExecutor(IQueryExecutor inner, SessionValidationFaultGate faultGate)
        : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (faultGate.SimulateOutage
                && string.Equals(
                    statement.Name,
                    IdentityOidcSessionSql.FindApplicationSessionValidationById.Name,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Simulated session state outage.");
            }

            return inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }
}