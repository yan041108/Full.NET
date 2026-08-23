using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验 Host 文档回收站端点的 OpenAPI 路径、响应与核心 schema。</summary>
internal static class OpenApiDocumentHostRecycleBinContractAssertions
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
            paths.GetProperty("/api/v1/document/host/recycle-bin"),
            "get",
            ["200", "401", "403"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/recycle-bin/{id}/restore"),
            "post",
            ["200", "400", "401", "403", "404", "409"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/recycle-bin/{id}/purge"),
            "post",
            ["200", "401", "403", "404"]);
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string tag = "DocumentHostRecycleBin";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/recycle-bin",
            HttpMethod.Get,
            "documentHostListRecycleBinItems",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/recycle-bin/{id}/restore",
            HttpMethod.Post,
            "documentHostRestoreRecycleBinItem",
            tag,
            200,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/recycle-bin/{id}/purge",
            HttpMethod.Post,
            "documentHostPurgeRecycleBinItem",
            tag,
            200,
            "application/json");
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
}
