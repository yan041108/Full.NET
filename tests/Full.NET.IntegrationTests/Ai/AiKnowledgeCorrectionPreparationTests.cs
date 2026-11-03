using System.Security.Cryptography;
using System.Text;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实解析、双库保存和当前权限边界；Identity/Files 是明确控制的 Port，不冒充 HTTP 或真实沙箱验收。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionPreparationTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Authorized_source_preparation_is_local_replayable_and_never_overwrites_human_heads(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection); await factory.InitializeAsync();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        var operations = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        foreach (var scoped in new[] { false, true })
        {
            var seed = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            var binding = new AiKnowledgeCorrectionStore.Binding((Guid)seed["KnowledgeBaseId"]!, (Guid)seed["DocumentId"]!, (Guid)seed["VersionId"]!, (Guid)seed["OwnerUserId"]!, 1);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)seed["TenantId"], Guid.CreateVersion7(), "current-stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            const string text = "带 BOM 的来源\r\n保持原始位置 😀"; var bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            var hash = Hash(bytes); var file = (Guid)seed["FileId"]!;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size,OriginalFileName='source.txt' WHERE Id=@Id", new { Hash = hash, Size = bytes.Length, Id = binding.VersionId });
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var writer = sp.GetRequiredService<ICurrentTenantContextWriter>(); SetScope();
            var controls = new Controls(sp.GetRequiredService<IDataTransactionState>(), sp.GetRequiredService<ICurrentTenant>(), session, file, binding.VersionId, bytes, hash);
            var store = sp.GetRequiredService<AiKnowledgeCorrectionStore>();
            var authority = new AiKnowledgeCorrectionAuthority(sp.GetRequiredService<IQueryExecutor>(), sp.GetRequiredService<IDataTransactionState>(), sp.GetRequiredService<ICurrentTenant>(), controls, controls, controls);
            var prepare = new AiKnowledgeCorrectionPreparationService(authority, store, sp.GetRequiredService<IDataTransactionState>(), controls, controls);
            var reader = new AiKnowledgeCorrectionWorkspaceReader(authority, store);
            var key = Guid.CreateVersion7(); var source = await prepare.PrepareAsync(binding, session, key, default); Assert.IsNotNull(source);
            Assert.AreEqual(1, controls.Opens); Assert.IsTrue(controls.LastStream!.Closed);
            var replay = await prepare.PrepareAsync(binding, session, key, default); Assert.IsNotNull(replay);
            Assert.AreEqual(source.Id, replay.Id); Assert.AreEqual(1, controls.Opens, "同身份重试不再次读取正文。");
            var workspace = await reader.ReadCurrentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session, default); Assert.IsNotNull(workspace);
            Assert.AreEqual(text, workspace.Snapshot.Document.Source.Blocks[0].Text); Assert.AreEqual(hash, workspace.Snapshot.Document.Source.ContentHash);
            Assert.AreEqual(bytes.Length, workspace.Snapshot.Document.Source.SizeBytes); Assert.AreEqual("managed:utf8-text-v1", workspace.Snapshot.ParserRuntimeId);
            var document = workspace.Snapshot.Document; var block = document.Blocks[0];
            var saved = await store.SaveAsync(binding, source.Id, 0, Guid.CreateVersion7(), Hash("correction"u8.ToArray()), "人工校对",
                document.Replace(block.Id, block.Parts[0].Id, "已校对的识别内容", default), source.ParserRuntimeId, default);
            Assert.IsTrue(saved.IsSuccess); binding = binding with { DocumentVersion = 2 };
            var reparseKey = Guid.CreateVersion7(); var reparse = await prepare.PrepareAsync(binding, session, reparseKey, default); Assert.IsNotNull(reparse);
            Assert.AreNotEqual(source.Id, reparse.Id);
            var current = await reader.ReadCurrentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session, default); Assert.IsNotNull(current);
            Assert.AreEqual(saved.Value!.Id, current.Snapshot.Identity.RevisionId); Assert.AreEqual("已校对的识别内容", current.Snapshot.Document.Blocks[0].Parts[0].Text);
            var selected = await reader.ReadSourceAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, reparse.Id, session, default);
            Assert.IsNotNull(selected); Assert.IsNull(selected.Snapshot.Identity.RevisionId); Assert.AreEqual(text, selected.Snapshot.Document.Blocks[0].Parts[0].Text);
            var count = await SourceCount(); var opens = controls.Opens;

            controls.Session = session with { SessionId = Guid.CreateVersion7() };
            Assert.IsNull(await prepare.PrepareAsync(binding, controls.Session, reparseKey, default), "新登录不能接管旧准备请求。");
            Assert.AreEqual(opens, controls.Opens); controls.Session = session;
            controls.DeniedPermission = AiKnowledgeCorrectionPermissions.Prepare;
            Assert.IsNull(await prepare.PrepareAsync(binding, session, Guid.CreateVersion7(), default));
            Assert.IsNotNull(await reader.ReadCurrentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session, default));
            controls.DeniedPermission = AiKnowledgeCorrectionPermissions.Read;
            Assert.IsNull(await reader.ReadCurrentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session, default));
            Assert.IsNull(await prepare.PrepareAsync(binding, session, Guid.CreateVersion7(), default));
            controls.DeniedPermission = null; Assert.AreEqual(opens, controls.Opens);

            controls.MetadataReady = false;
            Assert.IsNull(await reader.ReadCurrentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session, default));
            Assert.IsNull(await prepare.PrepareAsync(binding, session, Guid.CreateVersion7(), default));
            Assert.AreEqual(opens, controls.Opens); controls.MetadataReady = true;
            controls.BeforeOpen = () => { controls.DeniedPermission = AiKnowledgeCorrectionPermissions.Read; return Task.CompletedTask; };
            Assert.IsNull(await prepare.PrepareAsync(binding, session, Guid.CreateVersion7(), default));
            Assert.IsTrue(controls.LastStream!.Closed); Assert.AreEqual(0, controls.LastStream.Reads, "开流期间撤权时不得消费正文。");
            controls.BeforeOpen = null; controls.DeniedPermission = null;
            controls.Content = Encoding.UTF8.GetBytes(new string('x', bytes.Length));
            Assert.IsNull(await prepare.PrepareAsync(binding, session, Guid.CreateVersion7(), default)); Assert.IsTrue(controls.LastStream!.Closed);
            controls.Content = bytes;

            // 取消后才交付的开流结果也必须释放，且不能保存半份解析或留下未观察任务。
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            controls.BeforeOpen = async () => { entered.SetResult(); await release.Task; };
            using (var cancel = new CancellationTokenSource())
            {
                var pending = prepare.PrepareAsync(binding, session, Guid.CreateVersion7(), cancel.Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancel.Cancel(); release.SetResult();
                await Assert.ThrowsAsync<OperationCanceledException>(async () => _ = await pending);
            }
            Assert.IsTrue(controls.LastStream!.Closed); Assert.AreEqual(0, controls.LastStream.Reads); controls.BeforeOpen = null;
            Assert.AreEqual(count, await SourceCount());

            var beforeMismatch = controls.Opens;
            writer.SetTenant(new(Guid.CreateVersion7(), "other", "其他租户"));
            Assert.IsNull(await reader.ReadCurrentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session, default));
            Assert.IsNull(await prepare.PrepareAsync(binding, session, Guid.CreateVersion7(), default)); SetScope();
            Assert.AreEqual(beforeMismatch, controls.Opens);
            Assert.IsNull(await prepare.PrepareAsync(binding with { DocumentVersion = 1 }, session, Guid.CreateVersion7(), default));
            Assert.IsNull(await reader.ReadSourceAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, Guid.CreateVersion7(), session, default));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@Id", new { Id = binding.DocumentId });
            Assert.IsNull(await reader.ReadCurrentAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.VersionId, session, default));
            Assert.IsNull(await prepare.PrepareAsync(binding, session, Guid.CreateVersion7(), default));
            Assert.AreEqual(count, await SourceCount());
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE VersionId=@Id", new { Id = binding.VersionId }));
            void SetScope() { if (session.TenantId is { } id) writer.SetTenant(new(id, "correction", "校对")); else writer.SetHost(); }
            Task<int> SourceCount() => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_correction_source WHERE VersionId=@Id", new { Id = binding.VersionId });
        }
        Assert.AreEqual(operations, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"), "本地来源准备不能创建模型预算或收费操作。");
    }

    private sealed class Controls(IDataTransactionState transactions, ICurrentTenant tenant, SessionBindingSnapshot session,
        Guid fileId, Guid versionId, byte[] bytes, string hash) : IBackgroundSessionAuthorization, IHostFileReferenceReader,
        ITenantResourceFileDescriptorReader, IHostFileContentReader, ITenantResourceFileStore
    {
        internal SessionBindingSnapshot Session = session;
        internal string? DeniedPermission;
        internal bool MetadataReady = true;
        internal byte[] Content = bytes;
        private readonly int OriginalSize = bytes.Length;
        internal Func<Task>? BeforeOpen;
        internal int Opens;
        internal TrackedStream? LastStream;
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default) =>
            AuthorizeAllAsync(binding, [permissionCode], cancellationToken);
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(SessionBindingSnapshot binding, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default)
        {
            Check(cancellationToken);
            Assert.IsTrue(permissionCodes.Contains(AiKnowledgeCorrectionPermissions.Read)); Assert.IsTrue(permissionCodes.Contains(AiKnowledgeDocumentPermissions.SourceRead));
            return Task.FromResult<AuthorizedSessionActor?>(binding == Session && !permissionCodes.Contains(DeniedPermission!)
                ? new(binding.UserId, binding.TenantId, binding.SessionId) : null);
        }
        public Task<HostFileReference?> GetReadyReferenceAsync(Guid id, CancellationToken cancellationToken = default)
        { Check(cancellationToken); Assert.IsTrue(tenant.IsHost); Assert.AreEqual(fileId, id); return Task.FromResult<HostFileReference?>(MetadataReady ? new(fileId, OriginalSize, hash) : null); }
        public Task<TenantResourceFileDescriptor?> GetReadyDescriptorAsync(string module, Guid resource, Guid id, CancellationToken cancellationToken = default)
        { Check(cancellationToken); Assert.IsFalse(tenant.IsHost); Binding(module, resource, id); return Task.FromResult<TenantResourceFileDescriptor?>(MetadataReady ? new(fileId, "source.txt", "text/plain", OriginalSize, hash, Session.UserId) : null); }
        public async Task<Result<HostFileContent>> OpenReadyContentAsync(Guid id, CancellationToken cancellationToken = default)
        { Assert.AreEqual(fileId, id); await Opening(cancellationToken); return Result<HostFileContent>.Success(new(LastStream!, "text/plain", "source.txt")); }
        public async Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string module, Guid resource, Guid id, CancellationToken cancellationToken = default)
        { Binding(module, resource, id); await Opening(cancellationToken); return Result<TenantResourceFileContent>.Success(new(LastStream!, "text/plain", "source.txt")); }
        private async Task Opening(CancellationToken token)
        { Check(token); Opens++; if (BeforeOpen is not null) await BeforeOpen(); LastStream = new(Content); }
        private void Check(CancellationToken token) { token.ThrowIfCancellationRequested(); Assert.IsFalse(transactions.HasTransaction); Assert.AreEqual(Session.TenantId, tenant.Id); }
        private void Binding(string module, Guid resource, Guid id) { Assert.AreEqual("ai", module); Assert.AreEqual(versionId, resource); Assert.AreEqual(fileId, id); }
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName, string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes, false)
    {
        internal int Reads; internal bool Closed;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { Reads++; return base.ReadAsync(buffer, cancellationToken); }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { Closed = true; return base.DisposeAsync(); }
    }
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
