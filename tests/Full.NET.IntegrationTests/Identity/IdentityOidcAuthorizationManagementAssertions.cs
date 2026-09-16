using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcAuthorizationManagementAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyListGetRevokeFlowAsync(client, cancellationToken);
        await OpenApiIdentityOidcAuthorizationsContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/oidc-authorizations?page=1&pageSize=20");
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

    private static async Task VerifyListGetRevokeFlowAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            client,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            client,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        var listUrl = "/api/v1/identity/oidc-authorizations"
            + $"?page=1&pageSize=20&clientIdContains={Uri.EscapeDataString(IdentityOidcRelyingPartyFixture.PublicClientId)}"
            + $"&status={Uri.EscapeDataString(Statuses.Valid)}";
        using var listRequest = CreateBearerRequest(HttpMethod.Get, listUrl, adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResult<OidcAuthorizationResponse>>(
            cancellationToken);
        Assert.IsNotNull(page);
        Assert.IsTrue(page!.Items.Count > 0, "Expected at least one OIDC authorization grant.");
        var authorization = page.Items.First(item =>
            string.Equals(item.ClientId, IdentityOidcRelyingPartyFixture.PublicClientId, StringComparison.Ordinal));
        Assert.AreEqual(Statuses.Valid, authorization.Status);
        Assert.IsTrue(authorization.Scopes.Contains("offline_access"));

        using var getRequest = CreateBearerRequest(
            HttpMethod.Get,
            $"/api/v1/identity/oidc-authorizations/{authorization.Id:D}",
            adminToken);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        var detail = await getResponse.Content.ReadFromJsonAsync<OidcAuthorizationResponse>(cancellationToken);
        Assert.IsNotNull(detail);
        Assert.AreEqual(authorization.Id, detail!.Id);
        Assert.AreEqual(authorization.ClientId, detail.ClientId);

        using var revokeRequest = CreateBearerRequest(
            HttpMethod.Post,
            $"/api/v1/identity/oidc-authorizations/{authorization.Id:D}/revoke",
            adminToken);
        using var revokeResponse = await client.SendAsync(revokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeResponse.StatusCode);
        var revoked = await revokeResponse.Content.ReadFromJsonAsync<OidcAuthorizationResponse>(cancellationToken);
        Assert.IsNotNull(revoked);
        Assert.AreEqual(Statuses.Revoked, revoked!.Status);

        using var meAfterRevokeRequest = CreateBearerRequest(
            HttpMethod.Get,
            "/api/v1/me",
            flow.AccessToken);
        using var meAfterRevokeResponse = await client.SendAsync(meAfterRevokeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, meAfterRevokeResponse.StatusCode);

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            client,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
        StringAssert.Contains(refreshResult.RawBody, "error");
    }

    private static HttpRequestMessage CreateBearerRequest(
        HttpMethod method,
        string path,
        string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}