using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;

namespace Full.NET.IntegrationTests.Document;

/// <summary>六状态分页保留文档条件、Host 和原文档 ACL，超级管理员也不能读到受限来源。</summary>
[TestClass]
public sealed class DocumentPreviewStatusFilterApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Status_counts_pages_and_original_document_access_match(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync(); using var client = factory.CreateClientForHost("localhost");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, "admin"));
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7(); var restricted = Guid.CreateVersion7(); var tenantOnly = Guid.CreateVersion7();
        var otherUser = Guid.CreateVersion7(); var now = DateTime.UtcNow;
        foreach (var item in new[] { first, second, restricted, tenantOnly })
            await database.ExecuteAsync("""
                INSERT INTO fn_document_item (Id,TenantId,Title,CreatedAtUtc,CreatedByUserId,Version,Status,VisibilityMode)
                VALUES (@Id,@TenantId,'Preview status fixture',@Now,@Owner,1,1,@Visibility)
                """, new { Id = item, TenantId = item == tenantOnly ? Guid.CreateVersion7() : (Guid?)null, Now = now, Owner = otherUser, Visibility = item == restricted ? "restricted" : "host" });
        var visibleIds = new HashSet<Guid>(); var hiddenIds = new HashSet<Guid>();
        foreach (var state in HostDocumentPreviewTaskStatusKeys.All)
        {
            for (var row = 0; row < 5; row++)
            {
                var id = Guid.CreateVersion7(); if (row < 3) visibleIds.Add(id); else hiddenIds.Add(id);
                await database.ExecuteAsync("""
                    INSERT INTO fn_document_preview_task
                    (Id,DocumentItemId,DocumentTitle,SourceFileId,StatusKey,ProviderKey,RequestedByUserId,CreatedAtUtc,Version)
                    VALUES (@Id,@Document,'Preview status fixture',@Source,@Status,'disabled',@Owner,@Now,1)
                    """, new { Id = id, Document = row < 2 ? first : row == 2 ? second : row == 3 ? restricted : tenantOnly, Source = Guid.CreateVersion7(), Status = state, Owner = otherUser, Now = now.AddSeconds(row) });
            }
            var pageOne = await Read($"?statusKey={state}&pageSize=2"); var pageTwo = await Read($"?statusKey={state}&pageSize=2&page=2");
            Assert.AreEqual(3L, pageOne.Total); Assert.AreEqual(3L, pageTwo.Total); Assert.AreEqual(2, pageOne.Items.Count); Assert.AreEqual(1, pageTwo.Items.Count);
            var rows = pageOne.Items.Concat(pageTwo.Items).ToArray(); Assert.AreEqual(3, rows.Select(row => row.Id).Distinct().Count());
            Assert.IsTrue(rows.All(row => row.StatusKey == state && visibleIds.Contains(row.Id) && !hiddenIds.Contains(row.Id) && row.RequestedByUserId == otherUser));
            Assert.AreEqual(2L, (await Read($"?statusKey={state}&documentItemId={first:D}")).Total);
            Assert.AreEqual(0L, (await Read($"?statusKey={state}&documentItemId={restricted:D}")).Total);
            Assert.AreEqual(0L, (await Read($"?statusKey={state}&documentItemId={tenantOnly:D}")).Total);
        }
        Assert.AreEqual(18L, (await Read("?pageSize=100")).Total); Assert.AreEqual(18L, (await Read("?statusKey=%20%20")).Total);
        Assert.AreEqual(3L, (await Read("?statusKey=%20succeeded%20")).Total);
        foreach (var value in new[] { "unknown", "SUCCEEDED", "failed%27%20OR%201=1" })
        {
            using var response = await client.GetAsync("/api/v1/document/host/preview-tasks?statusKey=" + value); Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.AreEqual(CommonErrorCodes.RequestInvalid, problem.RootElement.GetProperty("code").GetString());
        }
        using var anonymous = factory.CreateClientForHost("localhost"); using var denied = await anonymous.GetAsync("/api/v1/document/host/preview-tasks?statusKey=failed"); Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);

        async Task<PagedResult<HostDocumentPreviewTaskResponse>> Read(string query)
        {
            using var response = await client.GetAsync("/api/v1/document/host/preview-tasks" + query); response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<PagedResult<HostDocumentPreviewTaskResponse>>())!;
        }
    }
}
