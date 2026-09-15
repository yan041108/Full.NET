using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcApiPermissionBoundaryAssertions
{
    private const string ExternalRedirectUri = "https://localhost:5013/signin-oidc-api-boundary";

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
        using var client = factory.CreateClientForHost("localhost");

        var externalClientId = $"api-ext-{Guid.NewGuid():N}"[..24];
        await CreateExternalClientAsync(client, externalClientId, cancellationToken);
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            externalClientId,
            ExternalRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.AccessToken));

        await VerifyProtectedApiRejectsExternalTokenAsync(
            client,
            flow.AccessToken,
            "/api/v1/identity/users?page=1&pageSize=1",
            cancellationToken);
        await VerifyProtectedApiRejectsExternalTokenAsync(
            client,
            flow.AccessToken,
            "/api/v1/identity/roles?page=1&pageSize=1",
            cancellationToken);
    }

    private static async Task VerifyProtectedApiRejectsExternalTokenAsync(
        HttpClient client,
        string accessToken,
        string path,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.IsTrue(body.Contains("type", StringComparison.OrdinalIgnoreCase));
        using var problem = JsonDocument.Parse(body);
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
        IdentityOidcErrorResponseAssertions.AssertDoesNotLeakInternalDetails(
            body,
            $"External client request to {path}");
    }

    private static async Task CreateExternalClientAsync(
        HttpClient client,
        string clientId,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/oidc-clients")
        {
            Content = JsonContent.Create(new CreateOidcClientRequest(
                clientId,
                "API permission boundary external client",
                [ExternalRedirectUri],
                [],
                ["openid", "profile"],
                false,
                false,
                null)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
    }
}