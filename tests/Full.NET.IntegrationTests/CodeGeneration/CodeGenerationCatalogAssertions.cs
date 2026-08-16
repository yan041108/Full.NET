using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>
/// 验收 Host 数据库目录的授权、只读基础表扫描与列同步保留人工 UI。
/// </summary>
internal static class CodeGenerationCatalogAssertions
{
    private const string CatalogPath = "/api/v1/code-generation/catalog/";

    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");

        using (var anonymous = await client.GetAsync(
                   CatalogPath + "tables",
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        var wrongToken = await factory.CreateHostAccessTokenAsync(
            [CodeGenerationTemplatePermissions.Read],
            cancellationToken);
        using (var forbidden = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       CatalogPath + "tables",
                       wrongToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var reader = await factory.CreateHostIdentityAsync(
            $"codegen-catalog-{Guid.NewGuid():N}",
            [CodeGenerationCatalogPermissions.Read],
            cancellationToken);
        var tenantToken = await LoginAndEnterAcmeTenantAsync(
            client,
            cancellationToken);
        using (var tenantDenied = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       CatalogPath + "tables",
                       tenantToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, tenantDenied.StatusCode);
        }

        IReadOnlyList<CodeGenerationCatalogTableResponse> tables;
        using (var listResponse = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       CatalogPath + "tables",
                       reader.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, listResponse.StatusCode);
            tables = (await listResponse.Content.ReadFromJsonAsync<
                CodeGenerationCatalogTableResponse[]>(
                cancellationToken))!;
            Assert.IsNotNull(tables);
            Assert.IsTrue(tables.Any(table =>
                string.Equals(
                    table.TableName,
                    "fn_codegeneration_template",
                    StringComparison.Ordinal)));
            Assert.IsFalse(tables.Any(table =>
                table.TableName.Contains('.', StringComparison.Ordinal)
                || table.TableName.Contains(' ', StringComparison.Ordinal)));
            CollectionAssert.AreEqual(
                tables.Select(table => table.TableName).ToArray(),
                tables.Select(table => table.TableName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray());
        }

        using (var missing = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       CatalogPath + "tables/missing_table/columns",
                       reader.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using (var invalid = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       CatalogPath + "tables/1invalid/columns",
                       reader.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        CodeGenerationCatalogColumnListResponse columns;
        using (var columnResponse = await client.SendAsync(
                   Authorized(
                       HttpMethod.Get,
                       CatalogPath
                           + "tables/fn_codegeneration_template/columns",
                       reader.AccessToken),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, columnResponse.StatusCode);
            columns = (await columnResponse.Content.ReadFromJsonAsync<
                CodeGenerationCatalogColumnListResponse>(
                cancellationToken))!;
            Assert.IsNotNull(columns);
            Assert.AreEqual("fn_codegeneration_template", columns.TableName);
            Assert.IsTrue(columns.Columns.Any(column =>
                column.DatabaseName == "Name" && column.Ui is not null));
            Assert.IsTrue(columns.Columns.Any(column =>
                column.DatabaseName == "Id"
                && column.Ui is { IncludeInCreate: false }));
        }

        var keptUi = new CodeGenerationPreviewColumnUiRequest(
            "textarea",
            true,
            true,
            true,
            true,
            true,
            true,
            "contains",
            false,
            true);
        var nameColumn = columns.Columns.Single(column =>
            column.DatabaseName == "Name");
        using (var syncResponse = await client.SendAsync(
                   AuthorizedJson(
                       HttpMethod.Post,
                       CatalogPath + "column-sync",
                       reader.AccessToken,
                       new CodeGenerationCatalogColumnSyncRequest(
                           "fn_codegeneration_template",
                           [nameColumn with { Ui = keptUi }])),
                   cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, syncResponse.StatusCode);
            var synced = await syncResponse.Content.ReadFromJsonAsync<
                CodeGenerationCatalogColumnSyncResponse>(
                cancellationToken);
            Assert.IsNotNull(synced);
            Assert.IsTrue(synced.AddedColumnNames.Contains("Id"));
            Assert.AreEqual(
                0,
                synced.RemovedColumnNames.Count);
            var syncedName = synced.Columns.Single(column =>
                column.DatabaseName == "Name");
            Assert.AreEqual("textarea", syncedName.Ui!.ControlKind);
        }

        await OpenApiCodeGenerationCatalogContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

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
