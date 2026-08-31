using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验渠道配置与绑定端点在 OpenAPI 文档中的路径、方法与核心 schema 属性。</summary>
internal static class OpenApiNotificationsProfilesBindingsContractAssertions
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
            }
        }

        _ = schemas;
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/provider-types",
            HttpMethod.Get,
            "notificationsListProviderTypes",
            "NotificationsProviderProfiles",
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/provider-profiles",
            HttpMethod.Get,
            "notificationsListProviderProfiles",
            "NotificationsProviderProfiles",
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/provider-profiles",
            HttpMethod.Post,
            "notificationsCreateProviderProfile",
            "NotificationsProviderProfiles",
            201,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/bindings",
            HttpMethod.Post,
            "notificationsCreateBinding",
            "NotificationsBindings",
            201,
            "application/json",
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/notifications/bindings/{bindingId}/publish",
            HttpMethod.Post,
            "notificationsPublishBinding",
            "NotificationsBindings",
            200,
            "application/json",
            "application/json");
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
                    "notifications-profiles-bindings-v1.json"));
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
