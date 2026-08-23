using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 校验 Host 代码生成运行目录的 OpenAPI 路径、状态码与安全摘要 Schema。
/// </summary>
internal static class OpenApiCodeGenerationRunsContractAssertions
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

        var actualPaths = openApi.RootElement.GetProperty("paths");
        foreach (var expectedPath in contract.RootElement
                     .GetProperty("paths")
                     .EnumerateObject())
        {
            var actualPath = actualPaths.GetProperty(expectedPath.Name);
            foreach (var expectedOperation in expectedPath.Value
                         .EnumerateObject())
            {
                var responses = actualPath
                    .GetProperty(expectedOperation.Name)
                    .GetProperty("responses");
                foreach (var status in expectedOperation.Value
                             .EnumerateArray())
                {
                    Assert.IsTrue(
                        responses.TryGetProperty(status.GetString()!, out _),
                        $"OpenAPI {expectedOperation.Name} "
                        + $"{expectedPath.Name} 缺少状态码 {status}。");
                }
            }
        }

        var schemas = openApi.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        foreach (var expectedSchema in contract.RootElement
                     .GetProperty("schemas")
                     .EnumerateObject())
        {
            var properties = FindSchema(schemas, expectedSchema.Name)
                .GetProperty("properties");
            foreach (var property in expectedSchema.Value.EnumerateArray())
            {
                Assert.IsTrue(
                    properties.TryGetProperty(property.GetString()!, out _),
                    $"OpenAPI schema {expectedSchema.Name} "
                    + $"缺少属性 {property}。");
            }
        }
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string tag = "CodeGenerationRuns";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/runs/preview",
            HttpMethod.Post,
            "codeGenerationPreviewRun",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/runs/apply",
            HttpMethod.Post,
            "codeGenerationApplyRun",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/runs/rollback",
            HttpMethod.Post,
            "codeGenerationRollbackRun",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/runs/rollback-chain",
            HttpMethod.Post,
            "codeGenerationRollbackRunChain",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/runs",
            HttpMethod.Get,
            "codeGenerationListRuns",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/runs/{runId}/artifacts.zip",
            HttpMethod.Get,
            "codeGenerationDownloadRunArtifacts",
            tag,
            200,
            "application/octet-stream");
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
        await using var stream = File.OpenRead(Path.Combine(
            FindRepositoryRoot(),
            "contracts",
            "openapi",
            "code-generation-runs-v1.json"));
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

        throw new DirectoryNotFoundException(
            "Could not locate the Full.NET repository root.");
    }
}
