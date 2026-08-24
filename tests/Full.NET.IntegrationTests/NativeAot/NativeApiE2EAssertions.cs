using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// Native 原生产物上的最小关键 HTTP 链路断言。
/// </summary>
internal static class NativeApiE2EAssertions
{
    public const string AdminPassword = "FullNet!2026Integration";

    public static async Task VerifyCriticalHttpFlowAsync(
        DatabaseProvider provider,
        string connectionString,
        IReadOnlyDictionary<string, string?>? settingsOverrides = null,
        CancellationToken cancellationToken = default)
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);

        await using var host = await NativeApiProcessHost.StartAsync(
            NativeApiArtifactLocator.RequireArtifact(),
            provider,
            connectionString,
            settingsOverrides ?? new Dictionary<string, string?>(),
            TimeSpan.FromMinutes(2),
            cancellationToken).ConfigureAwait(false);

        using var client = host.CreateClient();
        var token = await LoginAsync(client, cancellationToken).ConfigureAwait(false);
        await VerifyAuthenticatedMeAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyTenancyReadAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyOrganizationReadAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyCodeGenerationCatalogReadAsync(client, token, cancellationToken)
            .ConfigureAwait(false);
        await VerifyReadinessAsync(client, cancellationToken).ConfigureAwait(false);
        await host.StopGracefullyAsync(cancellationToken).ConfigureAwait(false);
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task<string> LoginAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", AdminPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken)
            .ConfigureAwait(false);
        Assert.IsNotNull(token);
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.AccessToken));
        return token.AccessToken;
    }

    private static async Task VerifyAuthenticatedMeAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
        Assert.AreEqual("admin", payload.RootElement.GetProperty("username").GetString());
    }

    private static async Task VerifyTenancyReadAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/tenants?page=1&pageSize=1");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task VerifyOrganizationReadAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/organization/units?page=1&pageSize=1");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task VerifyCodeGenerationCatalogReadAsync(
        HttpClient client,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/code-generation/catalog/tables");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false));
        Assert.IsTrue(payload.RootElement.ValueKind == JsonValueKind.Array);
    }

    private static async Task VerifyReadinessAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/health/ready", cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
