using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Modules.Document.Contracts;
using Dapper;
using System.Data;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Document;

/// <summary>同一分类层级回归用于双库 JIT 与独立 Native API，避免两条链路使用不同断言。</summary>
internal static class DocumentCategoryHierarchyAssertions
{
    private const string Path = "/api/v1/document/host/categories";

    public static async Task VerifyWaitingDeleteAsync(FullNetApiFactory factory, HttpClient client, string token, CancellationToken cancellationToken)
    {
        if (factory.Provider != DatabaseProvider.MySql) return;
        var parent = await CreateAsync(client, token, null, cancellationToken);
        var connectionString = MySqlConnectionStringPolicy.Create(factory.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false);
        await using var holder = new MySqlConnection(connectionString);
        // 仅观测连接使用夹具管理员；应用账号保持原权限，不为验收授予 PROCESS。
        var observerOptions = new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync())
        {
            Database = new MySqlConnectionStringBuilder(connectionString).Database,
        };
        await using var observer = new MySqlConnection(observerOptions.ConnectionString);
        await holder.OpenAsync(cancellationToken);
        await observer.OpenAsync(cancellationToken);
        await using var transaction = await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await holder.ExecuteAsync(new CommandDefinition("UPDATE fn_document_category SET Version = Version WHERE Id = @Id",
            new { parent.Id }, transaction, cancellationToken: cancellationToken));
        using var request = Request(HttpMethod.Post, $"{Path}/{parent.Id:D}/delete", token, new DeleteHostDocumentCategoryRequest(parent.Version));
        var deleting = client.SendAsync(request, cancellationToken);
        var committed = false;
        try
        {
            // 使用实际数据库锁等待作屏障，不能靠固定延迟猜测删除请求已进入扫描。
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            var waiting = false;
            while (!waiting && DateTimeOffset.UtcNow < deadline)
            {
                var count = await observer.QuerySingleAsync<long>(new CommandDefinition("""
                    SELECT COUNT(*) FROM information_schema.innodb_trx t
                    JOIN information_schema.PROCESSLIST p ON p.ID = t.trx_mysql_thread_id
                    WHERE t.trx_state = 'LOCK WAIT' AND p.DB = DATABASE()
                      AND t.trx_query LIKE '%fn_document_category%'
                    """, cancellationToken: cancellationToken));
                waiting = count > 0;
                if (!waiting) await Task.Delay(50, cancellationToken);
            }
            Assert.IsTrue(waiting, "Delete must be waiting on the category row before the child is inserted.");
            var childId = Guid.Parse("00000001-0000-7000-8000-" + Guid.NewGuid().ToString("N")[..12]);
            await holder.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_document_category (Id, TenantId, ParentId, Name, CreatedAtUtc, Version)
                VALUES (@Id, NULL, @ParentId, @Name, @CreatedAtUtc, 1)
                """, new { Id = childId, ParentId = parent.Id, Name = "000-child-" + childId.ToString("N"), CreatedAtUtc = DateTime.UtcNow },
                transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            using var response = await deleting;
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(DocumentErrorCodes.CategoryHasChildren, problem.RootElement.GetProperty("code").GetString());
        }
        finally
        {
            // 屏障失败也必须释放自有事务并等待请求退出，不能把阻塞请求留给后续测试。
            if (!committed) await transaction.RollbackAsync(CancellationToken.None);
            using var response = await deleting;
        }
    }

    public static async Task VerifyAsync(HttpClient client, string token, CancellationToken cancellationToken)
    {
        var root = await CreateAsync(client, token, null, cancellationToken);
        var child = await CreateAsync(client, token, root.Id, cancellationToken);
        var descendant = await CreateAsync(client, token, child.Id, cancellationToken);
        using (var rejected = await UpdateAsync(client, token, root, descendant.Id, cancellationToken))
            await AssertInvalidParentAsync(rejected, cancellationToken);
        using (var unchanged = await client.SendAsync(Request(HttpMethod.Get, $"{Path}/{root.Id:D}", token), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, unchanged.StatusCode);
            var row = await unchanged.Content.ReadFromJsonAsync<HostDocumentCategoryResponse>(cancellationToken);
            Assert.IsNotNull(row);
            Assert.IsNull(row.ParentId);
            Assert.AreEqual(root.Version, row.Version);
        }
        using (var detached = await UpdateAsync(client, token, descendant, null, cancellationToken))
            Assert.AreEqual(HttpStatusCode.OK, detached.StatusCode);

        // 两个独立请求使用各自的有效修订号；锁必须保护整条父链，不能只防止同一行覆盖。
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var a = await CreateAsync(client, token, null, cancellationToken);
            var b = await CreateAsync(client, token, null, cancellationToken);
            var responses = await Task.WhenAll(
                UpdateAsync(client, token, a, b.Id, cancellationToken),
                UpdateAsync(client, token, b, a.Id, cancellationToken));
            try
            {
                Assert.AreEqual(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
                await AssertInvalidParentAsync(responses.Single(response => response.StatusCode != HttpStatusCode.OK), cancellationToken);
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }

        // 删除父分类与新建子分类竞争时，只允许一种操作提交，不能留下失效父级。
        var parent = await CreateAsync(client, token, null, cancellationToken);
        var competing = await Task.WhenAll(
            client.SendAsync(Request(HttpMethod.Post, $"{Path}/{parent.Id:D}/delete", token,
                new DeleteHostDocumentCategoryRequest(parent.Version)), cancellationToken),
            client.SendAsync(Request(HttpMethod.Post, Path, token,
                new CreateHostDocumentCategoryRequest($"child-{Guid.NewGuid():N}", parent.Id, 0)), cancellationToken));
        try
        {
            if (competing[0].StatusCode == HttpStatusCode.OK)
                await AssertInvalidParentAsync(competing[1], cancellationToken);
            else
            {
                Assert.AreEqual(HttpStatusCode.UnprocessableEntity, competing[0].StatusCode);
                Assert.AreEqual(HttpStatusCode.Created, competing[1].StatusCode);
                using var problem = JsonDocument.Parse(await competing[0].Content.ReadAsStringAsync(cancellationToken));
                Assert.AreEqual(DocumentErrorCodes.CategoryHasChildren, problem.RootElement.GetProperty("code").GetString());
            }
        }
        finally { foreach (var response in competing) response.Dispose(); }
    }

    private static async Task<HostDocumentCategoryResponse> CreateAsync(HttpClient client, string token, Guid? parent, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Post, Path, token,
            new CreateHostDocumentCategoryRequest($"hierarchy-{Guid.NewGuid():N}", parent, 0)), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        var row = await response.Content.ReadFromJsonAsync<HostDocumentCategoryResponse>(cancellationToken);
        Assert.IsNotNull(row);
        return row;
    }

    private static async Task<HttpResponseMessage> UpdateAsync(HttpClient client, string token, HostDocumentCategoryResponse row, Guid? parent, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Put, $"{Path}/{row.Id:D}", token,
            new UpdateHostDocumentCategoryRequest(row.Name, parent, row.SortOrder, row.Version));
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task AssertInvalidParentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual(DocumentErrorCodes.CategoryInvalidParent, problem.RootElement.GetProperty("code").GetString());
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
}
