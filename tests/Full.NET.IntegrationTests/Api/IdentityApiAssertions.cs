using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.IntegrationTests.Api;

internal static class IdentityApiAssertions
{
    public static async Task VerifyLoginAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        using var invalidRequest = CreateLoginRequest("wrong-password");
        using var invalidResponse = await client.SendAsync(
            invalidRequest,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
        using (var problem = JsonDocument.Parse(
            await invalidResponse.Content.ReadAsStringAsync(cancellationToken)))
        {
            Assert.AreEqual(
                "identity.invalid_credentials",
                problem.RootElement.GetProperty("code").GetString());
        }

        using var loginRequest = CreateLoginRequest(FullNetApiFactory.TestPassword);
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var json = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
        var token = JsonSerializer.Deserialize<TokenResponse>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.IsNotNull(token);
        Assert.AreEqual("Bearer", token.TokenType);
        Assert.IsFalse(string.IsNullOrWhiteSpace(token.AccessToken));
        using (var tokenDocument = JsonDocument.Parse(json))
        {
            Assert.IsFalse(tokenDocument.RootElement.TryGetProperty("refreshToken", out _));
            Assert.IsFalse(tokenDocument.RootElement.TryGetProperty("csrfToken", out _));
        }

        var cookies = loginResponse.Headers.GetValues("Set-Cookie").ToArray();
        AssertCookie(cookies, "__Host-fullnet-refresh", httpOnly: true);
        AssertCookie(cookies, "fullnet-csrf", httpOnly: false);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.AccessToken);
        using var meResponse = await client.SendAsync(meRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, meResponse.StatusCode);
        var currentUser = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(
            cancellationToken);
        Assert.IsNotNull(currentUser);
        Assert.AreEqual("admin", currentUser.Username);
        Assert.AreEqual("系统管理员", currentUser.DisplayName);
        Assert.AreEqual("host", currentUser.Scope);
        Assert.IsNull(currentUser.TenantId);

        Assert.IsGreaterThanOrEqualTo(
            2L,
            await factory.GetAuthenticationAuditCountAsync(cancellationToken));
    }

    private static HttpRequestMessage CreateLoginRequest(string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = "admin",
                password,
            }),
        };
        request.Headers.Add("Origin", "http://localhost");
        return request;
    }

    private static void AssertCookie(
        IEnumerable<string> cookies,
        string name,
        bool httpOnly)
    {
        var cookie = cookies.Single(value => value.StartsWith(
            $"{name}=",
            StringComparison.Ordinal));
        StringAssert.Contains(cookie, "path=/", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(cookie, "secure", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(cookie, "samesite=strict", StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(
            httpOnly,
            cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }
}
