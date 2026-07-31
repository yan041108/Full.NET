using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 校验租户职级管理端点在 OpenAPI 文档中的路径、方法与核心 schema 属性。
/// </summary>
internal static class OpenApiOrganizationTenantPositionLevelsContractAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        using var contractDocument = await LoadContractDocumentAsync(cancellationToken);
        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var openApiDocument = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var openApiPaths = openApiDocument.RootElement.GetProperty("paths");
        var schemas = openApiDocument.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        foreach (var contractPath in contractDocument.RootElement
                     .GetProperty("paths")
                     .EnumerateArray())
        {
            var path = contractPath.GetProperty("path").GetString()!;
            Assert.IsTrue(
                openApiPaths.TryGetProperty(path, out var openApiPath),
                $"OpenAPI 缺少路径：{path}");
            foreach (var operation in contractPath.GetProperty("operations").EnumerateArray())
            {
                var method = operation.GetProperty("method").GetString()!.ToLowerInvariant();
                Assert.IsTrue(
                    openApiPath.TryGetProperty(method, out var openApiOperation),
                    $"OpenAPI 缺少操作：{method.ToUpperInvariant()} {path}");
                var status = operation.GetProperty("successStatus").GetInt32().ToString();
                Assert.IsTrue(openApiOperation.GetProperty("responses").TryGetProperty(
                    status,
                    out _));

                if (operation.TryGetProperty("requestSchema", out var requestSchema))
                {
                    AssertSchemaProperties(
                        schemas,
                        requestSchema.GetString()!,
                        contractDocument.RootElement.GetProperty("schemas")
                            .GetProperty(requestSchema.GetString()!));
                }

                if (operation.TryGetProperty("responseSchema", out var responseSchema))
                {
                    AssertSchemaProperties(
                        schemas,
                        responseSchema.GetString()!,
                        contractDocument.RootElement.GetProperty("schemas")
                            .GetProperty(responseSchema.GetString()!));
                }
            }
        }
    }

    private static void AssertSchemaProperties(
        JsonElement schemas,
        string schemaName,
        JsonElement contractSchema)
    {
        var candidates = schemas.EnumerateObject()
            .Where(candidate => schemaName.EndsWith("Page", StringComparison.Ordinal)
                ? candidate.Name.Contains("OrganizationPositionLevelResponse", StringComparison.Ordinal)
                    && candidate.Value.TryGetProperty("properties", out var pageProperties)
                    && pageProperties.TryGetProperty("items", out _)
                : candidate.Name.EndsWith(schemaName, StringComparison.Ordinal))
            .ToArray();
        Assert.IsTrue(candidates.Length > 0, $"OpenAPI 缺少 schema：{schemaName}");
        var properties = candidates[0].Value.GetProperty("properties");
        foreach (var property in contractSchema.GetProperty("properties").EnumerateArray())
        {
            Assert.IsTrue(properties.TryGetProperty(property.GetString()!, out _));
        }
    }

    private static async Task<JsonDocument> LoadContractDocumentAsync(
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                await using var stream = File.OpenRead(Path.Combine(
                    directory.FullName,
                    "contracts",
                    "openapi",
                    "organization-tenant-position-levels-v1.json"));
                return await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
