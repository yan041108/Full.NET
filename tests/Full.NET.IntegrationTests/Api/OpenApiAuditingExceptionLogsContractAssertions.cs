using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 校验 Host 异常日志端点在 OpenAPI 文档中的路径与核心 schema。
/// </summary>
internal static class OpenApiAuditingExceptionLogsContractAssertions
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

                AssertQueryContract(openApiOperation, responses, operation, method, path);

                if (operation.TryGetProperty("responseSchema", out var responseSchema))
                {
                    var responseSchemaName = responseSchema.GetString()!;
                    Assert.IsTrue(
                        TryFindSchema(schemas, responseSchemaName, out var openApiSchema),
                        $"OpenAPI 缺少 schema：{responseSchemaName}");
                    var openApiProperties = openApiSchema.GetProperty("properties");
                    foreach (var property in contractDocument.RootElement
                                 .GetProperty("schemas")
                                 .GetProperty(responseSchemaName)
                                 .GetProperty("properties")
                                 .EnumerateArray())
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

    private static void AssertPilotOperations(JsonElement document)
    {
        const string tag = "AuditingHostExceptionLogs";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/auditing/exception-logs",
            HttpMethod.Get,
            "auditingListHostExceptionLogs",
            tag,
            200,
            "application/json");
    }

    private static void AssertQueryContract(
        JsonElement openApiOperation,
        JsonElement responses,
        JsonElement contractOperation,
        string method,
        string path)
    {
        if (contractOperation.TryGetProperty(
                "queryParameters",
                out var queryParameters))
        {
            var actualNames = openApiOperation.GetProperty("parameters")
                .EnumerateArray()
                .Where(parameter =>
                    parameter.GetProperty("in").GetString() == "query")
                .Select(parameter => parameter.GetProperty("name").GetString())
                .ToHashSet(StringComparer.Ordinal);
            foreach (var parameter in queryParameters.EnumerateArray())
            {
                var name = parameter.GetString()!;
                Assert.IsTrue(
                    actualNames.Contains(name),
                    $"OpenAPI 缺少查询参数 {name}：{method.ToUpperInvariant()} {path}");
            }
        }

        if (contractOperation.TryGetProperty(
                "errorStatuses",
                out var errorStatuses))
        {
            foreach (var status in errorStatuses.EnumerateArray())
            {
                var statusCode = status.GetInt32();
                Assert.IsTrue(
                    responses.TryGetProperty(statusCode.ToString(), out _),
                    $"OpenAPI 缺少 {statusCode} 响应：{method.ToUpperInvariant()} {path}");
            }
        }
    }

    private static bool HasSuccessResponse(JsonElement responses, int successStatus)
    {
        if (responses.TryGetProperty(successStatus.ToString(), out _))
        {
            return true;
        }

        foreach (var response in responses.EnumerateObject())
        {
            if (int.TryParse(response.Name, out var status)
                && status is >= 200 and < 300)
            {
                return true;
            }
        }

        return responses.TryGetProperty("default", out _)
            || responses.TryGetProperty("2XX", out _);
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
            if (candidate.Name.Contains("ExceptionLog", StringComparison.Ordinal))
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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var sln = Path.Combine(directory.FullName, "Full.NET.slnx");
            if (File.Exists(sln))
            {
                var contractPath = Path.Combine(
                    directory.FullName,
                    "contracts",
                    "openapi",
                    "auditing-exception-logs-v1.json");
                await using var stream = File.OpenRead(contractPath);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
