using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 校验 Host OpenAPI 文档与 Scalar UI 是否按平台契约暴露。
/// </summary>
internal static class OpenApiDocumentationAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        using var contractDocument = await LoadContractDocumentAsync(cancellationToken);
        var contractRoot = contractDocument.RootElement;
        var openApiJsonPath = contractRoot.GetProperty("openApiJsonPath").GetString()
            ?? throw new InvalidOperationException("Contract openApiJsonPath is required.");
        var scalarUiPath = contractRoot.GetProperty("scalarUiPath").GetString()
            ?? throw new InvalidOperationException("Contract scalarUiPath is required.");
        var apiTitle = contractRoot.GetProperty("apiTitle").GetString()
            ?? throw new InvalidOperationException("Contract apiTitle is required.");
        var securitySchemeName = contractRoot.GetProperty("securitySchemeName").GetString()
            ?? throw new InvalidOperationException("Contract securitySchemeName is required.");

        using var openApiResponse = await client.GetAsync(openApiJsonPath, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, openApiResponse.StatusCode);
        using var openApiDocument = JsonDocument.Parse(
            await openApiResponse.Content.ReadAsStringAsync(cancellationToken));
        var openApiRoot = openApiDocument.RootElement;

        Assert.IsTrue(
            openApiRoot.TryGetProperty("openapi", out var openApiVersion),
            "OpenAPI 文档缺少 openapi 版本字段。");
        Assert.IsFalse(string.IsNullOrWhiteSpace(openApiVersion.GetString()));

        var info = openApiRoot.GetProperty("info");
        Assert.AreEqual(apiTitle, info.GetProperty("title").GetString());
        Assert.AreEqual(
            contractRoot.GetProperty("documentName").GetString(),
            info.GetProperty("version").GetString());

        var securityScheme = openApiRoot
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty(securitySchemeName);
        Assert.AreEqual(
            contractRoot.GetProperty("securitySchemeType").GetString(),
            securityScheme.GetProperty("type").GetString());
        Assert.AreEqual(
            contractRoot.GetProperty("securitySchemeScheme").GetString(),
            securityScheme.GetProperty("scheme").GetString());

        var paths = openApiRoot.GetProperty("paths");
        Assert.IsTrue(
            paths.EnumerateObject().Any(path => path.Name.StartsWith("/api/v1/", StringComparison.Ordinal)),
            "OpenAPI 文档未包含任何 /api/v1/** 路径。");

        using var scalarResponse = await client.GetAsync(scalarUiPath, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, scalarResponse.StatusCode);
        var scalarContentType = scalarResponse.Content.Headers.ContentType?.MediaType ?? string.Empty;
        StringAssert.Contains(scalarContentType, "text/html");
        var scalarBody = await scalarResponse.Content.ReadAsStringAsync(cancellationToken);
        StringAssert.Contains(scalarBody.ToLowerInvariant(), "scalar");
    }

    private static async Task<JsonDocument> LoadContractDocumentAsync(
        CancellationToken cancellationToken)
    {
        var repositoryRoot = FindRepositoryRoot();
        var contractPath = Path.Combine(
            repositoryRoot,
            "contracts",
            "openapi",
            "platform-api-documentation-v1.json");
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
