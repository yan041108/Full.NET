using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验模板与意图端点在 OpenAPI 文档中的路径、方法与核心 schema 属性。</summary>
internal static class OpenApiNotificationsTemplatesIntentsContractAssertions
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
                Assert.IsTrue(
                    openApiOperation.GetProperty("responses").TryGetProperty(
                        successStatus.ToString(),
                        out _),
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
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/templates",
            HttpMethod.Get,
            "notificationsListTemplates",
            "NotificationsTemplates",
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/templates",
            HttpMethod.Post,
            "notificationsCreateTemplate",
            "NotificationsTemplates",
            201,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/templates/{templateId}",
            HttpMethod.Get,
            "notificationsGetTemplate",
            "NotificationsTemplates",
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/templates/{templateId}",
            HttpMethod.Put,
            "notificationsUpdateTemplate",
            "NotificationsTemplates",
            200,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/templates/{templateId}/publish",
            HttpMethod.Post,
            "notificationsPublishTemplate",
            "NotificationsTemplates",
            200,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/intents",
            HttpMethod.Post,
            "notificationsCreateIntent",
            "NotificationsIntents",
            201,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/intents/{intentId}",
            HttpMethod.Get,
            "notificationsGetIntent",
            "NotificationsIntents",
            200,
            "application/json");
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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                await using var stream = File.OpenRead(Path.Combine(
                    directory.FullName,
                    "contracts",
                    "openapi",
                    "notifications-templates-intents-v1.json"));
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
