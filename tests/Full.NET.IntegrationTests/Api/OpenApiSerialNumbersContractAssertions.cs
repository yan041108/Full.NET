using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验流水号规则端点的 OpenAPI 路径、响应与核心 schema。</summary>
internal static class OpenApiSerialNumbersContractAssertions
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
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        AssertOperation(
            paths.GetProperty("/api/v1/serial-numbers/rules"),
            "get",
            ["200", "401"]);
        AssertOperation(
            paths.GetProperty("/api/v1/serial-numbers/rules"),
            "post",
            ["201", "400", "401", "409"]);
        AssertOperation(
            paths.GetProperty("/api/v1/serial-numbers/rules/{ruleId}"),
            "put",
            ["200", "400", "401", "404", "409"]);
        AssertOperation(
            paths.GetProperty("/api/v1/serial-numbers/rules/preview"),
            "post",
            ["200", "400", "401"]);

        var schemas = root.GetProperty("components").GetProperty("schemas");
        AssertSchema(
            schemas,
            "SerialNumberRuleResponse",
            [
                "id",
                "ruleKey",
                "scope",
                "resetInterval",
                "pattern",
                "minimumValue",
                "maximumValue",
                "isEnabled",
                "version",
            ]);
        AssertSchema(
            schemas,
            "CreateSerialNumberRuleRequest",
            [
                "ruleKey",
                "displayName",
                "scope",
                "resetInterval",
                "pattern",
                "minimumValue",
                "maximumValue",
            ]);
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
