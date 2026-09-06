using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.IntegrationTests.ImportExport;

/// <summary>ImportExport 静态 Schema 导入任务纵向切片验收夹具。</summary>
internal static class ImportExportTaskAssertions
{
    private const string TasksPath = "/api/v1/import-export/tasks";
    private const string SchemasPath = "/api/v1/import-export/schemas";

    public static async Task VerifyImportTaskPreviewContractAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var tenantToken = await LoginAndEnterAcmeTenantAsync(client, cancellationToken);

        using var schemasRequest = new HttpRequestMessage(HttpMethod.Get, SchemasPath);
        schemasRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        using var schemasResponse = await client.SendAsync(schemasRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, schemasResponse.StatusCode);
        var schemas = await schemasResponse.Content
            .ReadFromJsonAsync<StaticImportSchemaDefinition[]>(cancellationToken);
        Assert.IsNotNull(schemas);
        Assert.IsTrue(schemas.Any(schema =>
            schema.SchemaKey == StaticImportSchemaKeys.OrganizationTenantPositions));

        var templatePath =
            $"/api/v1/import-export/schemas/{StaticImportSchemaKeys.OrganizationTenantPositions}"
            + "/worksheets/positions/template";
        using var templateRequest = new HttpRequestMessage(HttpMethod.Get, templatePath);
        templateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        using var templateResponse = await client.SendAsync(templateRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, templateResponse.StatusCode);
        var templateBytes = await templateResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.IsTrue(templateBytes.Length > 0);

        using var createContent = new MultipartFormDataContent
        {
            { new StringContent(StaticImportSchemaKeys.OrganizationTenantPositions), "schemaKey" },
            { new StringContent("positions"), "worksheetKey" },
        };
        var fileContent = new ByteArrayContent(templateBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        createContent.Add(fileContent, "file", "tenant-positions-import.xlsx");

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, TasksPath)
        {
            Content = createContent,
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content
            .ReadFromJsonAsync<ImportExportTaskDetailResponse>(cancellationToken);
        Assert.IsNotNull(created);
        Assert.AreEqual(ImportExportTaskStatusKeys.PreviewSucceeded, created.StatusKey);
        Assert.AreEqual(StaticImportSchemaKeys.OrganizationTenantPositions, created.SchemaKey);
        Assert.AreEqual("positions", created.WorksheetKey);

        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{TasksPath}/{created.Id:D}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        using var getResponse = await client.SendAsync(getRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, getResponse.StatusCode);
        var loaded = await getResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>(
            cancellationToken);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(ImportExportTaskStatusKeys.PreviewSucceeded, loaded.StatusKey);

        using var executeRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{TasksPath}/{created.Id:D}/execute");
        executeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        using var executeResponse = await client.SendAsync(executeRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, executeResponse.StatusCode);
        var executed = await executeResponse.Content.ReadFromJsonAsync<ImportExportTaskDetailResponse>(
            cancellationToken);
        Assert.IsNotNull(executed);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, executed.StatusKey);
        Assert.AreEqual(created.ValidRowCount, executed.SucceededRowCount);
        Assert.IsTrue(executed.ProcessedRowCount >= created.ValidRowCount);

        await OpenApiImportExportContractAssertions.VerifyAsync(client, cancellationToken);
    }

    private static async Task<string> LoginAndEnterAcmeTenantAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new LoginRequest("admin", FullNetApiFactory.TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginToken = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken);
        Assert.IsNotNull(loginToken);

        using var availableRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/tenancy/available");
        availableRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            loginToken.AccessToken);
        using var availableResponse = await client.SendAsync(availableRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, availableResponse.StatusCode);
        var available = await availableResponse.Content
            .ReadFromJsonAsync<TenantContextSummary[]>(cancellationToken);
        Assert.IsNotNull(available);
        var acme = available.Single(tenant => tenant.Identifier == "acme");

        using var enterRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/tenancy/context")
        {
            Content = JsonContent.Create(new ChangeTenantContextRequest(acme.Id)),
        };
        enterRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            loginToken.AccessToken);
        using var enterResponse = await client.SendAsync(enterRequest, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, enterResponse.StatusCode);
        var entered = await enterResponse.Content
            .ReadFromJsonAsync<TenantContextTokenResponse>(cancellationToken);
        Assert.IsNotNull(entered);
        return entered.AccessToken;
    }
}
