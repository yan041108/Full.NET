using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验 OpenAPI 文档暴露 Signature 安全方案。</summary>
internal static class OpenApiSignatureSecuritySchemeAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var schemes = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");
        Assert.IsTrue(schemes.TryGetProperty("Signature", out var signature));
        Assert.AreEqual("apiKey", signature.GetProperty("type").GetString());
        Assert.AreEqual(
            "X-FullNET-Access-Key-Id",
            signature.GetProperty("name").GetString());
    }
}
