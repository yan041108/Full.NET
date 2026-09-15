using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcSessionTestSupport
{
    public const string AllowedOrigin = "http://localhost";

    public static HttpRequestMessage CreateSessionWriteRequest(
        HttpMethod method,
        string path,
        object? jsonBody = null,
        string? origin = AllowedOrigin,
        string? referer = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody);
        }

        if (!string.IsNullOrWhiteSpace(origin))
        {
            request.Headers.Add("Origin", origin);
        }

        if (!string.IsNullOrWhiteSpace(referer))
        {
            request.Headers.Add("Referer", referer);
        }

        return request;
    }
}

internal static class IdentityOidcSessionSecurityAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await VerifyCenterLoginOriginBoundaryAsync(client, cancellationToken);
        await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);

        await AssertOriginNotAllowedAsync(
            client,
            IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
                HttpMethod.Post,
                "/api/v1/identity/oidc/logout",
                origin: null),
            "OIDC center logout without origin",
            cancellationToken);
        await AssertOriginNotAllowedAsync(
            client,
            IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
                HttpMethod.Post,
                "/api/v1/identity/oidc/logout",
                origin: "https://evil.example"),
            "OIDC center logout origin rejection",
            cancellationToken);

        await AssertOriginNotAllowedAsync(
            client,
            IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
                HttpMethod.Post,
                "/api/v1/identity/oidc/logout/application",
                new { clientId = IdentityOidcRelyingPartyFixture.PublicClientId },
                origin: null),
            "OIDC application logout without origin",
            cancellationToken);
        await AssertOriginNotAllowedAsync(
            client,
            IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
                HttpMethod.Post,
                "/api/v1/identity/oidc/logout/application",
                new { clientId = IdentityOidcRelyingPartyFixture.PublicClientId },
                origin: "https://evil.example"),
            "OIDC application logout origin rejection",
            cancellationToken);

        using var validLogoutRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/logout");
        using var validLogoutResponse = await client.SendAsync(validLogoutRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, validLogoutResponse.StatusCode);
    }

    private static async Task VerifyCenterLoginOriginBoundaryAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var loginBody = new
        {
            username = "admin",
            password = FullNetApiFactory.TestPassword,
            returnUrl = "/connect/authorize",
        };
        await AssertOriginNotAllowedAsync(
            client,
            IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
                HttpMethod.Post,
                "/api/v1/identity/oidc/login",
                loginBody,
                origin: null),
            "OIDC center login without origin",
            cancellationToken);
        await AssertOriginNotAllowedAsync(
            client,
            IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
                HttpMethod.Post,
                "/api/v1/identity/oidc/login",
                loginBody,
                origin: null,
                referer: "https://evil.example/login"),
            "OIDC center login untrusted referer",
            cancellationToken);
        await AssertOriginNotAllowedAsync(
            client,
            IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
                HttpMethod.Post,
                "/api/v1/identity/oidc/login",
                loginBody,
                origin: "https://evil.example"),
            "OIDC center login origin rejection",
            cancellationToken);

        using (var invalidLoginRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/login",
            new
            {
                username = "admin",
                password = "wrong-password",
                returnUrl = "/connect/authorize",
            }))
        using (var invalidLoginResponse = await client.SendAsync(invalidLoginRequest, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, invalidLoginResponse.StatusCode);
        }

        using var validLoginRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/login",
            new
            {
                username = "admin",
                password = FullNetApiFactory.TestPassword,
                returnUrl = "/connect/authorize",
            });
        using var validLoginResponse = await client.SendAsync(validLoginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, validLoginResponse.StatusCode);
    }

    private static async Task AssertOriginNotAllowedAsync(
        HttpClient client,
        HttpRequestMessage request,
        string scenario,
        CancellationToken cancellationToken)
    {
        using (request)
        using (var response = await client.SendAsync(request, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var problem = JsonDocument.Parse(body);
            Assert.AreEqual(
                "identity.origin_not_allowed",
                problem.RootElement.GetProperty("code").GetString());
            IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(body, scenario);
        }
    }
}