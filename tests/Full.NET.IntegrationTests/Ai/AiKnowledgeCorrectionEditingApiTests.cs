using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实 Identity/Files、准备 Worker 和双库 HTTP 编辑；保存与恢复不触发索引、收费或发布。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionEditingApiTests
{
    [TestMethod] public Task SqlServer_public_edit_restore_and_history_preserve_cas_provenance_and_replay() => Verify(DatabaseProvider.SqlServer);
    [TestMethod] public Task MySql_public_edit_restore_and_history_preserve_cas_provenance_and_replay() => Verify(DatabaseProvider.MySql);
    internal static async Task Verify(DatabaseProvider provider, NativeApiArtifact? nativeArtifact = null)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = nativeArtifact is null ? null : new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-correction-edit-keys", Guid.CreateVersion7().ToString("N")),
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-correction-edit-files", Guid.CreateVersion7().ToString("N"))
        };
        using var factory = new FullNetApiFactory(provider, connection, settingsOverrides: settings); await factory.InitializeAsync();
        await using var native = nativeArtifact is null ? null : await NativeApiProcessHost.StartAsync(nativeArtifact, provider, connection, settings!, NativeAotTestTimeouts.ProcessStartup);
        if (native is not null) Console.WriteLine($"Native correction API log: {native.LogFilePath}");
        HttpClient Client(string host) => native?.CreateClient(host) ?? factory.CreateClientForHost(host);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        foreach (var scoped in new[] { false, true })
        {
            var permissions = new[] { AiKnowledgePermissions.Read, AiKnowledgePermissions.Create, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.Create,
                AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead,
                AiKnowledgeCorrectionPermissions.Read, AiKnowledgeCorrectionPermissions.Prepare, AiKnowledgeCorrectionPermissions.Edit, AiKnowledgeCorrectionPermissions.Restore, "tenancy.tenants.switch" };
            var username = "correction-edit-" + Guid.NewGuid().ToString("N");
            var owner = await factory.CreateHostIdentityAsync(username, permissions, password: FullNetApiFactory.TestPassword);
            using var host = Client("localhost");
            var hostAccess = native is null ? owner.AccessToken : await IntegrationTestAuthHelper.LoginAsHostUserAsync(host, username);
            host.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostAccess);
            using var client = Client(scoped ? "acme.localhost" : "localhost");
            var access = hostAccess;
            if (scoped) { var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client); access = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host, hostAccess, tenant.Id); }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var createdBase = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("公开编辑", null));
            Assert.AreEqual(HttpStatusCode.Created, createdBase.StatusCode); var knowledge = (await createdBase.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
            var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}/documents";
            using var createdDoc = await client.PostAsJsonAsync(path, new CreateAiKnowledgeDocumentRequest("原文保留", null));
            var document = (await createdDoc.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!; path += "/" + document.Id;
            const string text = "原文😀内容及真实位置";
            using var upload = await client.PostAsync(path + $"/versions?version=1&requestKey={Guid.CreateVersion7()}&fileName=source.txt", new StringContent(text, Encoding.UTF8, "text/plain"));
            Assert.AreEqual(HttpStatusCode.Accepted, upload.StatusCode);
            var version = (await upload.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.VersionId;
            var root = path + $"/versions/{version}/corrections";
            using var prepare = await client.PostAsJsonAsync(root + "/prepare", new AiKnowledgeCorrectionPrepareRequest(Guid.CreateVersion7(), 2));
            Assert.AreEqual(HttpStatusCode.Accepted, prepare.StatusCode, await prepare.Content.ReadAsStringAsync());
            var task = (await prepare.Content.ReadFromJsonAsync<AiKnowledgeCorrectionPreparationResponse>())!;
            using (var worker = await IntegrationWorkerHostFactory.BuildAsync(provider, connection, new Dictionary<string, string?>
            {
                [$"{AiKnowledgeIndexWorkerOptions.SectionName}:CorrectionPreparationEnabled"] = "true",
                [$"{AiKnowledgeIndexWorkerOptions.SectionName}:PollMilliseconds"] = "250"
            }, "CorrectionEditSource", nameof(AiKnowledgeIndexHostedProcessor), factory.Services.GetRequiredService<IConfiguration>()["Files:Local:RootPath"]))
            {
                await worker.StartAsync();
                try { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45)); while (await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_correction_preparation WHERE Id=@Id", new { task.Id }) != "completed") await Task.Delay(100, deadline.Token); }
                finally { await worker.StopAsync(); }
            }
            var original = await Read(); var current = original;
            var firstRequest = Request(new("replace", current.Block.Id, current.Block.Parts[0].Id, "人工更正", null, null)) with { Reason = "\uFEFF 核对说明 \uFEFF" };
            var first = await Edit(firstRequest); current = await Read();
            Assert.AreEqual(first.Id, current.RevisionId); Assert.AreEqual(firstRequest.Reason, first.Reason); Assert.AreEqual(3, current.DocumentVersion); Assert.AreEqual(text, current.Block.Parts[0].OriginalText);
            Assert.AreEqual(first, await Edit(firstRequest), "原请求响应丢失后不会因自身版本增加而重复保存。");
            using (var mismatch = await client.PostAsJsonAsync(root + "/revisions", firstRequest with { Reason = "另一保存意图" })) Assert.AreEqual(HttpStatusCode.Conflict, mismatch.StatusCode);
            using (var stale = await client.PostAsJsonAsync(root + "/revisions", firstRequest with { RequestKey = Guid.CreateVersion7() })) Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
            using (var injection = await client.PostAsJsonAsync(root + "/revisions", new { requestKey = Guid.CreateVersion7(), expected = Head(current), reason = "不接收原文锚点", operation = new { kind = "replace", blockId = current.Block.Id, partId = current.Block.Parts[0].Id, text = "注入", offset = (int?)null, excluded = (bool?)null, anchorId = Guid.CreateVersion7() } })) Assert.AreEqual(HttpStatusCode.BadRequest, injection.StatusCode);
            for (var race = 0; race < 4; race++)
            {
                current = await Read();
                var sameHead = Request(new("replace", current.Block.Id, current.Block.Parts[0].Id, "并发甲", null, null));
                var competing = sameHead with { RequestKey = Guid.CreateVersion7(), Operation = sameHead.Operation with { Text = "并发乙" } };
                var responses = await Task.WhenAll(client.PostAsJsonAsync(root + "/revisions", sameHead), client.PostAsJsonAsync(root + "/revisions", competing));
                try { CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(r => r.StatusCode).ToArray(),
                    string.Join(" | ", await Task.WhenAll(responses.Select(async r => $"{r.StatusCode}: {await r.Content.ReadAsStringAsync()}")))); }
                finally { foreach (var response in responses) response.Dispose(); }
            }
            current = await Read(); await Edit(Request(new("append", current.Block.Id, null, "明确人工补写", null, null)));
            var supplement = await Read(1); Assert.AreEqual("supplement", supplement.Block.Parts[0].ContentKind); Assert.IsNull(supplement.Block.Parts[0].SourceLocation);
            current = await Read(); await Edit(Request(new("merge", current.Block.Id, null, null, null, null)));
            current = await Read(); Assert.AreEqual(2, current.Block.Parts.Count); Assert.IsNull(current.Block.Parts[1].SourceLocation);
            await Edit(Request(new("split", current.Block.Id, current.Block.Parts[0].Id, null, 1, null)));
            current = await Read(); await Edit(Request(new("exclude", current.Block.Id, null, null, null, true)));
            current = await Read(); Assert.IsTrue(current.Block.Excluded);
            await Edit(Request(new("exclude", current.Block.Id, null, null, null, false)));
            current = await Read(); await Edit(Request(new("confirm", current.Block.Id, current.Block.Parts[0].Id, null, null, null)));
            current = await Read();
            // 历史预览不切换头；恢复写新的身份、父链、说明及当前文档版本。
            var historical = (await client.GetFromJsonAsync<AiKnowledgeCorrectionWorkspaceResponse>(root + "/workspace?revisionId=" + first.Id))!;
            Assert.AreEqual("人工更正", historical.Block.Parts[0].Text); Assert.AreEqual(current.RevisionId, (await Read()).RevisionId);
            var restoreRequest = new AiKnowledgeCorrectionRestoreRequest(Guid.CreateVersion7(), Head(current), "恢复首份人工修订", first.SourceId, first.Id);
            var restored = await Restore(restoreRequest); Assert.AreNotEqual(first.Id, restored.Id); Assert.AreEqual(current.RevisionId, restored.ParentId);
            Assert.AreEqual(restored, await Restore(restoreRequest)); current = await Read(); Assert.AreEqual("人工更正", current.Block.Parts[0].Text);
            var reset = await Restore(new(Guid.CreateVersion7(), Head(current), "显式采用原解析", original.SourceId, null));
            current = await Read(); Assert.AreEqual(reset.Id, current.RevisionId); Assert.AreEqual(text, current.Block.Parts[0].Text);
            for (var i = 0; i < 13; i++) { await Edit(Request(new("replace", current.Block.Id, current.Block.Parts[0].Id, "历史分页 " + i, null, null))); current = await Read(); }
            var history = (await client.GetFromJsonAsync<AiKnowledgeCorrectionRevisionPageResponse>(root + "/revisions"))!;
            Assert.AreEqual(20, history.Items.Count); Assert.IsNotNull(history.NextHeadVersion);
            Assert.IsTrue(history.Items.Zip(history.Items.Skip(1)).All(p => p.First.HeadVersion > p.Second.HeadVersion));
            var older = (await client.GetFromJsonAsync<AiKnowledgeCorrectionRevisionPageResponse>(root + "/revisions?beforeHeadVersion=" + history.NextHeadVersion))!;
            Assert.IsTrue(older.Items.Count > 0); Assert.IsFalse(history.Items.Select(r => r.Id).Intersect(older.Items.Select(r => r.Id)).Any());
            using (var invalidCursor = await client.GetAsync(root + "/revisions?beforeHeadVersion=0")) Assert.AreEqual(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
            using (var unchanged = await client.GetAsync(path + $"/versions/{version}/source")) Assert.AreEqual(text, await unchanged.Content.ReadAsStringAsync());
            var readName = "correction-reader-" + Guid.NewGuid().ToString("N");
            var readIdentity = await factory.CreateHostIdentityAsync(readName, AiKnowledgeCorrectionWorkbenchService.ReadPermissions, password: FullNetApiFactory.TestPassword);
            using var denied = Client("localhost");
            var readOnly = native is null ? readIdentity.AccessToken : await IntegrationTestAuthHelper.LoginAsHostUserAsync(denied, readName);
            denied.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readOnly);
            using (var noEdit = await denied.PostAsJsonAsync(root + "/revisions", Request(new("replace", current.Block.Id, current.Block.Parts[0].Id, "越权", null, null)))) Assert.AreEqual(HttpStatusCode.Forbidden, noEdit.StatusCode);
            using (var noRestore = await denied.PostAsJsonAsync(root + "/restore", restoreRequest)) Assert.AreEqual(HttpStatusCode.Forbidden, noRestore.StatusCode);
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE VersionId=@Id", new { Id = version }));
            var session = await db.QuerySingleAsync<Guid>("SELECT SessionId FROM fn_ai_knowledge_correction_preparation WHERE Id=@Id", new { task.Id });
            await db.ExecuteAsync("UPDATE fn_identity_refresh_session SET RevokedAtUtc=@Now WHERE Id=@Id", new { Now = DateTime.UtcNow, Id = session });
            using (var revoked = await client.GetAsync(root + "/workspace")) Assert.AreEqual(HttpStatusCode.Unauthorized, revoked.StatusCode);
            AiKnowledgeCorrectionEditRequest Request(AiKnowledgeCorrectionEditOperation op) => new(Guid.CreateVersion7(), Head(current), "人工核对", op);
            async Task<AiKnowledgeCorrectionWorkspaceResponse> Read(int block = 0) => (await client.GetFromJsonAsync<AiKnowledgeCorrectionWorkspaceResponse>(root + "/workspace?block=" + block))!;
            async Task<AiKnowledgeCorrectionRevisionResponse> Edit(AiKnowledgeCorrectionEditRequest request)
            {
                using var response = await client.PostAsJsonAsync(root + "/revisions", request);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync()); Assert.IsTrue(response.Headers.CacheControl!.NoStore);
                return (await response.Content.ReadFromJsonAsync<AiKnowledgeCorrectionRevisionResponse>())!;
            }
            async Task<AiKnowledgeCorrectionRevisionResponse> Restore(AiKnowledgeCorrectionRestoreRequest request)
            {
                using var response = await client.PostAsJsonAsync(root + "/restore", request);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
                return (await response.Content.ReadFromJsonAsync<AiKnowledgeCorrectionRevisionResponse>())!;
            }
        }
        if (native is not null)
        {
            await native.StopGracefullyAsync(); native.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, native.ExitCode);
        }
    }
    private static AiKnowledgeCorrectionHeadRequest Head(AiKnowledgeCorrectionWorkspaceResponse value) =>
        new(value.DocumentVersion, value.SourceId, value.RevisionId, value.HeadVersion, value.SourceHash, value.PayloadHash);
}
