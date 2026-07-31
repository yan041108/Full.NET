using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验 Grid 偏好端点的 OpenAPI 路径、方法和核心 schema。</summary>
internal static class OpenApiSettingsGridPreferencesContractAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var path = root.GetProperty("paths")
            .GetProperty("/api/v1/me/grid-preferences/{gridKey}");
        AssertOperation(path, "get", ["200", "401", "404"]);
        AssertOperation(path, "put", ["200", "400", "401", "404", "409"]);
        AssertOperation(path, "delete", ["200", "401", "404"]);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertSchema(
            schemas,
            "GridPreferenceResponse",
            ["gridKey", "schemaVersion", "columns", "version"]);
        AssertSchema(
            schemas,
            "UpdateGridPreferenceRequest",
            ["schemaVersion", "columns", "version"]);
        AssertSchema(
            schemas,
            "GridColumnPreference",
            ["columnKey", "order", "width", "visible", "fixed"]);
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
            .Single(candidate => candidate.Name.EndsWith(
                suffix,
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
