using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>
/// 验收 Host 代码生成模板目录的授权、审计、并发与软删除契约。
/// </summary>
internal static class CodeGenerationTemplateAssertions
{
    private const string TemplatesPath =
        "/api/v1/code-generation/templates/";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        using (var anonymous = await client.GetAsync(
                   TemplatesPath,
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Unauthorized,
                anonymous.StatusCode);
        }

        var wrongToken = await factory.CreateHostAccessTokenAsync(
            ["platform.dashboard.read"],
            cancellationToken);
        using (var forbidden = await client.SendAsync(
                   Authorized(HttpMethod.Get, TemplatesPath, wrongToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var reader = await factory.CreateHostIdentityAsync(
            $"codegen-reader-{Guid.NewGuid():N}",
            [CodeGenerationTemplatePermissions.Read],
            cancellationToken);
        var writer = await factory.CreateHostIdentityAsync(
            $"codegen-writer-{Guid.NewGuid():N}",
            [
                CodeGenerationTemplatePermissions.Read,
                CodeGenerationTemplatePermissions.Create,
                CodeGenerationTemplatePermissions.Update,
                CodeGenerationTemplatePermissions.Delete,
            ],
            cancellationToken);
        using (var forbiddenWrite = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       TemplatesPath,
                       reader.AccessToken,
                       new CreateCodeGenerationTemplateRequest(
                           "forbidden",
                           null,
                           CreateSchema())),
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Forbidden,
                forbiddenWrite.StatusCode);
        }

        var tenantToken = await LoginAndEnterAcmeTenantAsync(
            client,
            cancellationToken);
        using (var tenantDenied = await client.SendAsync(
                   Authorized(HttpMethod.Get, TemplatesPath, tenantToken),
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Forbidden,
                tenantDenied.StatusCode);
        }

        var created = await CreateAsync(
            client,
            writer,
            cancellationToken);
        await VerifyReadAndUpdateAsync(
            client,
            writer,
            created,
            cancellationToken);
        await VerifyValidationAsync(
            client,
            writer.AccessToken,
            cancellationToken);
        await OpenApiCodeGenerationTemplatesContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task<CodeGenerationTemplateResponse> CreateAsync(
        HttpClient client,
        HostTestIdentity writer,
        CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                TemplatesPath,
                writer.AccessToken,
                new CreateCodeGenerationTemplateRequest(
                    " Product CRUD ",
                    " Host template ",
                    CreateSchema())),
            cancellationToken);

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content
            .ReadFromJsonAsync<CodeGenerationTemplateResponse>(
                cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual("Product CRUD", created.Name);
        Assert.AreEqual("Host template", created.Description);
        Assert.AreEqual(writer.UserId, created.CreatedByUserId);
        Assert.AreEqual(1, created.Version);
        Assert.AreEqual("host.only", created.Schema.DataScope);
        Assert.AreEqual(64, created.SchemaSha256.Length);
        return created;
    }

    private static async Task VerifyReadAndUpdateAsync(
        HttpClient client,
        HostTestIdentity writer,
        CodeGenerationTemplateResponse created,
        CancellationToken cancellationToken)
    {
        using (var listResponse = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       TemplatesPath + "?page=1&pageSize=100",
                       writer.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
            var page = await listResponse.Content.ReadFromJsonAsync<
                PagedResult<CodeGenerationTemplateResponse>>(
                cancellationToken);
            Assert.IsNotNull(page);
            Assert.AreEqual(1, page.Total);
            Assert.AreEqual(created.Id, page.Items.Single().Id);
        }

        using (var getResponse = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       TemplatesPath + created.Id,
                       writer.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        }

        var update = new UpdateCodeGenerationTemplateRequest(
            "Updated Product",
            null,
            CreateSchema(),
            created.Version);
        CodeGenerationTemplateResponse updated;
        using (var updateResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Put,
                       TemplatesPath + created.Id,
                       writer.AccessToken,
                       update),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);
            updated = (await updateResponse.Content.ReadFromJsonAsync<
                CodeGenerationTemplateResponse>(cancellationToken))!;
            Assert.IsNotNull(updated);
            Assert.AreEqual(created.Version + 1, updated.Version);
            Assert.AreEqual(writer.UserId, updated.UpdatedByUserId);
        }

        using (var staleResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Put,
                       TemplatesPath + created.Id,
                       writer.AccessToken,
                       update),
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.Conflict,
                staleResponse.StatusCode);
            Assert.AreEqual(
                CodeGenerationTemplateErrorCodes.VersionConflict,
                await ReadCodeAsync(staleResponse, cancellationToken));
        }

        using (var deleteResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       TemplatesPath + created.Id + "/delete",
                       writer.AccessToken,
                       new DeleteCodeGenerationTemplateRequest(
                           updated.Version)),
                   cancellationToken))
        {
            Assert.AreEqual(
                HttpStatusCode.NoContent,
                deleteResponse.StatusCode);
        }

        using var deletedGet = await client.SendAsync(
            Authorized(
                HttpMethod.Get,
                TemplatesPath + created.Id,
                writer.AccessToken),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, deletedGet.StatusCode);
    }

    private static async Task VerifyValidationAsync(
        HttpClient client,
        string token,
        CancellationToken cancellationToken)
    {
        using var invalid = await client.SendAsync(
            AuthorizedJson(
                HttpMethod.Post,
                TemplatesPath,
                token,
                new CreateCodeGenerationTemplateRequest(
                    "invalid",
                    null,
                    CreateSchema() with { DataScope = "unknown" })),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.AreEqual(
            CodeGenerationTemplateErrorCodes.Invalid,
            await ReadCodeAsync(invalid, cancellationToken));

        using var missing = await client.SendAsync(
            Authorized(
                HttpMethod.Get,
                TemplatesPath + Guid.CreateVersion7(),
                token),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static CodeGenerationPreviewRequest CreateSchema() =>
        new(
            "acme",
            "catalog",
            "product",
            "acme_catalog_product",
            "Acme.Modules.Catalog",
            "Product",
            "products",
            "products",
            "HostOnly",
            true,
            [
                new("Id", "Id", "id", "Uuid", false, null, null, null),
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

    private static HttpRequestMessage Authorized(
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token);
        return request;
    }

    private static HttpRequestMessage AuthorizedJson<T>(
        HttpMethod method,
        string path,
        string token,
        T body)
    {
        var request = Authorized(method, path, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<string?> ReadCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("code").GetString();
    }

    private static async Task<string> LoginAndEnterAcmeTenantAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var login = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest(
                    "admin",
                    FullNetApiFactory.TestPassword)),
        };
        login.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(
            login,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content
            .ReadFromJsonAsync<TokenResponse>(cancellationToken);
        Assert.IsNotNull(loginToken);

        using var available = Authorized(
            HttpMethod.Get,
            "/api/v1/tenancy/available",
            loginToken.AccessToken);
        using var availableResponse = await client.SendAsync(
            available,
            cancellationToken);
        var tenants = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(tenants);
        var acme = tenants.Single(tenant => tenant.Identifier == "acme");

        using var enter = AuthorizedJson(
            HttpMethod.Put,
            "/api/v1/tenancy/context",
            loginToken.AccessToken,
            new ChangeTenantContextRequest(acme.Id));
        using var enteredResponse = await client.SendAsync(
            enter,
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enteredResponse.StatusCode);
        var entered = await enteredResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(
                cancellationToken);
        Assert.IsNotNull(entered);
        return entered.AccessToken;
    }
}
