using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验 Host 文档分享与匿名访问端点的 OpenAPI 路径、响应与核心 schema。</summary>
internal static class OpenApiDocumentHostSharesContractAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        AssertPilotOperations(document.RootElement);
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/shares"),
            "get",
            ["200", "401", "403"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/shares"),
            "post",
            ["201", "400", "401", "403", "404"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/shares/{id}/status"),
            "post",
            ["200", "400", "401", "403", "404", "409"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/public/shares/{shareCode}/access"),
            "post",
            ["200", "400", "401", "403", "404", "409", "429"]);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertSchema(
            schemas,
            "HostDocumentShareResponse",
            [
                "id",
                "documentId",
                "shareCode",
                "createdAtUtc",
                "expireTime",
                "maxAccessCount",
                "accessCount",
                "isEnabled",
                "version",
                "hasPassword",
            ]);
        AssertSchema(
            schemas,
            "HostDocumentShareAccessResponse",
            [
                "shareId",
                "documentId",
                "shareCode",
                "title",
                "fileName",
                "mimeType",
                "fileSizeBytes",
                "hasPassword",
                "accessCountRemaining",
            ]);
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string hostTag = "DocumentHostShares";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/shares",
            HttpMethod.Get,
            "documentHostListDocumentShares",
            hostTag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/shares",
            HttpMethod.Post,
            "documentHostCreateDocumentShare",
            hostTag,
            201,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/shares/{id}/status",
            HttpMethod.Post,
            "documentHostUpdateDocumentShareStatus",
            hostTag,
            200,
            "application/json",
            "application/json");

        const string publicTag = "DocumentPublicShares";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/public/shares/{shareCode}/access",
            HttpMethod.Post,
            "documentPublicAccessDocumentShare",
            publicTag,
            200,
            "application/json",
            "application/json",
            isPublic: true);
    }

    private static void AssertOperation(
        JsonElement path,
        string method,
        IReadOnlyList<string> statuses)
    {
        var responses = path.GetProperty(method).GetProperty("responses");
        foreach (var status in statuses)
        {
            Assert.IsTrue(
                responses.TryGetProperty(status, out _),
                $"{method.ToUpperInvariant()} 缺少响应状态 {status}");
        }
    }

    private static void AssertSchema(
        JsonElement schemas,
        string suffix,
        IReadOnlyList<string> properties)
    {
        var schema = schemas.EnumerateObject()
            .Single(candidate =>
                string.Equals(
                    candidate.Name,
                    suffix,
                    StringComparison.Ordinal)
                || candidate.Name.EndsWith(
                    "." + suffix,
                    StringComparison.Ordinal))
            .Value;
        var actual = schema.GetProperty("properties");
        foreach (var property in properties)
        {
            Assert.IsTrue(
                actual.TryGetProperty(property, out _),
                $"{suffix} 缺少属性 {property}");
        }
    }
}
