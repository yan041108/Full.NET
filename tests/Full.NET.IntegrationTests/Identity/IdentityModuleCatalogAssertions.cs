using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>Host 只读模块清单验收夹具。</summary>
internal static class IdentityModuleCatalogAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyListRequiresReadPermissionAsync(factory, client, cancellationToken);
        await VerifyListAndDetailAsync(client, cancellationToken);
        await OpenApiIdentityModuleCatalogContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyListRequiresReadPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/modules");
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

    private static async Task VerifyListAndDetailAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var adminToken = await LoginAsHostAdminAsync(client, cancellationToken);

        using var listRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/modules");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var listResponse = await client.SendAsync(listRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
        var entries = await listResponse.Content
            .ReadFromJsonAsync<ModuleCatalogEntryResponse[]>(cancellationToken);
        Assert.IsNotNull(entries);
        Assert.IsGreaterThanOrEqualTo(10, entries.Length);
        Assert.IsTrue(entries.Any(item => item.ModuleKey == "Identity"));
        Assert.IsFalse(entries.Any(item =>
            item.ModuleKey.Contains('/')
            || item.DisplayName.Contains('\\')
            || item.Version.Contains("CSharpCompilation", StringComparison.Ordinal)));

        using var detailRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/modules/Identity");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var detailResponse = await client.SendAsync(detailRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content
            .ReadFromJsonAsync<ModuleCatalogEntryResponse>(cancellationToken);
        Assert.IsNotNull(detail);
        Assert.AreEqual("Identity", detail.ModuleKey);
        Assert.AreEqual("Official", detail.SourceClassification);
        CollectionAssert.Contains(detail.HostProfiles.ToArray(), "Api");

        using var missingRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/identity/modules/missing-module");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            adminToken);
        using var missingResponse = await client.SendAsync(missingRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        using var problem = JsonDocument.Parse(
            await missingResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            IdentityErrorCodes.ModuleCatalogNotFound,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static async Task<string> LoginAsHostAdminAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(token);
        return token.AccessToken;
    }
}
