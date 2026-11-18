using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>原会话索引跨企业停用时不把不可读文件当成可计费恢复。</summary>
[TestClass]
public sealed class AiKnowledgeIndexTenantFileRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Suspended_tenant_requires_explicit_index_retry(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var root = new FilesRoot();
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(path => Path.GetFullPath(Path.Combine(path, executable))).FirstOrDefault(File.Exists);
        Assert.IsNotNull(node);
        var script = Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "scripts", "testing", "run-dotnet-test-suite.mjs");
        Assert.IsTrue(File.Exists(script));
        var settings = new Dictionary<string, string?>
        {
            ["Files:Local:RootPath"] = root.Path,
            ["FullNet:Ai:KnowledgeDocx:Enabled"] = "true",
            ["FullNet:Ai:KnowledgeDocx:NodeExecutablePath"] = node,
            ["FullNet:Ai:KnowledgeDocx:CliScriptPath"] = script,
            ["FullNet:Ai:KnowledgeDocx:ImageId"] = "sha256:" + new string('a', 64),
            ["FullNet:Ai:KnowledgeIndexWorker:Enabled"] = "true",
            ["FullNet:Ai:KnowledgeIndexWorker:BatchSize"] = "1",
        };
        using var api = new FullNetApiFactory(provider, connection, settings);
        await api.InitializeAsync();
        await using var db = AiOperationBudgetAssertions.Connection(api);
        string[] permissions = [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
            AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.SourceRead,
            AiKnowledgeDocumentPermissions.IndexEnqueue, AiKnowledgeDocumentPermissions.IndexRetry, "tenancy.tenants.switch"];
        var owner = await api.CreateHostIdentityAsync("index-recovery-" + Guid.NewGuid().ToString("N"), permissions);
        using var ownerHost = api.CreateClientForHost("localhost");
        using var client = api.CreateClientForHost("acme.localhost");
        using var management = api.CreateClientForHost("localhost");
        ownerHost.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client);
        await AiKnowledgeMembersApiAssertions.AddTenantMemberAsync(api, tenant.Id, owner.UserId);
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            await IntegrationTestTenantContextHelper.SwitchToTenantAsync(ownerHost, owner.AccessToken, tenant.Id));
        management.DefaultRequestHeaders.Authorization = new("Bearer", await api.CreateHostAccessTokenAsync(
            ["tenancy.host_tenants.read", "tenancy.tenant_lifecycle.suspend", "tenancy.tenant_lifecycle.reactivate"]));
        await using (var spareScope = api.Services.CreateAsyncScope())
        {
            spareScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var identifier = "ai-index-spare-" + Guid.NewGuid().ToString("N")[..10];
            var spare = await spareScope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(
                new ProvisionTenantRequest(identifier, "AI index spare", identifier + ".localhost"));
            Assert.IsTrue(spare.IsSuccess, spare.Error?.Code);
        }

        var row = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, true);
        var versionId = (Guid)row["VersionId"]!;
        var knowledgeId = (Guid)row["KnowledgeBaseId"]!;
        var documentId = (Guid)row["DocumentId"]!;
        var modelId = (Guid)row["ModelConfigId"]!;
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Owner,TenantId=@Tenant WHERE Id=@Id",
            new { Owner = owner.UserId, Tenant = tenant.Id, Id = knowledgeId });
        await db.ExecuteAsync("UPDATE fn_ai_model_config SET TenantId=@Tenant WHERE Id=@Id",
            new { Tenant = tenant.Id, Id = modelId });
        var bytes = Encoding.UTF8.GetBytes("synthetic source associated with the frozen document manifest");
        const string mime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        var tenantContext = new TenantContext(tenant.Id, "acme", "Acme");
        Guid fileId;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(tenantContext);
            await using var stream = new MemoryStream(bytes, writable: false);
            var upload = await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>()
                .UploadAsync("ai", versionId, owner.UserId, "source.docx", mime, stream, bytes.Length);
            Assert.IsTrue(upload.IsSuccess, upload.Error?.Code);
            fileId = upload.Value!.FileId;
        }
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET FileId=@FileId,ContentType=@Mime,ContentHash=@Hash,SizeBytes=@Size WHERE Id=@VersionId",
            new { FileId = fileId, Mime = mime, Hash = hash, Size = bytes.Length, VersionId = versionId });
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET FileId=@FileId,ContentType=@Mime,ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@VersionId",
            new { FileId = fileId, Mime = mime, Hash = hash, Size = bytes.Length, VersionId = versionId });
        var path = $"/api/v1/ai/knowledge-bases/{knowledgeId}/documents/{documentId}/versions/{versionId}/index";
        using var enqueue = await client.PostAsync(path, null);
        var body = await enqueue.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.Accepted, enqueue.StatusCode, body);
        using var json = JsonDocument.Parse(body);
        var taskId = json.RootElement.GetProperty("taskId").GetGuid();
        var binding = new AiKnowledgeIndexTaskBinding(knowledgeId, documentId, versionId, 1, 1, owner.UserId);
        var gate = new FileGate(versionId);
        var model = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory();
        using var worker = await IntegrationWorkerHostFactory.BuildAsync(provider, connection, settings,
            "Ai.Index.Recovery.Worker", nameof(AiKnowledgeIndexHostedProcessor), root.Path,
            configureServices: services => services.AddSingleton(api.Services.GetRequiredService<IDataProtectionProvider>()),
            configureTargetServices: services =>
            {
                services.RemoveAll<IAiModelClientFactory>();
                services.AddSingleton<IAiModelClientFactory>(model);
                var descriptor = services.Single(item => item.ServiceType == typeof(ITenantResourceFileStore));
                services.Remove(descriptor);
                services.AddScoped<ITenantResourceFileStore>(scope => new ControlledFiles(
                    (ITenantResourceFileStore)(descriptor.ImplementationFactory?.Invoke(scope)
                        ?? ActivatorUtilities.CreateInstance(scope, descriptor.ImplementationType!)), gate));
            });

        // 本场景从已冻结清单开始，避免把合成解析结果误报为真实 DOCX 解析。
        var text = "恢复来源内容";
        var parsed = new AiKnowledgeParsedDocx(hash.ToLowerInvariant(), bytes.Length, text + "\n",
            [new(0, "paragraph", 0, text.Length, 1, null, null, null, 0, false)]);
        var chunked = AiKnowledgeDocxModelChunker.Chunk(
            new AiKnowledgeBaseRecord { Id = knowledgeId, Version = 1, TenantId = tenant.Id, IsEnabled = true,
                DataClassification = "internal", EmbeddingModelConfigId = modelId, EmbeddingModelVersion = 1 },
            new AiModelConfigRecord { Id = modelId, Version = 1, TenantId = tenant.Id, IsEnabled = true,
                ProviderKey = "openai_compatible", ModelId = "text-embedding-3-small", EndpointBaseUrl = "https://api.openai.com/v1" },
            parsed, new(), default);
        Assert.IsTrue(chunked.IsSuccess);
        var plan = AiKnowledgeDocumentPlan.Create(chunked.Output! with { ParserRuntimeId = "sha256:" + new string('a', 64) }, default);
        Assert.IsNotNull(plan);
        await using (var scope = worker.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(tenantContext);
            var tasks = scope.ServiceProvider.GetRequiredService<AiKnowledgeIndexTaskStore>();
            var held = await tasks.AcquireAsync(binding, taskId, default);
            Assert.IsNotNull(held?.LeaseToken);
            Assert.IsTrue(await scope.ServiceProvider.GetRequiredService<AiKnowledgeIndexDocumentManifestStore>()
                .PersistAsync(binding, taskId, held.LeaseToken.Value, plan, default));
            Assert.IsTrue(await tasks.FailAsync(binding, taskId, held.LeaseToken.Value, AiKnowledgeIndexFailure.IndexUnavailable, default));
        }
        var sessionId = await db.QuerySingleAsync<Guid>(
            "SELECT SessionId FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@Id", new { Id = taskId });
        var manifestHash = await db.QuerySingleAsync<string>(
            "SELECT PayloadHash FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id = taskId });
        await RetryAsync(HttpStatusCode.Accepted);
        var pending = RunWorkerAsync();
        TenantSummary? suspended = null;
        try
        {
            if (await Task.WhenAny(gate.Entered.Task, pending).WaitAsync(TimeSpan.FromSeconds(40)) == pending)
                Assert.Fail($"Worker ended before file list: processed={await pending}, state={await StateAsync()}, failure={await FailureAsync()}");
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(40));
            using var currentResponse = await management.GetAsync($"/api/v1/tenancy/tenants/{tenant.Id:D}");
            Assert.AreEqual(HttpStatusCode.OK, currentResponse.StatusCode);
            var current = await currentResponse.Content.ReadFromJsonAsync<TenantSummary>();
            Assert.IsNotNull(current);
            using var response = await management.PostAsJsonAsync($"/api/v1/tenancy/tenants/{tenant.Id:D}/suspend",
                new SuspendTenantRequest(current.Version));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            suspended = await response.Content.ReadFromJsonAsync<TenantSummary>();
            Assert.IsNotNull(suspended);
            gate.Release.TrySetResult();
            await gate.Unavailable.Task.WaitAsync(TimeSpan.FromSeconds(40));
            Assert.AreEqual(1, await pending.WaitAsync(TimeSpan.FromSeconds(40)));
        }
        finally
        {
            gate.Release.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(40)); } catch { }
        }
        Assert.AreEqual("failed", await StateAsync());
        Assert.AreEqual("ai.knowledge.index_unavailable", await db.QuerySingleAsync<string>(
            "SELECT FailureCode FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = taskId }));
        await AssertOriginalIdentityAsync();
        await AssertLeaseReleasedAsync();
        Assert.AreEqual(0, model.Calls);
        Assert.AreEqual(0L, await BudgetCountAsync());
        using (var response = await management.PostAsJsonAsync($"/api/v1/tenancy/tenants/{tenant.Id:D}/reactivate",
            new ReactivateTenantRequest(suspended!.Version)))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(0, await RunWorkerAsync());
        Assert.AreEqual(0, model.Calls);
        await RetryAsync(HttpStatusCode.Accepted);
        await AssertOriginalIdentityAsync();
        Assert.AreEqual(1, await RunWorkerAsync());
        Assert.AreEqual("completed", await StateAsync());
        Assert.AreEqual(1, model.Calls);
        Assert.AreEqual(1L, await BudgetCountAsync());
        Assert.AreEqual("succeeded", await db.QuerySingleAsync<string>(
            "SELECT Outcome FROM fn_ai_operation_budget WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id)", new { Id = taskId }));
        Assert.AreEqual("known", await db.QuerySingleAsync<string>(
            "SELECT UsageStatus FROM fn_ai_operation_budget WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id)", new { Id = taskId }));
        Assert.AreEqual(7L, await db.QuerySingleAsync<long>(
            "SELECT ChargedTokens FROM fn_ai_operation_budget WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id)", new { Id = taskId }));
        await AssertOriginalIdentityAsync();
        await AssertLeaseReleasedAsync();
        Assert.AreEqual(0, await RunWorkerAsync());
        Assert.AreEqual(1, model.Calls);
        await using (var scope = api.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(tenantContext);
            var ready = await scope.ServiceProvider.GetRequiredService<ITenantResourceFileStore>().ListReadyAsync("ai", versionId);
            Assert.HasCount(1, ready);
            Assert.AreEqual(fileId, ready.Single().FileId);
        }

        async Task RetryAsync(HttpStatusCode expected)
        {
            using var response = await client.PostAsync($"{path}/{taskId}/retry", null);
            Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        async Task<int> RunWorkerAsync()
        {
            await using var scope = worker.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            return await scope.ServiceProvider.GetRequiredService<AiKnowledgeIndexWorkerCoordinator>().ProcessPendingAsync(default);
        }
        Task<string> StateAsync() => db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = taskId });
        Task<string?> FailureAsync() => db.QuerySingleOrDefaultAsync<string>("SELECT FailureCode FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = taskId });
        Task<long> BudgetCountAsync() => db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id IN (SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id)", new { Id = taskId });
        async Task AssertOriginalIdentityAsync()
        {
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM fn_ai_knowledge_index_task AS task INNER JOIN fn_ai_knowledge_index_session AS binding ON binding.IndexTaskId=task.Id INNER JOIN fn_ai_knowledge_version AS source ON source.Id=task.VersionId INNER JOIN fn_ai_knowledge_document_manifest AS manifest ON manifest.Id=task.Id WHERE task.Id=@TaskId AND task.VersionId=@VersionId AND source.FileId=@FileId AND binding.SessionId=@SessionId AND binding.ActorUserId=@Actor AND binding.TenantId=@Tenant AND manifest.PayloadHash=@ManifestHash",
                new { TaskId = taskId, VersionId = versionId, FileId = fileId, SessionId = sessionId, Actor = owner.UserId, Tenant = tenant.Id, ManifestHash = manifestHash }));
        }
        async Task AssertLeaseReleasedAsync()
        {
            Assert.AreEqual(1L, await db.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE Id=@Id AND LeaseToken IS NULL AND LeaseExpiresAtUtc IS NULL", new { Id = taskId }));
        }
    }

    private sealed class FilesRoot : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fullnet-ai-index-recovery-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
    private sealed class FileGate(Guid versionId)
    {
        internal Guid VersionId { get; } = versionId;
        internal int Paused;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Unavailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class ControlledFiles(ITenantResourceFileStore inner, FileGate gate) : ITenantResourceFileStore
    {
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId,
            string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default) =>
            inner.UploadAsync(ownerModuleKey, resourceId, actorUserId, originalFileName, contentType, content, contentLength, cancellationToken);
        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
            CancellationToken cancellationToken = default) => inner.OpenReadyContentAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
        public async Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default)
        {
            if (ownerModuleKey != "ai" || resourceId != gate.VersionId || Interlocked.Exchange(ref gate.Paused, 1) != 0)
                return await inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken);
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(cancellationToken);
            try { return await inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken); }
            catch (TenantResourceFileUnavailableException) { gate.Unavailable.TrySetResult(); throw; }
        }
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
    }
}
