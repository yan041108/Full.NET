using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.IntegrationTests.Printing;

/// <summary>真实双库验证草稿/发布边界、根锁竞争、输入和旧系统导航迁移，不伪造数据绑定预览。</summary>
[TestClass]
public sealed class PrintingTemplateConfigurationTests
{
    private const string Templates = "/api/v1/printing/templates";
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Host_drafts_publish_immutable_versions_and_repair_only_owned_navigation(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(); using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, "admin"));
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        const string leaf = "ScopeKey='host' AND TenantId IS NULL AND RouteName='printing-preview' AND Path='/printing/preview' AND ComponentKey='printing-preview' AND MenuType='menu'";
        const string directory = "ScopeKey='host' AND TenantId IS NULL AND RouteName='module-printing' AND Path='/modules/printing' AND MenuType='directory' AND IsSystem=1";
        Assert.AreEqual(1, await database.ExecuteAsync("UPDATE fn_identity_navigation SET RequiredPermission='printing.templates.preview',IsSystem=0 WHERE " + leaf));
        Assert.AreEqual(1, await database.ExecuteAsync("UPDATE fn_identity_navigation SET RequiredPermission='printing.templates.preview' WHERE " + directory));
        var leafVersion = await database.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + leaf);
        var directoryVersion = await database.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + directory);
        await Send(host, HttpMethod.Post, "/api/v1/identity/menus/sync-catalog", null, HttpStatusCode.OK);
        Assert.AreEqual("printing.templates.preview", await database.ExecuteScalarAsync<string>("SELECT RequiredPermission FROM fn_identity_navigation WHERE " + leaf));
        Assert.AreEqual("printing.templates.read", await database.ExecuteScalarAsync<string>("SELECT RequiredPermission FROM fn_identity_navigation WHERE " + directory));
        await database.ExecuteAsync("UPDATE fn_identity_navigation SET IsSystem=1 WHERE " + leaf);
        await Send(host, HttpMethod.Post, "/api/v1/identity/menus/sync-catalog", null, HttpStatusCode.OK);
        await Send(host, HttpMethod.Post, "/api/v1/identity/menus/sync-catalog", null, HttpStatusCode.OK);
        Assert.AreEqual(leafVersion + 1, await database.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + leaf));
        Assert.AreEqual(directoryVersion + 1, await database.ExecuteScalarAsync<long>("SELECT Version FROM fn_identity_navigation WHERE " + directory));

        var name = "printing.reader." + Guid.CreateVersion7().ToString("N"); await factory.CreateHostIdentityAsync(name, ["identity.navigation.read", PrintingTemplatePermissions.Read], password: FullNetApiFactory.TestPassword);
        using var reader = factory.CreateClientForHost("localhost"); reader.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(reader, name));
        Assert.IsTrue((await Send(reader, HttpMethod.Get, "/api/v1/navigation", null, HttpStatusCode.OK)).GetRawText().Contains("printing-preview", StringComparison.Ordinal));
        var key = "printing-test-" + Guid.CreateVersion7().ToString("N");
        CreatePrintingTemplateRequest Request(string code, string title) => new(code, title, PrintingFormSchemaKeys.TenantProfileCard, "<h1>First {{tenantName}}</h1>", true);
        await Send(reader, HttpMethod.Post, Templates, Request(key, "Denied"), HttpStatusCode.Forbidden);
        host.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");
        var invalid = await Send(host, HttpMethod.Post, Templates, Request(key, new string('名', 129)), HttpStatusCode.BadRequest);
        Assert.AreEqual(PrintingErrorCodes.TemplateInvalid, invalid.GetProperty("code").GetString()); Assert.IsTrue(invalid.GetProperty("title").GetString()!.Contains("模板参数", StringComparison.Ordinal));
        await Send(host, HttpMethod.Post, Templates, Request("中文机器码", "Invalid key"), HttpStatusCode.BadRequest);
        await Send(host, HttpMethod.Post, Templates, Request(new string('a', 129), "Long key"), HttpStatusCode.BadRequest);
        var draft = await Send(host, HttpMethod.Post, Templates, Request(key, new string('名', 128)), HttpStatusCode.Created);
        var id = draft.GetProperty("id").GetGuid(); var path = Templates + "/" + id.ToString("D"); var original = draft.GetProperty("layoutHtml").GetString();
        Assert.AreEqual(0, draft.GetProperty("latestPublishedVersionNumber").GetInt32()); Assert.AreEqual(1, draft.GetProperty("version").GetInt32());
        await Send(host, HttpMethod.Post, Templates, Request(key, "Duplicate"), HttpStatusCode.Conflict);
        await Send(reader, HttpMethod.Get, path, null, HttpStatusCode.OK);
        await Send(reader, HttpMethod.Put, path, new UpdatePrintingTemplateRequest("Denied", "<p>No</p>", true, 1), HttpStatusCode.Forbidden);
        await Send(reader, HttpMethod.Post, path + "/publish", new PublishPrintingTemplateRequest(null, 1), HttpStatusCode.Forbidden);
        await Send(host, HttpMethod.Put, path, new UpdatePrintingTemplateRequest(" ", "<p>No</p>", true, 1), HttpStatusCode.BadRequest);
        await Send(host, HttpMethod.Put, path, new UpdatePrintingTemplateRequest(new string('n', 129), "<p>No</p>", true, 1), HttpStatusCode.BadRequest);
        await Send(host, HttpMethod.Post, path + "/publish", new PublishPrintingTemplateRequest(new string('n', 513), 1), HttpStatusCode.BadRequest);
        var first = await Send(host, HttpMethod.Post, path + "/publish", new PublishPrintingTemplateRequest("First", 1), HttpStatusCode.OK);
        Assert.AreEqual(1, first.GetProperty("versionNumber").GetInt32()); Assert.AreEqual(original, first.GetProperty("layoutHtml").GetString());
        var updated = await Send(host, HttpMethod.Put, path, new UpdatePrintingTemplateRequest("Changed", "<h1>Second</h1>", true, 2), HttpStatusCode.OK);
        Assert.AreEqual(3, updated.GetProperty("version").GetInt32()); Assert.AreEqual(1, updated.GetProperty("latestPublishedVersionNumber").GetInt32());
        Assert.AreEqual(original, (await Send(host, HttpMethod.Get, path + "/versions/1", null, HttpStatusCode.OK)).GetProperty("layoutHtml").GetString());
        await Send(host, HttpMethod.Post, path + "/publish", new PublishPrintingTemplateRequest(null, 2), HttpStatusCode.Conflict);
        // 同一并发版本只可发布一次；另一请求在根锁后看到新版本，不留下孤立版本行。
        var attempts = await Task.WhenAll(host.PostAsJsonAsync(path + "/publish", new PublishPrintingTemplateRequest("Race one", 3)), host.PostAsJsonAsync(path + "/publish", new PublishPrintingTemplateRequest("Race two", 3)));
        try { CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, attempts.Select(response => response.StatusCode).ToArray()); }
        finally { foreach (var response in attempts) response.Dispose(); }
        var history = await Send(host, HttpMethod.Get, path + "/versions?page=1&pageSize=1", null, HttpStatusCode.OK);
        Assert.AreEqual(2, history.GetProperty("total").GetInt32()); Assert.AreEqual(1, history.GetProperty("items").GetArrayLength()); Assert.IsFalse(history.GetProperty("items")[0].TryGetProperty("layoutHtml", out _));
        Assert.AreEqual(2, (await Send(host, HttpMethod.Get, path, null, HttpStatusCode.OK)).GetProperty("latestPublishedVersionNumber").GetInt32());
        host.DefaultRequestHeaders.AcceptLanguage.Clear(); host.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        invalid = await Send(host, HttpMethod.Put, path, new UpdatePrintingTemplateRequest(" ", "<p>No</p>", true, 4), HttpStatusCode.BadRequest);
        Assert.IsTrue(invalid.GetProperty("title").GetString()!.Contains("Invalid template", StringComparison.Ordinal));
        var hostToken = host.DefaultRequestHeaders.Authorization!.Parameter!;
        var tenants = await Send(host, HttpMethod.Get, "/api/v1/tenancy/available", null, HttpStatusCode.OK);
        var tenantId = tenants.EnumerateArray().Single(tenant => tenant.GetProperty("identifier").GetString() == "acme").GetProperty("id").GetGuid();
        host.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, hostToken, tenantId));
        await Send(host, HttpMethod.Get, Templates, null, HttpStatusCode.Forbidden);
        await Send(host, HttpMethod.Put, path, new UpdatePrintingTemplateRequest("Tenant cannot edit", "<p>No</p>", true, 4), HttpStatusCode.Forbidden);
    }

    private static async Task<JsonElement> Send(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request); var json = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + " " + json);
        using var document = JsonDocument.Parse(json); return document.RootElement.Clone();
    }
}
