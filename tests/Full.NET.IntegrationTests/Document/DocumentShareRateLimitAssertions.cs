using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.IntegrationTests.Document;

/// <summary>
/// 校验匿名分享访问端点在超限时返回标准 429 ProblemDetails。
/// </summary>
internal static class DocumentShareRateLimitAssertions
{
    private const string PublicAccessPath = "/api/v1/document/public/shares";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowedResponse = await PostAccessAsync(
                client,
                $"rate-limit-{attempt}",
                cancellationToken);
            Assert.AreNotEqual(
                HttpStatusCode.TooManyRequests,
                allowedResponse.StatusCode,
                "前两次请求不应被 Document 匿名分享限流拒绝。");
        }

        using var rejectedResponse = await PostAccessAsync(
            client,
            "rate-limit-overflow",
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

    private static Task<HttpResponseMessage> PostAccessAsync(
        HttpClient client,
        string shareCode,
        CancellationToken cancellationToken)
    {
        return client.PostAsJsonAsync(
            $"{PublicAccessPath}/{shareCode}/access",
            new AccessHostDocumentShareRequest(null),
            cancellationToken);
    }
}
