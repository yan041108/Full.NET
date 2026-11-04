using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.ImportExport;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Printing;

/// <summary>正式双库 HTTP 验证发布冻结、当前租户绑定、精确授权、撤权和停用，不替换跨模块 Port。</summary>
[TestClass]
public sealed class PrintingTenantPublishedApiTests
{
    [TestMethod]
    public Task MySql_granted_versions_bind_only_current_tenant_and_revoke() => VerifyAsync(DatabaseProvider.MySql);

    [TestMethod]
    public Task SqlServer_granted_versions_bind_only_current_tenant_and_revoke() => VerifyAsync(DatabaseProvider.SqlServer);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.MySql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        using var api = new FullNetApiFactory(provider, cs);
        await api.InitializeAsync(); using var client = api.CreateClientForHost("localhost");
        var tenantToken = await ImportExportTaskAssertions.LoginAndEnterAcmeTenantAsync(client, default);
        var tenantId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(tenantToken).Claims
            .Single(c => c.Type == FullNetIdentityClaimTypes.TenantId).Value);
        var hostToken = await api.CreateHostAccessTokenAsync([PrintingTemplatePermissions.Create, PrintingTemplatePermissions.Read,
            PrintingTemplatePermissions.Update, PrintingTemplatePermissions.Publish, PrintingTemplatePermissions.GrantTenants, TenancyTenantManagementPermissions.Create]);
        var readOnlyHost = await api.CreateHostAccessTokenAsync([PrintingTemplatePermissions.Read]);
        Guid otherTenant;
        // 默认测试 Overlay 只有 Acme；第二租户通过正式开通入口创建，不依赖隐含种子。
        var otherCode = "print-" + Guid.CreateVersion7().ToString("N");
        using (var createdTenant = await SendAsync(HttpMethod.Post, "/api/v1/tenancy/tenants", hostToken,
            new ProvisionTenantRequest(otherCode, "Other printing tenant", otherCode + ".localhost")))
        {
            Assert.AreEqual(HttpStatusCode.Created, createdTenant.StatusCode, await createdTenant.Content.ReadAsStringAsync());
            otherTenant = (await createdTenant.Content.ReadFromJsonAsync<TenantSummary>())!.Id;
        }
        const string catalog = "/api/v1/printing/published-templates";
        using (var empty = await SendAsync(HttpMethod.Get, catalog, tenantToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, empty.StatusCode);
            Assert.AreEqual(0, (await empty.Content.ReadFromJsonAsync<PrintingPublishedTemplateResponse[]>())!.Length);
        }
        using (var anonymous = await client.GetAsync(catalog)) Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using (var host = await SendAsync(HttpMethod.Get, catalog, hostToken)) Assert.AreEqual(HttpStatusCode.Forbidden, host.StatusCode);
        PrintingTemplateResponse draft;
        using (var created = await SendAsync(HttpMethod.Post, "/api/v1/printing/templates", hostToken,
            new CreatePrintingTemplateRequest("tenant-print-" + Guid.CreateVersion7().ToString("N"), "租户档案", PrintingFormSchemaKeys.TenantProfileCard,
                "<section>v1:{{tenantName}}/{{tenantCode}}</section>", true)))
        {
            Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
            draft = (await created.Content.ReadFromJsonAsync<PrintingTemplateResponse>())!;
        }
        var path = $"/api/v1/printing/templates/{draft.Id:D}";
        await StatusAsync(HttpMethod.Post, path + "/publish", hostToken, HttpStatusCode.OK, new PublishPrintingTemplateRequest("v1", draft.Version));
        draft = await DraftAsync();
        await StatusAsync(HttpMethod.Put, path, hostToken, HttpStatusCode.OK,
            new UpdatePrintingTemplateRequest(draft.Name, "<section>v2:{{tenantName}}</section>", true, draft.Version));
        draft = await DraftAsync();
        await StatusAsync(HttpMethod.Post, path + "/publish", hostToken, HttpStatusCode.OK, new PublishPrintingTemplateRequest("v2", draft.Version));
        var preview = $"{catalog}/{draft.Id:D}/preview";
        var grants = path + "/versions/1/tenant-grants";
        var grant = grants + $"/{tenantId:D}";
        await StatusAsync(HttpMethod.Get, path, tenantToken, HttpStatusCode.Forbidden);
        await StatusAsync(HttpMethod.Post, path + "/preview", tenantToken, HttpStatusCode.Forbidden, new PreviewPrintingTemplateRequest(1));
        await StatusAsync(HttpMethod.Post, preview, hostToken, HttpStatusCode.Forbidden, new PreviewPrintingTemplateRequest(1));
        await StatusAsync(HttpMethod.Post, preview, tenantToken, HttpStatusCode.Forbidden, new PreviewPrintingTemplateRequest(null));
        await StatusAsync(HttpMethod.Put, grant, tenantToken, HttpStatusCode.Forbidden);
        await StatusAsync(HttpMethod.Put, grant, readOnlyHost, HttpStatusCode.Forbidden);
        await StatusAsync(HttpMethod.Put, grant, hostToken, HttpStatusCode.OK);
        await StatusAsync(HttpMethod.Put, grant, hostToken, HttpStatusCode.OK);
        using (var list = await SendAsync(HttpMethod.Get, grants + "?page=1&pageSize=1", hostToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
            var page = (await list.Content.ReadFromJsonAsync<PagedResult<Guid>>())!;
            Assert.AreEqual(1L, page.Total); CollectionAssert.AreEqual(new[] { tenantId }, page.Items.ToArray());
        }
        using (var list = await SendAsync(HttpMethod.Get, grants + "?page=2&pageSize=1", hostToken))
            Assert.AreEqual(0, (await list.Content.ReadFromJsonAsync<PagedResult<Guid>>())!.Items.Count);
        await StatusAsync(HttpMethod.Get, grants + "?page=0", hostToken, HttpStatusCode.BadRequest);
        await StatusAsync(HttpMethod.Get, grants + "?pageSize=201", hostToken, HttpStatusCode.BadRequest);
        await StatusAsync(HttpMethod.Put, path + $"/versions/2/tenant-grants/{otherTenant:D}", hostToken, HttpStatusCode.OK);
        using (var list = await SendAsync(HttpMethod.Get, catalog, tenantToken))
        {
            var rows = (await list.Content.ReadFromJsonAsync<PrintingPublishedTemplateResponse[]>())!;
            Assert.AreEqual(1, rows.Length); Assert.AreEqual(1, rows[0].VersionNumber); Assert.AreEqual(draft.Id, rows[0].TemplateId);
            Assert.IsFalse((await list.Content.ReadAsStringAsync()).Contains("layoutHtml", StringComparison.OrdinalIgnoreCase));
        }
        using (var rendered = await SendAsync(HttpMethod.Post, preview, tenantToken, new { versionNumber = (int?)null, tenantId = otherTenant }))
        {
            Assert.AreEqual(HttpStatusCode.OK, rendered.StatusCode, await rendered.Content.ReadAsStringAsync());
            var value = (await rendered.Content.ReadFromJsonAsync<PrintingTemplatePreviewResponse>())!;
            Assert.AreEqual(1, value.VersionNumber); Assert.AreEqual("acme", value.BoundFields["tenantCode"]);
            Assert.AreEqual($"<section>v1:{WebUtility.HtmlEncode(value.BoundFields["tenantName"])}/acme</section>", value.Html);
            Assert.IsFalse(value.Html.Contains("v2:", StringComparison.Ordinal));
        }
        await StatusAsync(HttpMethod.Post, preview, tenantToken, HttpStatusCode.Forbidden, new PreviewPrintingTemplateRequest(2));
        await StatusAsync(HttpMethod.Post, preview, tenantToken, HttpStatusCode.Forbidden, new PreviewPrintingTemplateRequest(0));
        await StatusAsync(HttpMethod.Delete, grant, hostToken, HttpStatusCode.OK);
        await StatusAsync(HttpMethod.Delete, grant, hostToken, HttpStatusCode.OK);
        await AssertHiddenAsync();
        await StatusAsync(HttpMethod.Put, grant, hostToken, HttpStatusCode.OK);
        draft = await DraftAsync();
        await StatusAsync(HttpMethod.Put, path, hostToken, HttpStatusCode.OK,
            new UpdatePrintingTemplateRequest(draft.Name, draft.LayoutHtml, false, draft.Version));
        await AssertHiddenAsync();
        await StatusAsync(HttpMethod.Put, grant, hostToken, HttpStatusCode.Forbidden);
        await StatusAsync(HttpMethod.Delete, grant, hostToken, HttpStatusCode.OK);

        async Task AssertHiddenAsync()
        {
            using var hidden = await SendAsync(HttpMethod.Get, catalog, tenantToken);
            Assert.AreEqual(HttpStatusCode.OK, hidden.StatusCode);
            Assert.AreEqual(0, (await hidden.Content.ReadFromJsonAsync<PrintingPublishedTemplateResponse[]>())!.Length);
            await StatusAsync(HttpMethod.Post, preview, tenantToken, HttpStatusCode.Forbidden, new PreviewPrintingTemplateRequest(null));
        }
        async Task<PrintingTemplateResponse> DraftAsync()
        {
            using var response = await SendAsync(HttpMethod.Get, path, hostToken);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<PrintingTemplateResponse>())!;
        }
        async Task StatusAsync(HttpMethod method, string url, string token, HttpStatusCode expected, object? body = null)
        {
            using var response = await SendAsync(method, url, token, body);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request);
        }
    }
}
