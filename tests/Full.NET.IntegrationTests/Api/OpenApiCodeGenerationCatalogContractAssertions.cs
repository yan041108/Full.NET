using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 校验 Host 代码生成数据库目录的 OpenAPI 路径与核心 Schema。
/// </summary>
internal static class OpenApiCodeGenerationCatalogContractAssertions
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
                        $"OpenAPI {expectedOperation.Name} {expectedPath.Name} "
                            + $"缺少 {status.GetString()}。");
                }
            }
        }
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string tag = "CodeGenerationCatalog";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/catalog/tables",
            HttpMethod.Get,
            "codeGenerationListCatalogTables",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/catalog/tables/{tableName}/columns",
            HttpMethod.Get,
            "codeGenerationListCatalogColumns",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/code-generation/catalog/column-sync",
            HttpMethod.Post,
            "codeGenerationSyncCatalogColumns",
            tag,
            200,
            "application/json",
            "application/json");
    }

    private static async Task<JsonDocument> LoadContractAsync(
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(Path.Combine(
            FindRepositoryRoot(),
            "contracts",
            "openapi",
            "code-generation-catalog-v1.json"));
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
