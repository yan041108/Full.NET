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
            CreateOrganizationOwnedPreviewRequest());
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

    private static CodeGenerationPreviewRequest CreateOrganizationOwnedPreviewRequest() =>
        new(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "tenant.required",
            null,
            [
                new("Id", "Id", "id", "uuid", false, null, null, null),
                new("TenantId", "TenantId", "tenantId", "uuid", false, null, null, null),
                new(
                    "OrganizationUnitId",
                    "OrganizationUnitId",
                    "organizationUnitId",
                    "uuid",
                    false,
                    null,
                    null,
                    null),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    "string",
                    false,
                    200,
                    null,
                    null),
                new("Version", "Version", "version", "int64", false, null, null, null),
                new(
                    "CreatedAtUtc",
                    "CreatedAtUtc",
                    "createdAtUtc",
                    "date.time.utc",
                    false,
                    null,
                    null,
                    null),
                new(
                    "CreatedById",
                    "CreatedById",
                    "createdById",
                    "uuid",
                    false,
                    null,
                    null,
                    null),
                new(
                    "UpdatedAtUtc",
                    "UpdatedAtUtc",
                    "updatedAtUtc",
                    "date.time.utc",
                    true,
                    null,
                    null,
                    null),
                new(
                    "UpdatedById",
                    "UpdatedById",
                    "updatedById",
                    "uuid",
                    true,
                    null,
                    null,
                    null),
                new(
                    "IsDeleted",
                    "IsDeleted",
                    "isDeleted",
                    "boolean",
                    false,
                    null,
                    null,
                    null),
                new(
                    "DeletedAtUtc",
                    "DeletedAtUtc",
                    "deletedAtUtc",
                    "date.time.utc",
                    true,
                    null,
                    null,
                    null),
                new(
                    "DeletedById",
                    "DeletedById",
                    "deletedById",
                    "uuid",
                    true,
                    null,
                    null,
                    null),
            ],
            new CodeGenerationEntityCapabilitiesRequest(
                "soft.delete",
                HasCreatedAudit: true,
                HasUpdatedAudit: true,
                HasDeletedAudit: true,
                HasVersion: true,
                "organization.unit"),
            "single",
            []);
}
