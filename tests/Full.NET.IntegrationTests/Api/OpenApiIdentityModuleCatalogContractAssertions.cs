using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验 Host 模块清单端点在 OpenAPI 文档中的路径与核心 schema。</summary>
internal static class OpenApiIdentityModuleCatalogContractAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        using var contractDocument = await LoadContractDocumentAsync(cancellationToken);
        var contractPaths = contractDocument.RootElement.GetProperty("paths");

        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var openApiDocument = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var openApiPaths = openApiDocument.RootElement.GetProperty("paths");
        var schemas = openApiDocument.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        foreach (var contractPath in contractPaths.EnumerateArray())
        {
            var path = contractPath.GetProperty("path").GetString()
                ?? throw new InvalidOperationException("Contract path is required.");
            Assert.IsTrue(
                openApiPaths.TryGetProperty(path, out var openApiPath),
                $"OpenAPI 缺少路径：{path}");
            foreach (var operation in contractPath.GetProperty("operations").EnumerateArray())
            {
                var method = operation.GetProperty("method").GetString()!.ToLowerInvariant();
                Assert.IsTrue(
                    openApiPath.TryGetProperty(method, out var openApiOperation),
                    $"OpenAPI 缺少操作：{method.ToUpperInvariant()} {path}");
                var successStatus = operation.GetProperty("successStatus").GetInt32();
                var responses = openApiOperation.GetProperty("responses");
                Assert.IsTrue(
                    responses.TryGetProperty(successStatus.ToString(), out _)
                    || responses.EnumerateObject().Any(item =>
                        int.TryParse(item.Name, out var status)
                        && status is >= 200 and < 300),
                    $"OpenAPI 缺少 {successStatus} 响应：{method.ToUpperInvariant()} {path}");

                if (operation.TryGetProperty("responseSchema", out var responseSchema))
                {
                    var responseSchemaName = responseSchema.GetString()!;
                    Assert.IsTrue(
                        TryFindSchema(schemas, responseSchemaName, out var openApiSchema),
                        $"OpenAPI 缺少 schema：{responseSchemaName}");
                    var expectedProperties = contractDocument.RootElement
                        .GetProperty("schemas")
                        .GetProperty(responseSchemaName)
                        .GetProperty("properties");
                    var openApiProperties = openApiSchema.GetProperty("properties");
                    foreach (var property in expectedProperties.EnumerateArray())
                    {
                        var propertyName = property.GetString()!;
                        Assert.IsTrue(
                            openApiProperties.TryGetProperty(propertyName, out _),
                            $"OpenAPI schema {responseSchemaName} 缺少属性：{propertyName}");
                    }
                }
            }
        }
    }

    private static bool TryFindSchema(
        JsonElement openApiSchemas,
        string schemaName,
        out JsonElement schema)
    {
        if (openApiSchemas.TryGetProperty(schemaName, out schema))
        {
            return true;
        }

        foreach (var candidate in openApiSchemas.EnumerateObject())
        {
            if (candidate.Name.Contains("ModuleCatalog", StringComparison.Ordinal)
                || candidate.Name.EndsWith(schemaName, StringComparison.Ordinal))
            {
                schema = candidate.Value;
                return true;
            }
        }

        schema = default;
        return false;
    }

    private static async Task<JsonDocument> LoadContractDocumentAsync(
        CancellationToken cancellationToken)
    {
        var repositoryRoot = FindRepositoryRoot();
        var contractPath = Path.Combine(
            repositoryRoot,
            "contracts",
            "openapi",
            "identity-host-modules-v1.json");
        await using var stream = File.OpenRead(contractPath);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
