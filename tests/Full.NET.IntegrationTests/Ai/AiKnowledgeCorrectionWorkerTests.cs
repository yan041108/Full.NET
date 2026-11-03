using System.Security.Cryptography;
using System.Text;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.AI.Abstractions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库 Worker 扫描到人工归档；解析输出、Identity/Files 及模型为显式夹具，不冒充实际 PDF 解析或 Native。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionWorkerTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Worker_routes_frozen_corrections_without_parser_and_preserves_scope_failure_and_replay(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var controls = new Controls(); using var factory = new FullNetApiFactory(provider, connection, configureTestServices: controls.Register);
        await factory.InitializeAsync(); await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        foreach (var scoped in new[] { false, true })
        {
            var seed = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            var binding = new AiKnowledgeIndexTaskBinding((Guid)seed["KnowledgeBaseId"]!, (Guid)seed["DocumentId"]!, (Guid)seed["VersionId"]!, 1, 1, (Guid)seed["OwnerUserId"]!);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)seed["TenantId"], Guid.CreateVersion7(), "worker-correction", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            var bytes = Encoding.UTF8.GetBytes("explicit PDF parser output fixture"); var hash = Hash(bytes);
            controls.Current = new(session, (Guid)seed["FileId"]!, binding.VersionId, bytes.Length, hash); controls.Denied = false; controls.Ready = true;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET OriginalFileName='a.pdf',ContentType='application/pdf',ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id; UPDATE fn_ai_knowledge_import SET OriginalFileName='a.pdf',ContentType='application/pdf',ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = binding.VersionId });
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var writer = sp.GetRequiredService<ICurrentTenantContextWriter>();
            if (session.TenantId is { } tenantId) writer.SetTenant(new(tenantId, "correction-worker", "人工任务")); else writer.SetHost();
            var tenant = sp.GetRequiredService<ICurrentTenant>(); var tasks = sp.GetRequiredService<AiKnowledgeIndexTaskStore>();
            var sessions = sp.GetRequiredService<AiKnowledgeIndexSessionService>(); var store = sp.GetRequiredService<AiKnowledgeCorrectionStore>();
            Assert.IsFalse(sp.GetRequiredService<IOptions<AiKnowledgePdfRuntimeOptions>>().Value.Enabled);
            Assert.IsFalse(sp.GetRequiredService<IOptions<AiKnowledgeDocxRuntimeOptions>>().Value.Enabled);
            Assert.IsFalse(sp.GetRequiredService<IOptions<AiKnowledgeDocumentRuntimeOptions>>().Value.Enabled);
            var calls = controls.Calls;
            var raw = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(raw);
            Assert.AreEqual(0, await Run()); Assert.AreEqual(calls, controls.Calls);
            Assert.AreEqual("queued", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = raw.Value }));
            var parsed = AiKnowledgeParsedDocument.FromPdf(new(hash, bytes.Length, [new(1, "原解析第一页"), new(2, "原解析第二页")]), default)!;
            var sourceBinding = new AiKnowledgeCorrectionStore.Binding(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, binding.OwnerUserId, 1);
            var origin = await store.CreateSourceAsync(sourceBinding, Guid.CreateVersion7(), Hash("prepare"u8.ToArray()), parsed, "fixture:pdf-v1", default); Assert.IsTrue(origin.IsSuccess);
            var draft = (await store.ReadCurrentAsync(sourceBinding, parsed.Format, hash, bytes.Length, parsed.Policy, default))!.Document;
            var first = draft.Blocks[0]; draft = draft.Replace(first.Id, first.Parts[0].Id, "经过人工校对的第一页", default);
            for (var i = 0; i < 16; i++) draft = draft.Append(null, $"明确补写第 {i} 项", Guid.CreateVersion7, default);
            var saved = await store.SaveAsync(sourceBinding, origin.Value!.Id, 0, Guid.CreateVersion7(), Hash("save"u8.ToArray()), "人工索引任务", draft, origin.Value.ParserRuntimeId, default); Assert.IsTrue(saved.IsSuccess);
            sourceBinding = sourceBinding with { DocumentVersion = 2 }; binding = binding with { DocumentVersion = 2 };
            var task = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(task);
            var originalPayload = await db.QuerySingleAsync<byte[]>("SELECT OriginalPayload FROM fn_ai_knowledge_correction_source WHERE Id=@Id", new { Id = origin.Value.Id });
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id = task.Value }));
            // 两域分别覆盖原会话权限及 Files 撤销，失败不生成预算操作或模型调用。
            controls.Denied = !scoped; controls.Ready = !scoped;
            Assert.AreEqual(1, await Run()); Assert.AreEqual(calls, controls.Calls);
            Assert.AreEqual("failed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = task.Value }));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@Id", new { Id = task.Value }));
            controls.Denied = false; controls.Ready = true; Assert.IsTrue(await tasks.RetryAsync(binding, task.Value, default));
            Assert.AreEqual(1, await Run()); Assert.AreEqual(calls + 2, controls.Calls);
            Assert.AreEqual("completed", await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = task.Value }));
            Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch AS batch INNER JOIN fn_ai_operation_budget AS ledger ON ledger.Id=batch.Id WHERE batch.IndexTaskId=@Id AND ledger.Outcome='succeeded' AND ledger.SettledAtUtc IS NOT NULL", new { Id = task.Value }));
            Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id WHERE batch.IndexTaskId=@Id AND vector.FormatKey='knowledge-correction-vector-v1'", new { Id = task.Value }));
            Assert.AreEqual(0, await Run()); Assert.AreEqual(calls + 2, controls.Calls); Assert.AreEqual(session.TenantId, tenant.Id);
            CollectionAssert.AreEqual(originalPayload, await db.QuerySingleAsync<byte[]>("SELECT OriginalPayload FROM fn_ai_knowledge_correction_source WHERE Id=@Id", new { Id = origin.Value.Id }));
            var next = await store.SaveAsync(sourceBinding, origin.Value.Id, 1, Guid.CreateVersion7(), Hash("next"u8.ToArray()), "后续人工修订", draft, origin.Value.ParserRuntimeId, default); Assert.IsTrue(next.IsSuccess);
            sourceBinding = sourceBinding with { DocumentVersion = 3 }; binding = binding with { DocumentVersion = 3 };
            var stale = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(stale);
            var latest = await store.SaveAsync(sourceBinding, origin.Value.Id, 2, Guid.CreateVersion7(), Hash("latest"u8.ToArray()), "旧排队任务不能追随新头", draft, origin.Value.ParserRuntimeId, default); Assert.IsTrue(latest.IsSuccess);
            Assert.AreEqual(0, await Run()); Assert.AreEqual(calls + 2, controls.Calls);
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT AttemptCount FROM fn_ai_knowledge_index_task WHERE Id=@Id", new { Id = stale.Value }));
            binding = binding with { DocumentVersion = 4 }; var fresh = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(fresh);
            Assert.AreEqual(1, await Run()); Assert.AreEqual(calls + 4, controls.Calls); Assert.AreEqual(0, await Run());
            Assert.AreEqual(fresh.Value, await db.QuerySingleAsync<Guid>("SELECT IndexTaskId FROM fn_ai_knowledge_active_generation WHERE DocumentId=@Id", new { Id = binding.DocumentId }));
            Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_generation WHERE DocumentId=@Id", new { Id = binding.DocumentId }));
            Assert.AreEqual(0, controls.Opens); Assert.AreEqual(session.TenantId, tenant.Id);

            async Task<int> Run()
            {
                await using var loop = factory.Services.CreateAsyncScope(); var loopTenant = loop.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>(); loopTenant.SetHost();
                var count = await loop.ServiceProvider.GetRequiredService<AiKnowledgeIndexWorkerCoordinator>().ProcessPendingAsync(default);
                Assert.IsTrue(loop.ServiceProvider.GetRequiredService<ICurrentTenant>().IsHost); return count;
            }
        }
    }

    private sealed record Input(SessionBindingSnapshot Session, Guid File, Guid Version, long Size, string Hash);
    private sealed class Controls
    {
        internal Input Current = null!; internal bool Denied, Ready = true; internal int Opens;
        private readonly List<AiKnowledgeCorrectionVectorTests.Model> models = [];
        internal int Calls => models.Sum(m => m.Calls);
        internal void Register(IServiceCollection services)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [$"{AiKnowledgeIndexWorkerOptions.SectionName}:Enabled"] = "true" }).Build();
            new AiModule().AddBackgroundServices(services, config);
            services.Configure<AiKnowledgeIndexWorkerOptions>(o => { o.Enabled = true; o.BatchSize = 4; });
            services.Configure<AiKnowledgePdfRuntimeOptions>(o => o.Enabled = false); services.Configure<AiKnowledgeDocxRuntimeOptions>(o => o.Enabled = false);
            services.Configure<AiKnowledgeDocumentRuntimeOptions>(o => o.Enabled = false);
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService)
                && (d.ImplementationType == typeof(AiKnowledgeIndexHostedProcessor) || d.ImplementationType == typeof(AiAgentRunHostedProcessor))).ToArray()) services.Remove(descriptor);
            services.AddScoped(p => new Ports(this, p.GetRequiredService<IDataTransactionState>(), p.GetRequiredService<ICurrentTenant>()));
            services.RemoveAll<IBackgroundSessionAuthorization>(); services.AddScoped<IBackgroundSessionAuthorization>(p => p.GetRequiredService<Ports>());
            services.RemoveAll<IHostFileReferenceReader>(); services.AddScoped<IHostFileReferenceReader>(p => p.GetRequiredService<Ports>());
            services.RemoveAll<ITenantResourceFileDescriptorReader>(); services.AddScoped<ITenantResourceFileDescriptorReader>(p => p.GetRequiredService<Ports>());
            services.RemoveAll<IHostFileContentReader>(); services.AddScoped<IHostFileContentReader>(p => p.GetRequiredService<Ports>());
            services.RemoveAll<ITenantResourceFileStore>(); services.AddScoped<ITenantResourceFileStore>(p => p.GetRequiredService<Ports>());
            services.RemoveAll<IAiModelClientFactory>(); services.AddScoped<IAiModelClientFactory>(p =>
            { var model = new AiKnowledgeCorrectionVectorTests.Model(p.GetRequiredService<IDataTransactionState>()); models.Add(model); return model; });
        }
    }
    private sealed class Ports(Controls controls, IDataTransactionState state, ICurrentTenant tenant) : IBackgroundSessionAuthorization,
        IHostFileReferenceReader, ITenantResourceFileDescriptorReader, IHostFileContentReader, ITenantResourceFileStore
    {
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default) => AuthorizeAllAsync(binding, [permissionCode], cancellationToken);
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(SessionBindingSnapshot binding, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default)
        { Check(cancellationToken); return Task.FromResult<AuthorizedSessionActor?>(binding == controls.Current.Session && !controls.Denied ? new(binding.UserId, binding.TenantId, binding.SessionId) : null); }
        public Task<HostFileReference?> GetReadyReferenceAsync(Guid fileId, CancellationToken cancellationToken = default)
        { Check(cancellationToken); Assert.IsTrue(tenant.IsHost); Assert.AreEqual(controls.Current.File, fileId); return Task.FromResult<HostFileReference?>(controls.Ready ? new(fileId, controls.Current.Size, controls.Current.Hash) : null); }
        public Task<TenantResourceFileDescriptor?> GetReadyDescriptorAsync(string module, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
        { Check(cancellationToken); Assert.IsFalse(tenant.IsHost); Assert.AreEqual("ai", module); Assert.AreEqual(controls.Current.Version, resourceId); Assert.AreEqual(controls.Current.File, fileId);
            return Task.FromResult<TenantResourceFileDescriptor?>(controls.Ready ? new(fileId, "a.pdf", "application/pdf", controls.Current.Size, controls.Current.Hash, controls.Current.Session.UserId) : null); }
        public Task<Result<HostFileContent>> OpenReadyContentAsync(Guid fileId, CancellationToken cancellationToken = default) { controls.Opens++; throw new AssertFailedException("Frozen correction must not reopen source content."); }
        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) { controls.Opens++; throw new AssertFailedException("Frozen correction must not reopen source content."); }
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        private void Check(CancellationToken token) { token.ThrowIfCancellationRequested(); Assert.IsFalse(state.HasTransaction); Assert.AreEqual(controls.Current.Session.TenantId, tenant.Id); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
