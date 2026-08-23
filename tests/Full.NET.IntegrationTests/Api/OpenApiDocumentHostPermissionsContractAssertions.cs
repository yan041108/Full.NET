using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验 Host 文档权限端点的 OpenAPI 路径、响应与核心 schema。</summary>
internal static class OpenApiDocumentHostPermissionsContractAssertions
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
            paths.GetProperty("/api/v1/document/host/permissions/by-document/{documentId}"),
            "get",
            ["200", "401", "403", "404"]);
        AssertOperation(
            paths.GetProperty("/api/v1/document/host/permissions"),
            "post",
            ["200", "400", "401", "403", "404"]);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertSchema(
            schemas,
            "HostDocumentPermissionResponse",
            ["id", "documentId", "userId", "permissionLevel", "createdAtUtc"]);
        AssertSchema(
            schemas,
            "SetHostDocumentPermissionsRequest",
            ["documentId", "permissions"]);
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string tag = "DocumentHostPermissions";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/permissions/by-document/{documentId}",
            HttpMethod.Get,
            "documentHostListDocumentPermissions",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/document/host/permissions",
            HttpMethod.Post,
            "documentHostSetDocumentPermissions",
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
