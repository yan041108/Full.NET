using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验 Host 文档条目端点的 OpenAPI 路径、响应与核心 schema。</summary>
internal static class OpenApiDocumentHostItemsContractAssertions
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
            paths.GetProperty("/api/v1/document/host/items"),
            "get",
            ["200", "401"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/items"),
            "post",
            ["201", "400", "401"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/items/{itemId}"),
            "put",
            ["200", "400", "401", "404", "409"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/items/{itemId}/versions"),
            "get",
            ["200", "401", "404"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/items/{itemId}/versions"),
            "post",
            ["200", "400", "401", "404", "409"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/items/{itemId}/delete"),
            "post",
            ["200", "400", "401", "404", "409"]);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertSchema(
            schemas,
            "HostDocumentItemResponse",
            [
                "id",
                "title",
                "description",
                "categoryId",
                "currentVersion",
                "createdAtUtc",
                "createdByUserId",
                "updatedAtUtc",
                "updatedByUserId",
                "version",
            ]);
        AssertSchema(
            schemas,
            "CreateHostDocumentItemRequest",
            ["title", "description"]);
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string tag = "DocumentHostItems";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items",
            HttpMethod.Get,
            "documentHostListItems",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items",
            HttpMethod.Post,
            "documentHostCreateItem",
            tag,
            201,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items/{itemId}",
            HttpMethod.Put,
            "documentHostUpdateItem",
            tag,
            200,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items/{itemId}/versions",
            HttpMethod.Get,
            "documentHostListItemVersions",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items/{itemId}/versions",
            HttpMethod.Post,
            "documentHostAddItemVersion",
            tag,
            200,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items/{itemId}/versions/upload",
            HttpMethod.Post,
            "documentHostUploadItemVersion",
            tag,
            200,
            "application/json",
            "multipart/form-data");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items/{itemId}/content",
            HttpMethod.Get,
            "documentHostDownloadItemContent",
            tag,
            200,
            "application/octet-stream");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items/{itemId}/preview",
            HttpMethod.Get,
            "documentHostPreviewItemContent",
            tag,
            200,
            "application/octet-stream");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items/{itemId}/versions/{versionId}/preview",
            HttpMethod.Get,
            "documentHostPreviewItemVersionContent",
            tag,
            200,
            "application/octet-stream");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items/{itemId}/delete",
            HttpMethod.Post,
            "documentHostDeleteItem",
            tag,
            200,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/items/{itemId}/restore",
            HttpMethod.Post,
            "documentHostRestoreItem",
            tag,
            200,
            "application/json",
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
