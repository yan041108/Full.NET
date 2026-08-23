using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 校验 Host 访问日志端点在 OpenAPI 文档中的路径与核心 schema。
/// </summary>
internal static class OpenApiAuditingAccessLogsContractAssertions
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
        AssertPilotOperations(openApiDocument.RootElement);
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
                    HasSuccessResponse(responses, successStatus),
                    $"OpenAPI 缺少 {successStatus} 响应：{method.ToUpperInvariant()} {path}");

                AssertQueryParameters(openApiOperation, operation, method, path);
                AssertErrorStatuses(responses, operation, method, path);

                if (operation.TryGetProperty("responseSchema", out var responseSchema))
                {
                    var responseSchemaName = responseSchema.GetString()!;
                    if (TryFindSchema(schemas, responseSchemaName, out _))
                    {
                        AssertSchemaProperties(
                            schemas,
                            responseSchemaName,
                            contractDocument.RootElement
                                .GetProperty("schemas")
                                .GetProperty(responseSchemaName));
                    }
                }
            }
        }
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string tag = "AuditingHostAccessLogs";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/auditing/access-logs",
            HttpMethod.Get,
            "auditingListHostAccessLogs",
            tag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/auditing/access-logs/cursor",
            HttpMethod.Get,
            "auditingListHostAccessLogsByCursor",
            tag,
            200,
            "application/json");
    }

    private static void AssertQueryParameters(
        JsonElement openApiOperation,
        JsonElement contractOperation,
        string method,
        string path)
    {
        if (!contractOperation.TryGetProperty(
                "queryParameters",
                out var contractParameters))
        {
            return;
        }

        var actualNames = openApiOperation.GetProperty("parameters")
            .EnumerateArray()
            .Where(parameter =>
                parameter.GetProperty("in").GetString() == "query")
            .Select(parameter => parameter.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var parameter in contractParameters.EnumerateArray())
        {
            var name = parameter.GetString()
                ?? throw new InvalidOperationException(
                    "Contract query parameter is required.");
            Assert.IsTrue(
                actualNames.Contains(name),
                $"OpenAPI 缺少查询参数 {name}：{method.ToUpperInvariant()} {path}");
        }
    }

    private static void AssertErrorStatuses(
        JsonElement responses,
        JsonElement contractOperation,
        string method,
        string path)
    {
        if (!contractOperation.TryGetProperty(
                "errorStatuses",
                out var errorStatuses))
        {
            return;
        }

        foreach (var status in errorStatuses.EnumerateArray())
        {
            var statusCode = status.GetInt32();
            Assert.IsTrue(
                responses.TryGetProperty(statusCode.ToString(), out _),
                $"OpenAPI 缺少 {statusCode} 响应：{method.ToUpperInvariant()} {path}");
        }
    }

    private static bool HasSuccessResponse(JsonElement responses, int successStatus)
    {
        if (responses.TryGetProperty(successStatus.ToString(), out _))
        {
            return true;
        }

        if (successStatus is >= 200 and < 300)
        {
            foreach (var response in responses.EnumerateObject())
            {
                if (int.TryParse(response.Name, out var status)
                    && status is >= 200 and < 300)
                {
                    return true;
                }
            }
        }

        return responses.TryGetProperty("default", out _)
            || responses.TryGetProperty("2XX", out _)
            || responses.TryGetProperty("2xx", out _);
    }

    private static void AssertSchemaProperties(
        JsonElement openApiSchemas,
        string schemaName,
        JsonElement contractSchema)
    {
        if (!TryFindSchema(openApiSchemas, schemaName, out var openApiSchema))
        {
            Assert.Fail($"OpenAPI 缺少 schema：{schemaName}");
        }

        var openApiProperties = openApiSchema.GetProperty("properties");
        foreach (var property in contractSchema.GetProperty("properties").EnumerateArray())
        {
            var propertyName = property.GetString()
                ?? throw new InvalidOperationException("Contract property is required.");
            Assert.IsTrue(
                openApiProperties.TryGetProperty(propertyName, out _),
                $"OpenAPI schema {schemaName} 缺少属性：{propertyName}");
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
            if (candidate.Name.Contains("AccessLog", StringComparison.Ordinal)
                && schemaName.Contains("AccessLog", StringComparison.Ordinal))
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
            "auditing-access-logs-v1.json");
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
