using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Native API、身份和 Files 验证发布治理；完整候选由生产 JIT 索引器配合受控模型准备。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeKnowledgePublicationE2ETests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_publication_directory_rejected_draft_and_retirement_race(DatabaseProvider provider)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-native-publication-keys", Guid.CreateVersion7().ToString("N")),
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-native-publication-files", Guid.CreateVersion7().ToString("N")),
            ["FullNet:Ai:KnowledgeIndexRebuild:Enabled"] = "true",
        };
        var model = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory();
        using var factory = new FullNetApiFactory(provider, connection, settingsOverrides: settings, configureTestServices: services =>
        {
            new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
            services.RemoveAll<IAiModelClientFactory>(); services.AddSingleton<IAiModelClientFactory>(model);
        });
        await factory.InitializeAsync();
        var username = $"native-review-{Guid.CreateVersion7():N}";
        var reviewerIdentity = await factory.CreateHostIdentityAsync(username,
            [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead,
                AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgePublicationPermissions.Read, AiKnowledgePublicationPermissions.Review, "tenancy.tenants.switch"],
            password: FullNetApiFactory.TestPassword);
        var reviewerEmail = username + "@example.test";
        await factory.EnsureHostUserProfileEmailAsync(reviewerIdentity.UserId, reviewerEmail, "原生独立审核");
        await using var api = await NativeApiProcessHost.StartAsync(artifact, provider, connection, settings, NativeAotTestTimeouts.ProcessStartup);
        using var client = api.CreateClient(); using var reviewerClient = api.CreateClient();
        Console.WriteLine($"Native publication API log: {api.LogFilePath}");
        var owner = await NativeApiE2EAssertions.LoginAsync(client, api.LogFilePath);
        var reviewer = await IntegrationTestAuthHelper.LoginAsHostUserAsync(reviewerClient, username);
        // 模型管理属于 Host；租户只通过自己的知识库策略显式批准这些 Host 配置。
        var embedding = await Send<AiModelConfigResponse>(HttpMethod.Post, "/api/v1/ai/model-configs", owner,
            new CreateAiModelConfigRequest(null, "原生发布模型", "openai_compatible", "https://api.openai.com/v1", "text-embedding-3-small", "native-test-placeholder", null, false, true), HttpStatusCode.Created);
        var generation = await Send<AiModelConfigResponse>(HttpMethod.Post, "/api/v1/ai/model-configs", owner,
            new CreateAiModelConfigRequest(null, "原生历史模型", "openai_compatible", "https://api.openai.com/v1", "gpt-4.1-mini", "native-test-placeholder", null, false, true), HttpStatusCode.Created);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        string? previousScopeHead = null;
        foreach (var tenant in new[] { false, true })
        {
            Guid reviewerId = reviewerIdentity.UserId;
            if (tenant)
            {
                var target = (await Send<TenantContextSummary[]>(HttpMethod.Get, "/api/v1/tenancy/available", owner)).Single(item => item.Identifier == "acme");
                owner = (await Send<TenantContextTokenResponse>(HttpMethod.Put, "/api/v1/tenancy/context", owner, new ChangeTenantContextRequest(target.Id))).AccessToken;
                await Expect(HttpMethod.Get, previousScopeHead!, owner, null, HttpStatusCode.NotFound);
                var invitation = await Send<CreateTenantInvitationResult>(HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", owner,
                    new CreateTenantInvitationRequest(reviewerEmail, TenantMemberRoles.Admin));
                var accepted = await Send<AcceptTenantInvitationResponse>(HttpMethod.Post, "/api/v1/identity/tenant-invitations/accept", reviewer,
                    new AcceptTenantInvitationRequest(invitation.InvitationToken));
                Assert.AreEqual(reviewerId, accepted.UserId);
                var hostReviewer = await IntegrationTestAuthHelper.LoginAsHostUserAsync(reviewerClient, username);
                reviewer = (await Send<TenantContextTokenResponse>(HttpMethod.Put, "/api/v1/tenancy/context", hostReviewer, new ChangeTenantContextRequest(target.Id))).AccessToken;
            }
            using var payload = Payload(owner); var actor = Guid.Parse(payload.RootElement.GetProperty("sub").GetString()!);
            Guid? tenantId = tenant ? Guid.Parse(payload.RootElement.GetProperty(FullNetIdentityClaimTypes.TenantId).GetString()!) : null;
            var knowledge = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", owner, new CreateAiKnowledgeBaseRequest("原生发布治理", null), HttpStatusCode.Created);
            var root = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
            await Send<AiKnowledgeBaseResponse>(HttpMethod.Put, root + "/policy", owner, new UpdateAiKnowledgePolicyRequest("internal", embedding.Id, embedding.Version, generation.Id, generation.Version, knowledge.Version));
            knowledge = await Send<AiKnowledgeBaseResponse>(HttpMethod.Get, root, owner);
            await Send<AiKnowledgeMembersResponse>(HttpMethod.Put, root + "/members", owner, new SetAiKnowledgeMembersRequest([reviewerId], knowledge.Version));
            var document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Post, root + "/documents", owner, new CreateAiKnowledgeDocumentRequest("正式原文", null), HttpStatusCode.Created);
            var path = root + $"/documents/{document.Id}";
            await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest([reviewerId], document.Version));
            var publications = path + "/publications";
            previousScopeHead = publications + "/head";
            var first = await Generate("原生发布的第一版知识正文。");
            var request = new RequestAiKnowledgePublication(Guid.CreateVersion7(), first.Version, 0, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var proposal = await Send<AiKnowledgePublicationResponse>(HttpMethod.Post, publications, owner, request, HttpStatusCode.Created);
            Assert.AreEqual(proposal.Id, (await Send<AiKnowledgePublicationResponse>(HttpMethod.Post, publications, owner, request, HttpStatusCode.Created)).Id);
            await Expect(HttpMethod.Post, publications + $"/{proposal.Id}/review", owner, new ReviewAiKnowledgePublication(true, proposal.Version, proposal.SnapshotHash), HttpStatusCode.Conflict);
            proposal = await Review(proposal, true);
            if (!tenant) await Expect(HttpMethod.Post, publications + $"/{proposal.Id}/publish", reviewer, new PublishAiKnowledgePublication(proposal.Version, proposal.SnapshotHash), HttpStatusCode.Forbidden);
            proposal = await Publish(proposal);
            Assert.AreEqual("published", proposal.StateKey);
            Assert.AreEqual(proposal.Id, (await Send<AiKnowledgePublicationHeadResponse>(HttpMethod.Get, publications + "/head", owner)).PublicationId);
            var originalSource = (await Retrieve()).Sources.Single();
            Assert.AreEqual(proposal.Id, originalSource.PublicationId);
            Guid chatId;
            if (!tenant) chatId = (await Send<AiChatSessionResponse>(HttpMethod.Post, "/api/v1/ai/chat/sessions", owner, new CreateAiChatSessionRequest(generation.Id, "原生发布历史"), HttpStatusCode.Created)).Id;
            else
            {
                // 当前产品基线仅开放租户历史读取；不为 K01 验收扩大租户聊天写入权限。
                await Expect(HttpMethod.Post, "/api/v1/ai/chat/sessions", owner, new CreateAiChatSessionRequest(generation.Id, "租户禁止写入"), HttpStatusCode.Forbidden);
                chatId = Guid.CreateVersion7();
                await db.ExecuteAsync("INSERT INTO fn_ai_chat_session (Id,TenantId,OwnerUserId,ModelConfigId,ModelName,Title,CreatedAtUtc) VALUES (@Id,@Tenant,@Actor,@Model,@ModelName,'原生租户历史夹具',@Now)",
                    new { Id = chatId, Tenant = tenantId, Actor = actor, Model = generation.Id, ModelName = generation.ModelId, Now = DateTimeOffset.UtcNow });
            }
            var messageId = Guid.CreateVersion7();
            var lineage = new AiKnowledgeAnswerLineage(3, messageId, chatId, actor, tenantId,
                [new(originalSource.KnowledgeBaseId, originalSource.DocumentId, originalSource.VersionId, originalSource.DocumentVersion, originalSource.KnowledgeBaseVersion,
                    AiKnowledgeFileSource.RequireFileId(originalSource), originalSource.ContentHash, originalSource.TaskId!.Value, originalSource.ActiveReceiptHash!, originalSource.ActiveManifestHash!, originalSource.ActivePlanHash!, proposal.Id)], []);
            // 历史正文为明确种入的测试数据；来源与回执来自真实完整代次，Native 读取必须重验发布身份。
            await db.ExecuteAsync("INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc,IsKnowledgeAnswer,KnowledgeLineage) VALUES (@Id,@SessionId,'assistant',@Content,'completed',@Now,1,@Lineage)",
                new { Id = messageId, SessionId = chatId, Content = "受发布保护的历史正文", Now = DateTimeOffset.UtcNow, Lineage = AiKnowledgeAnswerLineageCodec.Encode(lineage) });
            Assert.AreEqual("受发布保护的历史正文", (await ReadHistory()).Content);

            var folder = await Send<AiKnowledgeFolderResponse>(HttpMethod.Post, root + "/folders", owner, new SaveAiKnowledgeFolder("原生文件夹", null, 0));
            folder = await Send<AiKnowledgeFolderResponse>(HttpMethod.Put, root + $"/folders/{folder.Id}", owner, new SaveAiKnowledgeFolder("已改名", null, folder.Version));
            Assert.AreEqual(folder.Id, (await Send<AiKnowledgeFolderResponse[]>(HttpMethod.Get, root + "/folders", reviewer)).Single().Id);
            var item = new SetAiKnowledgeDirectoryItem(document.Id, Guid.CreateVersion7(), 0, folder.Id, ["Ａ", "a", " 中文 "], actor);
            var batch = await Send<AiKnowledgeDirectoryItemResult[]>(HttpMethod.Post, root + "/directory/batch", owner,
                new SetAiKnowledgeDirectoryBatch([item, item with { DocumentId = Guid.CreateVersion7(), RequestKey = Guid.CreateVersion7() }]));
            Assert.IsTrue(batch[0].Succeeded); Assert.IsFalse(batch[1].Succeeded);
            Assert.AreEqual(batch[0].Version, (await Send<AiKnowledgeDirectoryItemResult[]>(HttpMethod.Post, root + "/directory/batch", owner, new SetAiKnowledgeDirectoryBatch([item])))[0].Version);
            var page = await Send<JsonElement>(HttpMethod.Get, root + $"/directory?folderId={folder.Id}&tag=a&state=published", owner);
            Assert.AreEqual(document.Id, page.GetProperty("items")[0].GetProperty("id").GetGuid());
            Assert.AreEqual(2, page.GetProperty("items")[0].GetProperty("tags").GetArrayLength());
            await Expect(HttpMethod.Delete, root + $"/folders/{folder.Id}", owner, new DeleteAiKnowledgeFolder(folder.Version), HttpStatusCode.Conflict);

            // 第二份完整代次真实收费、持久化和激活；驳回不能改变第一版发布身份或资产。
            var second = await Generate("原生发布的第二版修订正文。");
            var rejected = await Send<AiKnowledgePublicationResponse>(HttpMethod.Post, publications, owner,
                request with { RequestKey = Guid.CreateVersion7(), VersionId = second.Version, ExpectedHeadVersion = 1 }, HttpStatusCode.Created);
            rejected = await Review(rejected, false); Assert.AreEqual("rejected", rejected.StateKey);
            await Expect(HttpMethod.Post, publications + $"/{rejected.Id}/publish", owner, new PublishAiKnowledgePublication(rejected.Version, rejected.SnapshotHash), HttpStatusCode.Conflict);
            Assert.AreEqual(first.Version, (await Retrieve()).Sources.Single().VersionId);
            Assert.AreEqual("受发布保护的历史正文", (await ReadHistory()).Content);
            var next = await Send<AiKnowledgePublicationResponse>(HttpMethod.Post, publications, owner,
                request with { RequestKey = Guid.CreateVersion7(), VersionId = second.Version, ExpectedHeadVersion = 1 }, HttpStatusCode.Created);
            next = await Review(next, true);
            // 只移动临时库的保留期时间，不伪造代次、审批或模型回执。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_active_generation SET ActivatedAtUtc=@Time WHERE DocumentId=@Id", new { Time = DateTimeOffset.UtcNow.AddHours(-26), Id = document.Id });
            await Cleanup(first.Task); Assert.AreEqual(0, await CleanupState(first.Task));
            var old = (await Retrieve()).Sources.Single();
            await using (var scope = Scope())
            {
                var sp = scope.ServiceProvider;
                await using var lease = await AiKnowledgeGenerationLease.AcquireAsync(old, actor, sp.GetRequiredService<IQueryExecutor>(), sp.GetRequiredService<ICommandExecutor>(),
                    sp.GetRequiredService<Full.NET.Abstractions.Messaging.ICommandTransaction>(), sp.GetRequiredService<ICurrentTenant>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<IIdGenerator>(), sp.GetRequiredService<IOptions<DatabaseOptions>>(), default);
                Assert.IsNotNull(lease);
                var publishing = Publish(next); var cleanup = Cleanup(first.Task); var reading = ReadDuringSwitch();
                await Task.WhenAll(publishing, cleanup, reading);
                if (reading.Result is { } observed) Assert.IsTrue(observed.Sources.Single().PublicationId == proposal.Id || observed.Sources.Single().PublicationId == next.Id);
                await Cleanup(first.Task); Assert.AreEqual(0, await CleanupState(first.Task), "刚被替换的发布证据仍受最短保留期保护。");
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_publication SET UpdatedAtUtc=@Time WHERE Id=@Id", new { Time = DateTimeOffset.UtcNow.AddHours(-26), Id = proposal.Id });
                await Cleanup(first.Task);
                Assert.IsTrue(lease.IsValid); Assert.AreEqual(0, await CleanupState(first.Task), "保留期已过时，在途读取租约仍须保护已退休代次。");
            }
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_read_lease WHERE GenerationId=@Id", new { Id = first.Task }));
            await Cleanup(first.Task); Assert.AreEqual(2, await CleanupState(first.Task));
            Assert.AreEqual(second.Version, (await Retrieve()).Sources.Single().VersionId);
            Assert.AreEqual("", (await ReadHistory()).Content, "发布身份变更后不能复用旧历史依赖。");
            var head = await Send<AiKnowledgePublicationHeadResponse>(HttpMethod.Get, publications + "/head", owner);
            Assert.AreEqual(next.Id, head.PublicationId);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_publication SET EffectiveToUtc=@Time WHERE Id=@Id", new { Time = DateTimeOffset.UtcNow.AddMinutes(-1), Id = next.Id });
            var expiredPage = await Send<JsonElement>(HttpMethod.Get, root + "/directory?state=expired", owner);
            Assert.AreEqual(document.Id, expiredPage.GetProperty("items")[0].GetProperty("id").GetGuid());
            await Expect(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{chatId}/knowledge/ask", owner,
                new AskAiKnowledgeRequest([knowledge.Id], embedding.Id, "到期后不得外发"), tenant ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_publication SET EffectiveToUtc=@Time WHERE Id=@Id", new { Time = request.EffectiveToUtc, Id = next.Id });
            head = await Send<AiKnowledgePublicationHeadResponse>(HttpMethod.Post, publications + "/withdraw", owner, new WithdrawAiKnowledgePublication(head.Version, next.Id));
            Assert.IsNull(head.PublicationId); Assert.AreEqual(3, head.Version);
            await Assert.ThrowsAsync<InvalidDataException>(() => Retrieve());
            await Expect(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{chatId}/knowledge/ask", owner,
                new AskAiKnowledgeRequest([knowledge.Id], embedding.Id, "下架后不得外发"), tenant ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict);
            // 撤销成员后复用旧真实令牌，目录和发布投影必须立即不可见。
            knowledge = await Send<AiKnowledgeBaseResponse>(HttpMethod.Get, root, owner);
            await Send<AiKnowledgeMembersResponse>(HttpMethod.Put, root + "/members", owner, new SetAiKnowledgeMembersRequest([], knowledge.Version));
            await Expect(HttpMethod.Get, publications + $"/{next.Id}", reviewer, null, HttpStatusCode.NotFound);
            await Expect(HttpMethod.Get, root + "/folders", reviewer, null, HttpStatusCode.NotFound);
            Console.WriteLine(JsonSerializer.Serialize(new { nativePublication = true, provider = provider.ToString(), scope = tenant ? "tenant" : "host", first.Task, successor = second.Task, modelCalls = model.Calls }));

            AsyncServiceScope Scope()
            {
                var scope = factory.Services.CreateAsyncScope(); var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                if (tenantId is { } id) writer.SetTenant(new(id, "acme", "测试租户")); else writer.SetHost(); return scope;
            }
            async Task<(Guid Version, Guid Task)> Generate(string text)
            {
                document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
                using var upload = new HttpRequestMessage(HttpMethod.Post, path + $"/versions?version={document.Version}&requestKey={Guid.CreateVersion7()}&fileName=source.txt");
                upload.Headers.Authorization = new("Bearer", owner); upload.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(text)); upload.Content.Headers.ContentType = new("text/plain");
                using var response = await client.SendAsync(upload); await Require(response, HttpStatusCode.Accepted);
                var imported = (await response.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!; Assert.AreEqual("completed", imported.State);
                var task = (await Send<JsonElement>(HttpMethod.Post, path + $"/versions/{imported.VersionId}/index", owner, null, HttpStatusCode.Accepted)).GetProperty("taskId").GetGuid();
                document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner); knowledge = await Send<AiKnowledgeBaseResponse>(HttpMethod.Get, root, owner);
                await using var scope = Scope(); var sp = scope.ServiceProvider; var binding = new AiKnowledgeIndexTaskBinding(knowledge.Id, document.Id, imported.VersionId, document.Version, knowledge.Version, actor);
                var held = await sp.GetRequiredService<AiKnowledgeIndexTaskStore>().AcquireAsync(binding, task, default); Assert.IsNotNull(held?.LeaseToken);
                Assert.IsTrue(await sp.GetRequiredService<AiKnowledgeIndexPersistenceService>().ExecuteTextGenerationAsync(binding, task, held.LeaseToken.Value, default));
                var status = await Send<AiKnowledgeIndexStatusResponse>(HttpMethod.Get, path + $"/versions/{imported.VersionId}/index", owner); Assert.AreEqual("ready", status.State);
                return (imported.VersionId, task);
            }
            async Task<AiKnowledgeRetrievalSnapshot> Retrieve()
            {
                await using var scope = Scope(); var sp = scope.ServiceProvider;
                var config = await sp.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AiModelConfigRecord>(tenant ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat, AiSqlParameters.Create(("ModelConfigId", embedding.Id)), default);
                Assert.IsNotNull(config); var vector = new float[1536]; vector[0] = 1;
                // JIT 的内部查询同样需要交互边界；只重放真实 Native 登录返回的主体，权威会话/权限仍查询真实 Identity。
                var contexts = sp.GetRequiredService<IHttpContextAccessor>(); var previous = contexts.HttpContext;
                contexts.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(payload.RootElement.EnumerateObject().SelectMany(property =>
                    property.Value.ValueKind == JsonValueKind.Array ? property.Value.EnumerateArray().Select(value => new Claim(property.Name, value.ToString()))
                        : [new Claim(property.Name, property.Value.ToString())]), "Bearer")) };
                try { return await sp.GetRequiredService<AiKnowledgeRetrievalService>().RetrieveAsync([knowledge.Id], actor, config, vector, default); }
                finally { contexts.HttpContext = previous; }
            }
            async Task<AiKnowledgeRetrievalSnapshot?> ReadDuringSwitch()
            {
                // 发布切换发生在来源查询和证明之间时允许安全拒绝；后续独立读取仍须成功绑定新发布。
                try { return await Retrieve(); } catch (InvalidDataException) { return null; }
            }
            async Task Cleanup(Guid task) { await using var scope = Scope(); await scope.ServiceProvider.GetRequiredService<AiKnowledgeIndexCleanupStore>().CleanupRetiredTaskAsync(knowledge.Id, document.Id, actor, task, DateTimeOffset.UtcNow.AddHours(-24), default); }
            Task<int> CleanupState(Guid task) => db.QuerySingleAsync<int>("SELECT CleanupState FROM fn_ai_knowledge_generation WHERE Id=@Id", new { Id = task });
            async Task<AiChatMessageResponse> ReadHistory() => (await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{chatId}", owner)).Messages.Single(message => message.Id == messageId);
            Task<AiKnowledgePublicationResponse> Review(AiKnowledgePublicationResponse value, bool approve) => Send<AiKnowledgePublicationResponse>(HttpMethod.Post, publications + $"/{value.Id}/review", reviewer, new ReviewAiKnowledgePublication(approve, value.Version, value.SnapshotHash));
            Task<AiKnowledgePublicationResponse> Publish(AiKnowledgePublicationResponse value) => Send<AiKnowledgePublicationResponse>(HttpMethod.Post, publications + $"/{value.Id}/publish", owner, new PublishAiKnowledgePublication(value.Version, value.SnapshotHash));
        }
        await api.StopGracefullyAsync(); api.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, api.ExitCode);

        async Task<T> Send<T>(HttpMethod method, string path, string token, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            using var response = await client.SendAsync(request); await Require(response, expected);
            return (await response.Content.ReadFromJsonAsync<T>())!;
        }
        async Task Expect(HttpMethod method, string path, string token, object? body, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            using var response = await client.SendAsync(request); await Require(response, expected);
        }
        static async Task Require(HttpResponseMessage response, HttpStatusCode expected) => Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
    private static JsonDocument Payload(string token)
    {
        var value = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '=')));
    }
}
