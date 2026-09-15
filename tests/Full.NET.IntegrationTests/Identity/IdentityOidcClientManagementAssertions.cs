using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcClientManagementAssertions
{
    private const string PublicRedirectUri = "https://localhost:5010/signin-oidc-mgmt-public";
    private const string ConfidentialRedirectUri = "https://localhost:5011/signin-oidc-mgmt-confidential";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyCreateDisableRotateAsync(client, cancellationToken);
        await OpenApiIdentityOidcClientsContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/oidc-clients?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken));
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            "authorization.permission_denied",
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task VerifyCreateDisableRotateAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);
        var publicClientId = $"mgmt-public-{Guid.NewGuid():N}"[..24];
        using var createPublicRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc-clients",
            adminToken,
            new CreateOidcClientRequest(
                publicClientId,
                "集成测试公开客户端",
                [PublicRedirectUri],
                [],
                ["openid", "profile"],
                false,
                true,
                null));
        using var createPublicResponse = await client.SendAsync(createPublicRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createPublicResponse.StatusCode);
        var createdPublic = await createPublicResponse.Content
            .ReadFromJsonAsync<CreateOidcClientResponse>(cancellationToken);
        Assert.IsNotNull(createdPublic);
        Assert.IsNull(createdPublic.Secret);
        Assert.AreEqual("public", createdPublic.Client.ClientType);
        Assert.IsFalse(createdPublic.Client.IsDisabled);

        var confidentialClientId = $"mgmt-conf-{Guid.NewGuid():N}"[..24];
        using var createConfidentialRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/identity/oidc-clients",
            adminToken,
            new CreateOidcClientRequest(
                confidentialClientId,
                "集成测试机密客户端",
                [ConfidentialRedirectUri],
                [],
                ["openid", "profile"],
                true,
                false,
                null));
        using var createConfidentialResponse = await client.SendAsync(
            createConfidentialRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createConfidentialResponse.StatusCode);
        var createdConfidential = await createConfidentialResponse.Content
            .ReadFromJsonAsync<CreateOidcClientResponse>(cancellationToken);
        Assert.IsNotNull(createdConfidential);
        Assert.IsFalse(string.IsNullOrWhiteSpace(createdConfidential.Secret));
        Assert.AreEqual("confidential", createdConfidential.Client.ClientType);

        using var rotateRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-clients/{createdConfidential.Client.Id:D}/rotate",
            adminToken,
            new { });
        using var rotateResponse = await client.SendAsync(rotateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, rotateResponse.StatusCode);
        var rotated = await rotateResponse.Content
            .ReadFromJsonAsync<RotateOidcClientSecretResponse>(cancellationToken);
        Assert.IsNotNull(rotated);
        Assert.AreNotEqual(createdConfidential.Secret, rotated.Secret);

        using var disableRequest = CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-clients/{createdPublic.Client.Id:D}/disable",
            adminToken,
            new { });
        using var disableResponse = await client.SendAsync(disableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content
            .ReadFromJsonAsync<OidcClientResponse>(cancellationToken);
        Assert.IsNotNull(disabled);
        Assert.IsTrue(disabled.IsDisabled);

        var (_, challenge) = IdentityOidcRelyingPartyFixture.CreatePkcePair();
        var authorizeUrl = "/connect/authorize"
            + $"?client_id={Uri.EscapeDataString(publicClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(PublicRedirectUri)}"
            + "&response_type=code&scope=openid%20profile"
            + "&state=state&nonce=nonce"
            + $"&code_challenge={Uri.EscapeDataString(challenge)}"
            + "&code_challenge_method=S256";
        using var authorizeResponse = await client.GetAsync(authorizeUrl, cancellationToken);
        Assert.IsTrue(authorizeResponse.Headers.Location is not null);
        StringAssert.Contains(authorizeResponse.Headers.Location!.ToString(), "error=unauthorized_client");
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken) =>
        await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);

    private static HttpRequestMessage CreateBearerJsonRequest<T>(
        HttpMethod method,
        string path,
        string accessToken,
        T body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}