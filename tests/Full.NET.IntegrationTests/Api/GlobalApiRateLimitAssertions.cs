using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Abstractions.Results;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 校验 Host 全局限流在超限时返回标准 ProblemDetails 429。
/// </summary>
internal static class GlobalApiRateLimitAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        var accessToken = await factory.CreateHostAccessTokenAsync(
            ["platform.dashboard.read"],
            cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowedResponse = await client.GetAsync(
                "/api/v1/me",
                cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, allowedResponse.StatusCode);
        }

        using var rejectedResponse = await client.GetAsync(
            "/api/v1/me",
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
        Assert.AreEqual(
            "application/problem+json",
            rejectedResponse.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(
            await rejectedResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            CommonErrorCodes.RateLimited,
            problem.RootElement.GetProperty("code").GetString());
    }
}
