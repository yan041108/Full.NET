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
        string? origin = AllowedOrigin)
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
        await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);

        using (var centerLogoutRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/logout",
            origin: "https://evil.example"))
        using (var centerLogoutResponse = await client.SendAsync(centerLogoutRequest, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, centerLogoutResponse.StatusCode);
            var body = await centerLogoutResponse.Content.ReadAsStringAsync(cancellationToken);
            using var problem = JsonDocument.Parse(body);
            Assert.AreEqual(
                "identity.origin_not_allowed",
                problem.RootElement.GetProperty("code").GetString());
            IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(
                body,
                "OIDC center logout origin rejection");
        }

        using (var applicationLogoutRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/logout/application",
            new { clientId = IdentityOidcRelyingPartyFixture.PublicClientId },
            origin: "https://evil.example"))
        using (var applicationLogoutResponse = await client.SendAsync(
            applicationLogoutRequest,
            cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, applicationLogoutResponse.StatusCode);
            using var problem = JsonDocument.Parse(
                await applicationLogoutResponse.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                "identity.origin_not_allowed",
                problem.RootElement.GetProperty("code").GetString());
        }

        using var validLogoutRequest = IdentityOidcSessionTestSupport.CreateSessionWriteRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc/logout");
        using var validLogoutResponse = await client.SendAsync(validLogoutRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NoContent, validLogoutResponse.StatusCode);
    }
}