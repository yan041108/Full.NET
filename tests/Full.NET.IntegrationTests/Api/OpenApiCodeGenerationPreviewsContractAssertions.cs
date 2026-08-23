using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 校验代码生成预览端点的 OpenAPI 路径与核心 Schema。
/// </summary>
internal static class OpenApiCodeGenerationPreviewsContractAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        using var contract = await LoadContractAsync(cancellationToken);
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var openApi = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        AssertPilotOperations(openApi.RootElement);

        var expected = contract.RootElement;
        var path = expected.GetProperty("path").GetString()!;
        var operation = openApi.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty("post");
        Assert.IsTrue(operation.TryGetProperty("requestBody", out _));
        Assert.IsTrue(
            operation.GetProperty("responses").TryGetProperty("200", out _));
        Assert.IsTrue(
            operation.GetProperty("responses").TryGetProperty(
                expected.GetProperty("errorStatus").GetInt32().ToString(),
                out _));

        var schemas = openApi.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        foreach (var schemaContract in expected.GetProperty("schemas")
            .EnumerateObject())
        {
            var schema = FindSchema(schemas, schemaContract.Name);
            var properties = schema.GetProperty("properties");
            foreach (var property in schemaContract.Value.EnumerateArray())
            {
                Assert.IsTrue(
                    properties.TryGetProperty(property.GetString()!, out _),
                    $"OpenAPI schema {schemaContract.Name} 缺少属性 {property}。");
            }
        }
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string tag = "CodeGenerationPreviews";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/previews",
            HttpMethod.Post,
            "codeGenerationPreviewCrud",
            tag,
            200,
            "application/json");
    }

    private static JsonElement FindSchema(
        JsonElement schemas,
        string suffix)
    {
        foreach (var schema in schemas.EnumerateObject())
        {
            if (schema.Name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return schema.Value;
            }
        }

        Assert.Fail($"OpenAPI 缺少 schema：{suffix}");
        return default;
    }

    private static async Task<JsonDocument> LoadContractAsync(
        CancellationToken cancellationToken)
    {
        var repositoryRoot = FindRepositoryRoot();
        await using var stream = File.OpenRead(Path.Combine(
            repositoryRoot,
            "contracts",
            "openapi",
            "code-generation-previews-v1.json"));
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
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
