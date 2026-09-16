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
        await VerifyProtectedApiRejectsExternalTokenAsync(
            client,
            flow.AccessToken,
            "/api/v1/identity/online-sessions?page=1&pageSize=1",
            cancellationToken);

        await VerifyFirstPartyNonPrivilegedUserBoundaryAsync(factory, client, cancellationToken);
    }

    private static async Task VerifyFirstPartyNonPrivilegedUserBoundaryAsync(
        FullNetApiFactory factory,
        HttpClient adminClient,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            adminClient,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var username = $"oidc-api-boundary-{Guid.NewGuid():N}";
        var password = FullNetApiFactory.TestPassword;
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/users")
        {
            Content = JsonContent.Create(new CreateHostUserRequest(
                username,
                "OIDC API permission boundary user",
                password)),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var createResponse = await adminClient.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);

        using var userClient = factory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            userClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            username,
            password,
            requestOfflineAccess: false,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.AccessToken));

        await VerifyProtectedApiRejectsTokenAsync(
            userClient,
            flow.AccessToken,
            "/api/v1/identity/oidc-authorizations?page=1&pageSize=1",
            "First-party non-privileged user",
            cancellationToken);
        await VerifyProtectedApiRejectsTokenAsync(
            userClient,
            flow.AccessToken,
            "/api/v1/identity/oidc-clients?page=1&pageSize=1",
            "First-party non-privileged user",
            cancellationToken);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var meResponse = await userClient.SendAsync(meRequest, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.OK,
            meResponse.StatusCode,
            "First-party users without admin permissions must still access their own profile.");
    }

    private static Task VerifyProtectedApiRejectsExternalTokenAsync(
        HttpClient client,
        string accessToken,
        string path,
        CancellationToken cancellationToken) =>
        VerifyProtectedApiRejectsTokenAsync(
            client,
            accessToken,
            path,
            "External client",
            cancellationToken);

    private static async Task VerifyProtectedApiRejectsTokenAsync(
        HttpClient client,
        string accessToken,
        string path,
        string scenario,
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
            $"{scenario} request to {path}");
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