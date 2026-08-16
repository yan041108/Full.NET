using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.CodeGeneration.Contracts;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>
/// 验证 Host 代码生成预览的授权、确定性产物和安全错误契约。
/// </summary>
internal static class CodeGenerationPreviewAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        await VerifyAnonymousRequestAsync(client, cancellationToken);
        await VerifyPermissionAsync(factory, client, cancellationToken);
        await VerifyPreviewAsync(factory, client, cancellationToken);
        await VerifyOrganizationOwnedPreviewAsync(factory, client, cancellationToken);
        await VerifyHostScopeOrganizationOwnershipRejectedAsync(
            factory,
            client,
            cancellationToken);
        await VerifyInvalidSchemaAsync(factory, client, cancellationToken);
        await OpenApiCodeGenerationPreviewsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyAnonymousRequestAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/code-generation/previews",
            CreatePreviewRequest(),
            cancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task VerifyPermissionAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            await factory.CreateHostAccessTokenAsync(
                ["platform.dashboard.read"],
                cancellationToken),
            CreatePreviewRequest());
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task VerifyPreviewAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            await factory.CreateHostAccessTokenAsync(
                [CodeGenerationPreviewPermissions.Read],
                cancellationToken),
            CreatePreviewRequest());
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content
            .ReadFromJsonAsync<CodeGenerationPreviewResponse>(cancellationToken);
        Assert.IsNotNull(preview);
        Assert.AreEqual("acme_catalog_product", preview.DatabaseTableName);
        Assert.IsTrue(preview.Artifacts.Count >= 7);
        Assert.IsTrue(preview.Artifacts.All(artifact =>
            artifact.Sha256.Length == 64));
        Assert.IsTrue(preview.Artifacts.Any(artifact =>
            artifact.Kind == "vue_client"));
        Assert.IsTrue(preview.Artifacts.Any(artifact =>
            artifact.Kind == "vue_view"));
        // Layui 客户端已冻结，Host 预览默认不再发出 layui_client。
        Assert.IsFalse(preview.Artifacts.Any(artifact =>
            artifact.Kind == "layui_client"));
    }

    private static async Task VerifyOrganizationOwnedPreviewAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            await factory.CreateHostAccessTokenAsync(
                [CodeGenerationPreviewPermissions.Read],
                cancellationToken),
            CodeGenerationOrganizationOwnedTestSupport.CreatePreviewRequest());
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content
            .ReadFromJsonAsync<CodeGenerationPreviewResponse>(cancellationToken);
        Assert.IsNotNull(preview);
        var feature = preview!.Artifacts
            .Single(artifact => artifact.Path == "backend/ProductFeature.g.cs")
            .Content;
        StringAssert.Contains(feature, "IOrganizationOwnedEntityWriteAuthorizer");
        StringAssert.Contains(feature, "BuildOrganizationUnitFilter");
    }

    private static async Task VerifyHostScopeOrganizationOwnershipRejectedAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        foreach (var dataScope in new[] { "host.only", "global" })
        {
            var organizationOwned =
                CodeGenerationOrganizationOwnedTestSupport.CreatePreviewRequest();
            var invalid = organizationOwned with
            {
                DataScope = dataScope,
                Columns = organizationOwned.Columns
                    .Where(column =>
                        !string.Equals(
                            column.ClrPropertyName,
                            "TenantId",
                            StringComparison.Ordinal))
                    .ToArray(),
            };
            using var request = CreateRequest(
                await factory.CreateHostAccessTokenAsync(
                    [CodeGenerationPreviewPermissions.Read],
                    cancellationToken),
                invalid);
            using var response = await client.SendAsync(request, cancellationToken);

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(
                CodeGenerationErrorCodes.InvalidPreviewSchema,
                problem.RootElement.GetProperty("code").GetString());
        }
    }

    private static async Task VerifyInvalidSchemaAsync(
        FullNetApiFactory factory,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var invalid = CreatePreviewRequest() with
        {
            DataScope = "Unspecified",
        };
        using var request = CreateRequest(
            await factory.CreateHostAccessTokenAsync(
                [CodeGenerationPreviewPermissions.Read],
                cancellationToken),
            invalid);
        using var response = await client.SendAsync(request, cancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(
            CodeGenerationErrorCodes.InvalidPreviewSchema,
            problem.RootElement.GetProperty("code").GetString());
    }

    private static HttpRequestMessage CreateRequest(
        string accessToken,
        CodeGenerationPreviewRequest input)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/code-generation/previews")
        {
            Content = JsonContent.Create(input),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        return request;
    }

    private static CodeGenerationPreviewRequest CreatePreviewRequest() =>
        new(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "TenantRequired",
            true,
            [
                new("Id", "Id", "id", "Uuid", false, null, null, null),
                new(
                    "TenantId",
                    "TenantId",
                    "tenantId",
                    "Uuid",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    "String",
                    false,
                    200,
                    null,
                    null),
                new(
                    "IsActive",
                    "IsActive",
                    "isActive",
                    "Boolean",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Version",
                    "Version",
                    "version",
                    "Int64",
                    false,
                    null,
                    null,
                    null),
            ]);
}
