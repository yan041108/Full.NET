using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验 Host 文档预览转换任务端点的 OpenAPI 路径、响应与核心 schema。</summary>
internal static class OpenApiDocumentHostPreviewTasksContractAssertions
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
            paths.GetProperty("/api/v1/document/host/preview-tasks"),
            "get",
            ["200", "401", "403"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/preview-tasks"),
            "post",
            ["201", "400", "401", "403", "404", "422"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/preview-tasks/{taskId}"),
            "get",
            ["200", "401", "403", "404"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/preview-tasks/{taskId}/content"),
            "get",
            ["200", "401", "403", "404", "422"]);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertSchema(
            schemas,
            "HostDocumentPreviewTaskResponse",
            [
                "id",
                "documentItemId",
                "documentTitle",
                "versionId",
                "sourceFileId",
                "outputFileId",
                "statusKey",
                "providerKey",
                "errorCode",
                "requestedByUserId",
                "createdAtUtc",
                "startedAtUtc",
                "completedAtUtc",
                "version",
            ]);
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string tag = "DocumentHostPreviewTasks";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/preview-tasks",
            HttpMethod.Get,
            "documentHostListDocumentPreviewTasks",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/preview-tasks",
            HttpMethod.Post,
            "documentHostCreateDocumentPreviewTask",
            tag,
            201,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/preview-tasks/{taskId}",
            HttpMethod.Get,
            "documentHostGetDocumentPreviewTask",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/preview-tasks/{taskId}/content",
            HttpMethod.Get,
            "documentHostDownloadDocumentPreviewTaskContent",
            tag,
            200,
            "application/octet-stream");
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
