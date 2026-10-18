using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库验证并发授权准入、原会话冻结和最终派发撤销；不打开来源或调用收费模型。</summary>
[TestClass]
public sealed class AiKnowledgeIndexGrantTests
{
    [TestMethod] public Task SqlServer_grant_admission_and_revoke_fence_original_task() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod] public Task MySql_grant_admission_and_revoke_fence_original_task() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, new Dictionary<string, string?> { ["FullNet:Ai:KnowledgeIndexGrants:Enabled"] = "true" });
        await factory.InitializeAsync();
        var identity = await factory.CreateHostIdentityAsync($"index-grant-{Guid.CreateVersion7():N}",
            [AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgePermissions.PolicyUpdate,
                AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Create, AiKnowledgeDocumentPermissions.SourceRead,
                AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue, AiKnowledgeIndexGrantPermissions.Read,
                AiKnowledgeIndexGrantPermissions.Create, AiKnowledgeIndexGrantPermissions.Revoke, AiModelPermissions.Read, AiModelPermissions.Create, "tenancy.tenants.switch"]);
        using var host = factory.CreateClientForHost("localhost"); host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        using var modelResponse = await host.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(null, "授权模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.AreEqual(HttpStatusCode.Created, modelResponse.StatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        foreach (var scoped in new[] { false, true })
        {
            using var client = factory.CreateClientForHost(scoped ? "acme.localhost" : "localhost"); Guid? tenantId = null; var access = identity.AccessToken;
            if (scoped)
            {
                var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client); tenantId = tenant.Id;
                await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(factory, tenant.Id, identity.UserId);
                access = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, identity.AccessToken, tenant.Id);
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var create = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("收费增量", null));
            Assert.AreEqual(HttpStatusCode.Created, create.StatusCode, await create.Content.ReadAsStringAsync());
            var knowledge = (await create.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
            var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
            using var policy = await client.PutAsJsonAsync(path + "/policy", new UpdateAiKnowledgePolicyRequest("internal", model.Id, model.Version, null, null, knowledge.Version));
            Assert.AreEqual(HttpStatusCode.OK, policy.StatusCode, await policy.Content.ReadAsStringAsync());
            knowledge = (await policy.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
            using var docResponse = await client.PostAsJsonAsync(path + "/documents", new CreateAiKnowledgeDocumentRequest("增量来源", null));
            Assert.AreEqual(HttpStatusCode.Created, docResponse.StatusCode, await docResponse.Content.ReadAsStringAsync());
            var document = (await docResponse.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
            var versionId = Guid.CreateVersion7();
            await using var db = AiOperationBudgetAssertions.Connection(factory);
            var p = new { KnowledgeBaseId = knowledge.Id, DocumentId = document.Id, VersionId = versionId, FileId = Guid.CreateVersion7(),
                ImportId = Guid.CreateVersion7(), OwnerUserId = identity.UserId, Now = DateTimeOffset.UtcNow };
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc) VALUES (@VersionId,@DocumentId,@FileId,1,'a.txt','text/plain','abcdef',10,@OwnerUserId,@Now)", p);
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_import (Id,DocumentId,VersionId,RequestKey,ExpectedDocumentVersion,OriginalFileName,ContentType,ContentHash,SizeBytes,ActorUserId,FileId,StateKey,CreatedAtUtc,UpdatedAtUtc) VALUES (@ImportId,@DocumentId,@VersionId,@ImportId,1,'a.txt','text/plain','abcdef',10,@OwnerUserId,@FileId,'completed',@Now,@Now)", p);
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (tenantId is { } id) context.SetTenant(new(id, "grant-test", "授权测试")); else context.SetHost();
            Assert.IsTrue(await services.GetRequiredService<AiKnowledgeIncrementalIndexWriter>().ApplyAsync(new(knowledge.Id, document.Id, versionId, tenantId), default));
            var expiry = new DateTimeOffset(DateTimeOffset.UtcNow.AddHours(1).UtcTicks / 10 * 10 + 7, TimeSpan.Zero);
            var request = new CreateAiKnowledgeIndexGrantRequest(Guid.CreateVersion7(), knowledge.Version, expiry, 1, 100, true);
            using var denied = await client.PostAsJsonAsync(path + "/index-grants", request with { ConfirmPaidDispatch = false });
            Assert.AreEqual(HttpStatusCode.BadRequest, denied.StatusCode);
            using var grantResponse = await client.PostAsJsonAsync(path + "/index-grants", request);
            Assert.AreEqual(HttpStatusCode.OK, grantResponse.StatusCode, await grantResponse.Content.ReadAsStringAsync());
            var grant = (await grantResponse.Content.ReadFromJsonAsync<AiKnowledgeIndexGrantResponse>())!;
            Assert.AreEqual(expiry.UtcTicks / 10 * 10, grant.ExpiresAtUtc.UtcTicks);
            using var replay = await client.PostAsJsonAsync(path + "/index-grants", request);
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode, await replay.Content.ReadAsStringAsync());
            Assert.AreEqual(grant.Id, (await replay.Content.ReadFromJsonAsync<AiKnowledgeIndexGrantResponse>())!.Id);
            // 两个独立消费者可竞争扫描，但任务、次数与原会话绑定只能同事务成功一次。
            var first = ActivatorUtilities.CreateInstance<AiKnowledgeIndexGrantProcessor>(factory.Services);
            var second = ActivatorUtilities.CreateInstance<AiKnowledgeIndexGrantProcessor>(factory.Services);
            try { await Task.WhenAll(first.ProcessAsync(16, default), second.ProcessAsync(16, default)); }
            finally { first.Dispose(); second.Dispose(); }
            var row = await services.GetRequiredService<AiKnowledgeIndexGrantStore>().LoadAsync(grant.Id, default);
            Assert.IsNotNull(row); Assert.AreEqual(1, row.AdmittedDocuments);
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_index_grant_item WHERE GrantId=@Id", new { Id = grant.Id }));
            var taskId = await db.QuerySingleAsync<Guid>("SELECT TaskId FROM fn_ai_index_grant_item WHERE GrantId=@Id", new { Id = grant.Id });
            var binding = new AiKnowledgeIndexTaskBinding(knowledge.Id, document.Id, versionId, document.Version, knowledge.Version, identity.UserId);
            var tasks = services.GetRequiredService<AiKnowledgeIndexTaskStore>(); var held = await tasks.AcquireAsync(binding, taskId, default);
            Assert.IsNotNull(held); Assert.IsTrue(held.LeaseToken.HasValue);
            Assert.IsNotNull(await tasks.ReadDispatchAsync(binding, taskId, held.LeaseToken.Value, default));
            var grants = services.GetRequiredService<AiKnowledgeIndexGrantStore>();
            Assert.IsTrue(await grants.ReserveBatchAsync(binding, taskId, 0, 60, default));
            Assert.IsTrue(await grants.ReserveBatchAsync(binding, taskId, 0, 60, default));
            Assert.IsFalse(await grants.ReserveBatchAsync(binding, taskId, 1, 41, default));
            Assert.IsTrue(await grants.ReserveBatchAsync(binding, taskId, 1, 40, default));
            Assert.AreEqual(100L, await db.QuerySingleAsync<long>("SELECT ReservedInputTokens FROM fn_ai_index_grant_item WHERE TaskId=@Id", new { Id = taskId }));
            using var stop = await client.PostAsJsonAsync(path + $"/index-grants/{grant.Id}/revoke", new RevokeAiKnowledgeIndexGrantRequest(row.Version));
            Assert.AreEqual(HttpStatusCode.OK, stop.StatusCode, await stop.Content.ReadAsStringAsync());
            Assert.IsNull(await tasks.ReadDispatchAsync(binding, taskId, held.LeaseToken.Value, default));
            var sessions = services.GetRequiredService<AiKnowledgeIndexSessionService>();
            Assert.IsNull(await sessions.ReadHeldAsync(binding, taskId, held.LeaseToken.Value, default));
            Assert.IsFalse(await grants.ReserveBatchAsync(binding, taskId, 2, 0, default));
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE VersionId=@VersionId", p));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = taskId }));
            Assert.IsNotNull(AiKnowledgeIndexGrantPolicy.Session(row));
        }
    }
}
