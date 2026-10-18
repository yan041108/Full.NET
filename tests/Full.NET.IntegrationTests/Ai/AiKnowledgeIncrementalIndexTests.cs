using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Features.ManageKnowledgeDocuments;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证来源确认事件、单调增量意图与提交失败；后台消费不执行模型调用。</summary>
[TestClass]
public sealed class AiKnowledgeIncrementalIndexTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task SqlServer_source_confirmation_creates_idempotent_incremental_intent(bool scoped) => VerifyAsync(DatabaseProvider.SqlServer, scoped);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task MySql_source_confirmation_creates_idempotent_incremental_intent(bool scoped) => VerifyAsync(DatabaseProvider.MySql, scoped);

    private static async Task VerifyAsync(DatabaseProvider provider, bool scoped)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection);
        await factory.InitializeAsync();
        var owner = await factory.CreateHostIdentityAsync($"incremental-{Guid.CreateVersion7():N}",
            [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Read,
                AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.VersionsRead,
                AiKnowledgeDocumentPermissions.Delete, AiKnowledgeDocumentPermissions.Update, AiKnowledgeDocumentPermissions.IndexRead,
                AiKnowledgeDocumentPermissions.IndexChangesRead, "tenancy.tenants.switch"]);
        using var host = factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var client = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost");
        Guid? tenantId = null;
        var accessToken = owner.AccessToken;
        if (scoped)
        {
            var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client);
            tenantId = tenant.Id;
            await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, owner.UserId);
            accessToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, owner.AccessToken, tenant.Id);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var createBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("增量来源", null));
        Assert.AreEqual(HttpStatusCode.Created, createBase.StatusCode, await createBase.Content.ReadAsStringAsync());
        var knowledge = (await createBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        using var createDoc = await client.PostAsJsonAsync($"/api/v1/ai/knowledge-bases/{knowledge.Id}/documents", new CreateAiKnowledgeDocumentRequest("最新来源", null));
        var document = (await createDoc.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}/documents/{document.Id}";
        await using var db = AiOperationBudgetAssertions.Connection(factory);

        async Task<AiKnowledgeImportResponse> Upload(int expected, Guid retry, string text)
        {
            using var response = await client.PostAsync(path + $"/versions?version={expected}&requestKey={retry}&fileName=source.txt", new StringContent(text, Encoding.UTF8, "text/plain"));
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode, await response.Content.ReadAsStringAsync());
            var result = (await response.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!;
            Assert.AreEqual("completed", result.State);
            return result;
        }
        var firstKey = Guid.CreateVersion7();
        var first = await Upload(1, firstKey, "第一版");
        Assert.AreEqual(first.Id, (await Upload(1, firstKey, "第一版")).Id);
        var firstEventCount = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeSourceConfirmed.EventType });
        Assert.AreEqual(1, firstEventCount);
        var second = await Upload(2, Guid.CreateVersion7(), "第二版");
        Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeSourceConfirmed.EventType }));
        var payloads = (await db.QueryAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeSourceConfirmed.EventType })).ToArray();

        await using var scope = factory.Services.CreateAsyncScope();
        var current = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        current.SetHost();
        var serializer = scope.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>();
        var handler = new AiKnowledgeSourceConfirmedHandler(serializer, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());
        var events = payloads.Select(bytes => (Bytes: bytes, Event: serializer.Deserialize<AiKnowledgeSourceConfirmed>(bytes))).ToArray();
        var latest = events.Single(item => item.Event.VersionId == second.VersionId);
        var old = events.Single(item => item.Event.VersionId == first.VersionId);
        var envelope = new IntegrationEventContext(Guid.CreateVersion7(), handler.EventType, 1, tenantId, null, DateTimeOffset.UtcNow);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ConcurrentDelivery()));
        var stableId = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_index_change WHERE DocumentId=@DocumentId", new { DocumentId = document.Id });
        await handler.HandleAsync(envelope, old.Bytes, CancellationToken.None);
        await handler.HandleAsync(envelope, latest.Bytes, CancellationToken.None);
        async Task ConcurrentDelivery()
        {
            await using var delivery = factory.Services.CreateAsyncScope();
            var deliveryTenant = delivery.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            deliveryTenant.SetHost();
            await new AiKnowledgeSourceConfirmedHandler(delivery.ServiceProvider.GetRequiredService<IIntegrationEventSerializer>(), delivery.ServiceProvider.GetRequiredService<IServiceScopeFactory>())
                .HandleAsync(envelope, latest.Bytes, CancellationToken.None);
        }
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ConcurrentDelivery()));
        Assert.IsTrue(current.IsHost);
        Assert.IsNull(current.Id);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.HandleAsync(envelope with { TenantId = Guid.CreateVersion7() }, latest.Bytes, CancellationToken.None));
        Assert.IsTrue(current.IsHost);
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change WHERE DocumentId=@DocumentId", new { DocumentId = document.Id }));
        Assert.AreEqual(stableId, await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_index_change WHERE DocumentId=@DocumentId", new { DocumentId = document.Id }));
        Assert.AreEqual(second.VersionId, await db.QuerySingleAsync<Guid>("SELECT VersionId FROM fn_ai_knowledge_index_change WHERE DocumentId=@DocumentId", new { DocumentId = document.Id }));
        using var versionsResponse = await client.GetAsync(path + "/versions");
        var versions = (await versionsResponse.Content.ReadFromJsonAsync<Full.NET.Abstractions.Results.PagedResult<AiKnowledgeVersionResponse>>())!;
        Assert.AreEqual("awaiting_index_confirmation", versions.Items.Single(item => item.Id == second.VersionId).ProcessingStatus);
        Assert.AreEqual("pending_processing", versions.Items.Single(item => item.Id == first.VersionId).ProcessingStatus);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@DocumentId", new { DocumentId = document.Id }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_session AS session INNER JOIN fn_ai_knowledge_index_task AS task ON task.Id=session.IndexTaskId WHERE task.DocumentId=@DocumentId", new { DocumentId = document.Id }));

        var directoryPath = $"/api/v1/ai/knowledge-bases/{knowledge.Id}/index-changes";
        async Task<AiKnowledgeIndexChangePage> Directory(string query = "")
        {
            using var response = await client.GetAsync(directoryPath + query);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<AiKnowledgeIndexChangePage>())!;
        }
        var confirmed = await Directory("?pageSize=1");
        Assert.AreEqual(1, confirmed.Items.Count); Assert.IsNull(confirmed.NextDocumentId);
        Assert.AreEqual(document.Id, confirmed.Items[0].DocumentId); Assert.AreEqual(second.VersionId, confirmed.Items[0].VersionId);
        Assert.AreEqual(2, confirmed.Items[0].VersionNumber);
        Assert.AreEqual(0, (await Directory($"?afterDocumentId={document.Id}&pageSize=1")).Items.Count);
        foreach (var invalid in new[] { "?pageSize=0", "?pageSize=51", "?pageSize=", "?afterDocumentId=", "?pageSize=1&pageSize=2", "?tenantId=" + Guid.CreateVersion7(), "?afterDocumentId=" + Guid.Empty })
        {
            using var rejected = await client.GetAsync(directoryPath + invalid);
            Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode, invalid);
        }
        // 完成来源已前进而投影落后时，目录不能暴露旧来源作为当前入口。
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_change SET VersionId=@VersionId,VersionNumber=1 WHERE DocumentId=@Id", new { VersionId = first.VersionId, Id = document.Id });
        Assert.AreEqual(0, (await Directory()).Items.Count);
        await handler.HandleAsync(envelope, latest.Bytes, CancellationToken.None);
        foreach (var permission in new[] { false, true })
        {
            var outsider = await factory.CreateHostIdentityAsync($"directory-{Guid.CreateVersion7():N}", permission
                ? [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.VersionsRead, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeDocumentPermissions.IndexChangesRead, "tenancy.tenants.switch"]
                : [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.VersionsRead, AiKnowledgeDocumentPermissions.IndexRead, "tenancy.tenants.switch"]);
            using var other = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost");
            var otherToken = outsider.AccessToken;
            if (tenantId is { } directoryTenant)
            {
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, directoryTenant, outsider.UserId);
                otherToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, outsider.AccessToken, directoryTenant);
            }
            other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
            using var forbidden = await other.GetAsync(directoryPath);
            Assert.AreEqual(permission ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        // 元数据更新可靠修复历史缺失投影，不制造新文件来源或有费用任务。
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id });
        using var metadataUpdate = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeDocumentRequest("更新后的标题", "更新后的描述", 3));
        Assert.AreEqual(HttpStatusCode.OK, metadataUpdate.StatusCode, await metadataUpdate.Content.ReadAsStringAsync());
        var updatedDocument = (await metadataUpdate.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        Assert.AreEqual(4, updatedDocument.Version);
        using var staleUpdate = await client.PutAsJsonAsync(path, new UpdateAiKnowledgeDocumentRequest("过期", null, 3));
        Assert.AreEqual(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        var metadataPayload = await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeDocumentMetadataUpdated.EventType });
        var metadata = serializer.Deserialize<AiKnowledgeDocumentMetadataUpdated>(metadataPayload);
        Assert.AreEqual(tenantId, metadata.TenantId); Assert.AreEqual(4, metadata.MetadataVersion);
        var metadataHandler = new AiKnowledgeDocumentMetadataUpdatedHandler(serializer, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());
        var metadataEnvelope = envelope with { MessageType = metadataHandler.EventType };
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => metadataHandler.HandleAsync(metadataEnvelope, metadataPayload, CancellationToken.None)));
        Assert.IsTrue(current.IsHost); Assert.IsNull(current.Id);
        Assert.AreEqual(second.VersionId, await db.QuerySingleAsync<Guid>("SELECT VersionId FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id }));
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id }));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => metadataHandler.HandleAsync(metadataEnvelope with { TenantId = Guid.CreateVersion7() }, metadataPayload, CancellationToken.None));
        Assert.AreEqual("更新后的标题", (await Directory()).Items.Single().Title);

        // 真实事务失败必须同时保留旧元数据及原事件数，而非仅模拟回滚回调。
        if (tenantId is { } metadataTenant) current.SetTenant(new(metadataTenant, "acme", "Acme"));
        var metadataService = ActivatorUtilities.CreateInstance<AiKnowledgeDocumentService>(scope.ServiceProvider, new FailingOutbox());
        await Assert.ThrowsExactlyAsync<IOException>(() => metadataService.UpdateAsync(knowledge.Id, document.Id, owner.UserId,
            new UpdateAiKnowledgeDocumentRequest("不应提交", null, 4), CancellationToken.None));
        Assert.AreEqual(4, await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_document WHERE Id=@Id", new { Id = document.Id }));
        Assert.AreEqual("更新后的标题", await db.QuerySingleAsync<string>("SELECT Title FROM fn_ai_knowledge_document WHERE Id=@Id", new { Id = document.Id }));
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = metadataHandler.EventType }));
        current.SetHost();
        // 未来通知不具备权威版本；缺失、停用以及不归属当前范围的来源均不能补写。
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id });
        await metadataHandler.HandleAsync(metadataEnvelope, serializer.Serialize(metadata with { MetadataVersion = 99 }), CancellationToken.None);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id }));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@Id", new { Id = knowledge.Id });
        using (var hidden = await client.GetAsync(directoryPath)) Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        await metadataHandler.HandleAsync(metadataEnvelope, metadataPayload, CancellationToken.None);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id }));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=1 WHERE Id=@Id", new { Id = knowledge.Id });
        await metadataHandler.HandleAsync(metadataEnvelope, serializer.Serialize(metadata with { MetadataVersion = 3 }), CancellationToken.None);
        Assert.AreEqual(second.VersionId, await db.QuerySingleAsync<Guid>("SELECT VersionId FROM fn_ai_knowledge_index_change WHERE DocumentId=@Id", new { Id = document.Id }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@Id", new { Id = document.Id }));

        // 注入真正的 Outbox 写入故障，确认本地完成状态不会单独提交。
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET StateKey='pending_confirmation',LeaseToken=@Lease,LeaseExpiresAtUtc=@Expires WHERE Id=@Id", new { Id = second.Id, Lease = stableId, Expires = DateTime.UtcNow.AddMinutes(3) });
        if (tenantId is { } activeTenant) current.SetTenant(new TenantContext(activeTenant, "acme", "Acme"));
        var store = ActivatorUtilities.CreateInstance<AiKnowledgeImportStore>(scope.ServiceProvider, new FailingOutbox());
        var parameters = store.Parameters(knowledge.Id, document.Id, owner.UserId);
        parameters["OperationId"] = second.Id; parameters["LeaseToken"] = stableId;
        await Assert.ThrowsExactlyAsync<IOException>(() => store.StateAsync(parameters, "completed", null, CancellationToken.None));
        Assert.AreEqual("pending_confirmation", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_import WHERE Id=@Id", new { Id = second.Id }));
        Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeSourceConfirmed.EventType }));

        await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET StateKey='completed' WHERE Id=@Id", new { Id = second.Id });
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_change WHERE DocumentId=@DocumentId", new { DocumentId = document.Id });
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(4)) };
        var deletion = client.SendAsync(deleteRequest);
        await Task.WhenAll(deletion, ConcurrentDelivery(), metadataHandler.HandleAsync(metadataEnvelope, metadataPayload, CancellationToken.None));
        using var deleted = await deletion;
        Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode, await deleted.Content.ReadAsStringAsync());
        Assert.AreEqual(0, (await Directory()).Items.Count);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change WHERE DocumentId=@DocumentId", new { DocumentId = document.Id }));
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change AS intent INNER JOIN fn_ai_knowledge_document AS document ON document.Id=intent.DocumentId WHERE document.Id=@DocumentId AND document.IsDeleted=0", new { DocumentId = document.Id }));
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_index_change WHERE DocumentId=@DocumentId", new { DocumentId = document.Id });
        await handler.HandleAsync(envelope, latest.Bytes, CancellationToken.None);
        await metadataHandler.HandleAsync(metadataEnvelope, metadataPayload, CancellationToken.None);
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_change WHERE DocumentId=@DocumentId", new { DocumentId = document.Id }));

        // 两库各自的 UUID 排序不同，必须沿数据库返回的非空游标遍历，不能按客户端 Guid 顺序猜测。
        var directoryDocuments = new HashSet<Guid>(); var directoryVersions = new HashSet<Guid>();
        for (var index = 0; index < 3; index++)
        {
            using var added = await client.PostAsJsonAsync($"/api/v1/ai/knowledge-bases/{knowledge.Id}/documents", new CreateAiKnowledgeDocumentRequest("分页来源" + index, null));
            Assert.AreEqual(HttpStatusCode.Created, added.StatusCode);
            var row = (await added.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!; directoryDocuments.Add(row.Id);
            using var uploaded = await client.PostAsync($"/api/v1/ai/knowledge-bases/{knowledge.Id}/documents/{row.Id}/versions?version=1&requestKey={Guid.CreateVersion7()}&fileName=page.txt", new StringContent("分页", Encoding.UTF8, "text/plain"));
            Assert.AreEqual(HttpStatusCode.Accepted, uploaded.StatusCode, await uploaded.Content.ReadAsStringAsync());
            directoryVersions.Add((await uploaded.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.VersionId);
        }
        foreach (var payload in await db.QueryAsync<byte[]>("SELECT Payload FROM fn_outbox_message WHERE MessageType=@Type", new { Type = AiKnowledgeSourceConfirmed.EventType }))
            if (directoryVersions.Contains(serializer.Deserialize<AiKnowledgeSourceConfirmed>(payload).VersionId)) await handler.HandleAsync(envelope, payload, CancellationToken.None);
        foreach (var pageSize in new[] { 1, 2 })
        {
            var seen = new HashSet<Guid>(); Guid? cursor = null; var pages = 0;
            do
            {
                var result = await Directory($"?pageSize={pageSize}" + (cursor is { } after ? $"&afterDocumentId={after}" : ""));
                Assert.IsTrue(result.Items.Count <= pageSize);
                foreach (var row in result.Items) Assert.IsTrue(seen.Add(row.DocumentId), "目录不得重复来源");
                if (result.NextDocumentId is { } next) { Assert.AreEqual(result.Items.Last().DocumentId, next); Assert.AreNotEqual(cursor, next); }
                cursor = result.NextDocumentId; Assert.IsTrue(++pages <= 3);
            } while (cursor.HasValue);
            CollectionAssert.AreEquivalent(directoryDocuments.ToArray(), seen.ToArray());
            Assert.AreEqual(pageSize == 1 ? 3 : 2, pages);
        }
        Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task"));
        if (scoped)
        {
            // 切换租户后旧 Host 令牌已失效；隔离断言必须使用真实切回 Host 后的有效会话。
            using var staleSession = await host.GetAsync(directoryPath);
            Assert.AreEqual(HttpStatusCode.Unauthorized, staleSession.StatusCode);
            using var returnHostRequest = new HttpRequestMessage(HttpMethod.Put, "/api/v1/tenancy/context")
            {
                Content = JsonContent.Create(new ChangeTenantContextRequest(null)),
            };
            returnHostRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var returnHostResponse = await host.SendAsync(returnHostRequest);
            Assert.AreEqual(HttpStatusCode.OK, returnHostResponse.StatusCode, await returnHostResponse.Content.ReadAsStringAsync());
            var returned = await returnHostResponse.Content.ReadFromJsonAsync<TenantContextTokenResponse>();
            Assert.IsNotNull(returned);
            Assert.IsNull(returned.Context.TenantId);
            host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", returned.AccessToken);
            using var wrongScope = await host.GetAsync(directoryPath);
            Assert.AreEqual(HttpStatusCode.NotFound, wrongScope.StatusCode);
        }
    }

    private sealed class FailingOutbox : IOutboxWriter
    {
        public Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(string eventType, int schemaVersion, TEvent payload, CancellationToken cancellationToken = default) =>
            throw new IOException("Controlled Outbox failure");
        public Task AddAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(string eventType, int schemaVersion, TEvent payload, Full.NET.Messaging.Abstractions.IntegrationEventMetadata metadata, CancellationToken cancellationToken = default) =>
            throw new IOException("Controlled Outbox failure");
    }
}
