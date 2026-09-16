using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcMultiInstanceRevokeAllAssertions
{
    public static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        using var primaryFactory = new FullNetApiFactory(
            provider,
            connectionString,
            IdentityOidcProtocolAssertions.Settings);
        using var secondaryFactory = primaryFactory.CreateIsolatedFactory();
        await primaryFactory.InitializeAsync(cancellationToken);
        await secondaryFactory.InitializeAsync(cancellationToken);

        using var primaryClient = primaryFactory.CreateClientForHost("localhost");
        using var secondaryClient = secondaryFactory.CreateClientForHost("localhost");
        var flow = await IdentityOidcRelyingPartyFixture.RunAuthorizationCodeFlowAsync(
            primaryClient,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            IdentityOidcRelyingPartyFixture.PublicRedirectUri,
            null,
            "admin",
            FullNetApiFactory.TestPassword,
            requestOfflineAccess: true,
            cancellationToken: cancellationToken);
        Assert.IsFalse(string.IsNullOrWhiteSpace(flow.RefreshToken));

        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(
            primaryClient,
            "admin",
            FullNetApiFactory.TestPassword,
            cancellationToken);
        var adminUserId = await ResolveAdminUserIdAsync(primaryClient, adminToken, cancellationToken);

        using var revokeAllRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/identity/online-sessions/users/{adminUserId:D}/revoke-all")
        {
            Content = JsonContent.Create(new { }),
        };
        revokeAllRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var revokeAllResponse = await primaryClient.SendAsync(revokeAllRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revokeAllResponse.StatusCode);

        using var mePrimaryRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        mePrimaryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var mePrimaryResponse = await primaryClient.SendAsync(mePrimaryRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mePrimaryResponse.StatusCode);

        using var meSecondaryRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meSecondaryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", flow.AccessToken);
        using var meSecondaryResponse = await secondaryClient.SendAsync(meSecondaryRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, meSecondaryResponse.StatusCode);

        var refreshResult = await IdentityOidcRelyingPartyFixture.ExchangeRefreshTokenAsync(
            secondaryClient,
            flow.RefreshToken!,
            IdentityOidcRelyingPartyFixture.PublicClientId,
            null,
            cancellationToken);
        Assert.IsFalse(refreshResult.IsSuccessStatusCode);
        StringAssert.Contains(refreshResult.RawBody, "invalid_grant");
    }

    private static async Task<Guid> ResolveAdminUserIdAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/users?page=1&pageSize=50");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<HostUserResponse>>(cancellationToken);
        Assert.IsNotNull(page);
        return page!.Items.Single(item => item.Username == "admin").Id;
    }
}