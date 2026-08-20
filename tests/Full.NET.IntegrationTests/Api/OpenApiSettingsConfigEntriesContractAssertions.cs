using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 校验 Host 系统配置端点在 OpenAPI 文档中的路径、方法与核心 schema 属性。
/// </summary>
internal static class OpenApiSettingsConfigEntriesContractAssertions
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
        AssertStringEnum(
            schemas,
            "ConfigEntryResponse",
            "valueKind",
            ["string", "boolean", "integer", "decimal", "json", "secret"]);
        AssertStringEnum(
            schemas,
            "CreateConfigEntryRequest",
            "valueKind",
            ["string", "boolean", "integer", "decimal", "json", "secret"]);

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

                if (operation.TryGetProperty("requestSchema", out var requestSchema))
                {
                    AssertSchemaProperties(
                        schemas,
                        requestSchema.GetString()!,
                        contractDocument.RootElement
                            .GetProperty("schemas")
                            .GetProperty(requestSchema.GetString()!));
                }

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
        const string tag = "SettingsHostConfigEntries";
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries", HttpMethod.Get, "settingsListHostConfigEntries", tag, 200, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries/by-key/{configKey}", HttpMethod.Get, "settingsGetHostConfigEntryByKey", tag, 200, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries/{configEntryId}", HttpMethod.Get, "settingsGetHostConfigEntry", tag, 200, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries", HttpMethod.Post, "settingsCreateHostConfigEntry", tag, 201, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries/{configEntryId}", HttpMethod.Put, "settingsUpdateHostConfigEntry", tag, 200, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries/{configEntryId}/disable", HttpMethod.Post, "settingsDisableHostConfigEntry", tag, 200, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries/{configEntryId}/delete", HttpMethod.Post, "settingsDeleteHostConfigEntry", tag, 204, null, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries/batch-delete", HttpMethod.Post, "settingsBatchDeleteHostConfigEntries", tag, 204, null, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries/batch-update-values", HttpMethod.Post, "settingsBatchUpdateHostConfigEntryValues", tag, 200, "application/json", "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries/list", HttpMethod.Get, "settingsListAllHostConfigEntries", tag, 200, "application/json");
        OpenApiPilotContractAssertions.AssertOperation(document, "/api/v1/settings/config-entries/groups", HttpMethod.Get, "settingsListHostConfigEntryGroups", tag, 200, "application/json");
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

    private static void AssertStringEnum(
        JsonElement schemas,
        string schemaName,
        string propertyName,
        string[] expectedValues)
    {
        var property = schemas
            .GetProperty(schemaName)
            .GetProperty("properties")
            .GetProperty(propertyName);
        Assert.IsTrue(
            property.TryGetProperty("enum", out var enumValues),
            $"OpenAPI schema {schemaName}.{propertyName} 缺少稳定机器码 enum。");
        CollectionAssert.AreEqual(
            expectedValues,
            enumValues.EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    private static void AssertSchemaProperties(
        JsonElement openApiSchemas,
        string schemaName,
        JsonElement contractSchema)
    {
        if (!TryFindSchema(openApiSchemas, schemaName, out var openApiSchema))
        {
            var available = string.Join(
                ", ",
                openApiSchemas.EnumerateObject().Select(item => item.Name).OrderBy(name => name));
            Assert.Fail($"OpenAPI 缺少 schema：{schemaName}；现有：{available}");
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

        if (schemaName is "ConfigEntryResponsePage")
        {
            foreach (var candidate in openApiSchemas.EnumerateObject())
            {
                if (!candidate.Name.Contains("ConfigEntry", StringComparison.Ordinal))
                {
                    continue;
                }

                if (candidate.Value.TryGetProperty("properties", out var properties)
                    && (properties.TryGetProperty("items", out _)
                        || properties.TryGetProperty("Items", out _)))
                {
                    schema = candidate.Value;
                    return true;
                }
            }
        }

        foreach (var candidate in openApiSchemas.EnumerateObject())
        {
            if (candidate.Name.EndsWith(schemaName, StringComparison.Ordinal))
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
            "settings-config-entries-v1.json");
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
