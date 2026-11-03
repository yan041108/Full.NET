using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Api;

/// <summary>两个所有者端点在过滤和分页前隔离身份、对象权限与固定近期窗口。</summary>
[TestClass]
public sealed class ObjectSearchApiTests
{
    private const string Documents = "/api/v1/document/host/items";
    private const string Requests = "/api/v1/enterprise_request/enterprise-requests";

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Recent_object_search_keeps_authorization_literal_text_and_bounded_pages(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, includeEnterpriseRequest: true); await factory.InitializeAsync();
        using var administrator = factory.CreateClientForHost("localhost");
        administrator.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(administrator, "admin"));
        using var anonymous = factory.CreateClientForHost("localhost");
        await SendAsync(anonymous, HttpMethod.Get, Documents + "/search?q=Find", null, HttpStatusCode.Unauthorized);
        await SendAsync(administrator, HttpMethod.Get, Requests + "/search?q=Find", null, HttpStatusCode.Forbidden);
        var query = "Find_%[~" + Guid.CreateVersion7().ToString("N")[..8]; var ids = new List<Guid>();
        for (var index = 0; index < 8; index++)
        {
            var created = await SendAsync(administrator, HttpMethod.Post, Documents, new CreateHostDocumentItemRequest(query + " " + index, "Hidden body"), HttpStatusCode.Created);
            ids.Add(created.GetProperty("id").GetGuid());
        }
        var old = await SendAsync(administrator, HttpMethod.Post, Documents, new CreateHostDocumentItemRequest(query + " old", "Hidden old body"), HttpStatusCode.Created);
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        await database.ExecuteAsync("UPDATE fn_document_item SET CreatedAtUtc=@Old WHERE Id=@Id", new { Id = old.GetProperty("id").GetGuid(), Old = DateTime.UtcNow.AddDays(-100) });
        await SendAsync(administrator, HttpMethod.Post, Documents, new CreateHostDocumentItemRequest("Find ordinary " + query[8..], "Ordinary"), HttpStatusCode.Created);
        var first = await SearchAsync(administrator, Documents, query); Assert.AreEqual(5, first.GetProperty("items").GetArrayLength()); Assert.IsTrue(first.GetProperty("hasMore").GetBoolean());
        var second = await SearchAsync(administrator, Documents, query, 2); Assert.AreEqual(3, second.GetProperty("items").GetArrayLength()); Assert.IsFalse(second.GetProperty("hasMore").GetBoolean());
        var seen = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.AreEqual(8, seen.Distinct().Count()); Assert.IsFalse(seen.Contains(old.GetProperty("id").GetGuid()));
        foreach (var hit in first.GetProperty("items").EnumerateArray()) CollectionAssert.AreEquivalent(new[] { "id", "title", "number" }, hit.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(0, (await SearchAsync(administrator, Documents, query.ToLowerInvariant())).GetProperty("items").GetArrayLength());
        foreach (var invalid in new[] { "?q=x", "?q=" + new string('x', 65), "?q=Find&page=0", "?q=Find&page=21", "?q=Find&q=other", "?q=Find&tenantId=" + Guid.CreateVersion7(), "?q=Find%0Ahidden" })
            await SendAsync(administrator, HttpMethod.Get, Documents + "/search" + invalid, null, HttpStatusCode.BadRequest);

        var reader = await factory.CreateHostIdentityAsync("search.reader." + Guid.CreateVersion7().ToString("N"), [HostDocumentPermissions.Read]);
        using var restricted = factory.CreateClientForHost("localhost"); restricted.DefaultRequestHeaders.Authorization = new("Bearer", reader.AccessToken);
        Assert.AreEqual(0, (await SearchAsync(restricted, Documents, query)).GetProperty("items").GetArrayLength());
        await SendAsync(administrator, HttpMethod.Post, "/api/v1/document/host/permissions", new SetHostDocumentPermissionsRequest(ids[0], [new(reader.UserId, ["read"])], "restricted", 1), HttpStatusCode.OK);
        var readable = await SearchAsync(restricted, Documents, query); Assert.AreEqual(1, readable.GetProperty("items").GetArrayLength());
        Assert.AreEqual(ids[0], readable.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.AreEqual(0, (await SearchAsync(administrator, Documents, query + " 0")).GetProperty("items").GetArrayLength(), "超级管理员的搜索不能绕过文档 restricted ACL。");
        await SendAsync(administrator, HttpMethod.Post, "/api/v1/document/host/permissions", new SetHostDocumentPermissionsRequest(ids[0], [], "restricted", 2), HttpStatusCode.OK);
        Assert.AreEqual(0, (await SearchAsync(restricted, Documents, query)).GetProperty("items").GetArrayLength());
        await SendAsync(restricted, HttpMethod.Get, Documents + "/" + ids[0].ToString("D"), null, HttpStatusCode.Forbidden);
        var denied = await factory.CreateHostIdentityAsync("search.denied." + Guid.CreateVersion7().ToString("N"), []);
        using var noPermission = factory.CreateClientForHost("localhost"); noPermission.DefaultRequestHeaders.Authorization = new("Bearer", denied.AccessToken);
        await SendAsync(noPermission, HttpMethod.Get, Documents + "/search?q=Find", null, HttpStatusCode.Forbidden);

        var tenants = (await administrator.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!;
        administrator.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(administrator,
            administrator.DefaultRequestHeaders.Authorization!.Parameter!, tenants.Single(item => item.Identifier == "acme").Id));
        await SendAsync(administrator, HttpMethod.Get, Documents + "/search?q=Find", null, HttpStatusCode.Forbidden);
        var token = administrator.DefaultRequestHeaders.Authorization!.Parameter!;
        var unit = await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(administrator, token, default);
        var actor = await EnterpriseRequestAssertions.GetCurrentUserIdAsync(administrator, token, default);
        for (var index = 0; index < 8; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Requests) { Content = JsonContent.Create(new {
                requestNumber = "SEARCH-" + Guid.CreateVersion7().ToString("N"), title = query + " " + index, status = "Draft", totalAmount = "0.00", applicantUserId = actor }) };
            request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unit.ToString("D")); using var response = await administrator.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        first = await SearchAsync(administrator, Requests, query); second = await SearchAsync(administrator, Requests, query, 2);
        Assert.AreEqual(5, first.GetProperty("items").GetArrayLength()); Assert.AreEqual(3, second.GetProperty("items").GetArrayLength());
        Assert.IsFalse(first.TryGetProperty("total", out _)); Assert.IsFalse(second.GetProperty("hasMore").GetBoolean());
        foreach (var invalid in new[] { "?q=x", "?q=Find&page=21", "?q=Find&q=other", "?q=Find&tenantId=" + Guid.CreateVersion7() })
            await SendAsync(administrator, HttpMethod.Get, Requests + "/search" + invalid, null, HttpStatusCode.BadRequest);
        if (await ObjectSearchQueryEvidence.CaptureAsync(database, provider, connection, actor, tenants.Single(item => item.Identifier == "acme").Id, unit))
        {
            Assert.IsTrue((await SearchAsync(administrator, Requests, "BudgetFind", 20)).GetProperty("isTruncated").GetBoolean());
            var host = await SendAsync(administrator, HttpMethod.Put, "/api/v1/tenancy/context", new ChangeTenantContextRequest(null), HttpStatusCode.OK);
            administrator.DefaultRequestHeaders.Authorization = new("Bearer", host.GetProperty("accessToken").GetString());
            var capped = await SearchAsync(administrator, Documents, "BudgetFind", 20);
            Assert.IsTrue(capped.GetProperty("isTruncated").GetBoolean()); Assert.IsFalse(capped.GetProperty("hasMore").GetBoolean());
        }
    }

    private static Task<JsonElement> SearchAsync(HttpClient client, string path, string query, int page = 1) =>
        SendAsync(client, HttpMethod.Get, path + "/search?q=" + Uri.EscapeDataString(query) + "&page=" + page, null, HttpStatusCode.OK);
    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request); var text = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + " " + text);
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
