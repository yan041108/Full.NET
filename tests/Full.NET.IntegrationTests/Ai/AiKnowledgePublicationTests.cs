using System.Data.Common;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageKnowledgePublications;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库、完整计量代次、发布/租约/清理事务；身份和 Files/模型仅为明确受控边界。</summary>
[TestClass]
public sealed class AiKnowledgePublicationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public async Task Publication_preserves_old_content_and_serializes_review_heads_readers_and_cleanup(DatabaseProvider provider)
    {
        var clock = new AiOperationBudgetAssertions.TestClock();
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => { services.RemoveAll<IClock>(); services.AddSingleton<IClock>(clock); });
        await AiKnowledgeIndexEmbeddingAssertions.VerifyAsync(factory, clock, wholeGeneration: true, executeGeneration: true,
            publication: async (services, db, binding, files, original) => await Verify(services, db, binding, files, original));
    }
    private static async Task Verify(IServiceProvider sp, DbConnection db, AiKnowledgeIndexTaskBinding binding,
        AiKnowledgeIndexEmbeddingAssertions.ControlledFiles files, SessionBindingSnapshot owner)
    {
        var f = new Fixture(sp, files, owner);
        var reviewer = owner with { UserId = Guid.CreateVersion7(), SessionId = Guid.CreateVersion7() };
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_member (Id,KnowledgeBaseId,UserId,CreatedAtUtc) VALUES (@Id,@Base,@Actor,@Now)", new { Id = Guid.CreateVersion7(), Base = binding.KnowledgeBaseId, Actor = reviewer.UserId, Now = DateTimeOffset.UtcNow });
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@Id,@Doc,@Actor,@Now)", new { Id = Guid.CreateVersion7(), Doc = binding.DocumentId, Actor = reviewer.UserId, Now = DateTimeOffset.UtcNow });
        var sourceTask = await db.QuerySingleAsync<Guid>("SELECT IndexTaskId FROM fn_ai_knowledge_active_generation WHERE DocumentId=@Id", new { Id = binding.DocumentId });
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_generation WHERE Id=@Id AND CleanupState=0", new { Id = sourceTask }));
        var model = (await f.Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(owner.TenantId.HasValue ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            AiSqlParameters.Create(("ModelConfigId", await db.ExecuteScalarAsync<Guid>("SELECT EmbeddingModelConfigId FROM fn_ai_knowledge_base WHERE Id=@Id", new { Id = binding.KnowledgeBaseId }))), default))!;
        var vector = new float[1536]; vector[0] = 1;
        Task<AiKnowledgeRetrievalSnapshot> Retrieve() => f.Retrieval.RetrieveAsync([binding.KnowledgeBaseId], owner.UserId, model, vector, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => Retrieve());
        var headResult = await f.Publications.ReadHeadAsync(binding.KnowledgeBaseId, binding.DocumentId, owner, default);
        Assert.IsTrue(headResult.IsSuccess, $"{headResult.Error?.Code}: tenant={f.Tenant.Id}, host={f.Tenant.IsHost}, available={f.Tenant.IsAvailable}, owner={owner.TenantId}");
        var head = headResult.Value!;
        Assert.AreEqual(0, head.Version); Assert.IsNull(head.PublicationId);
        var request = new RequestAiKnowledgePublication(Guid.CreateVersion7(), binding.VersionId, 0, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddDays(2));
        var requested = await f.Publications.RequestAsync(binding.KnowledgeBaseId, binding.DocumentId, request, owner, default);
        Assert.IsTrue(requested.IsSuccess, requested.Error?.Code);
        var proposal = requested.Value!;
        Assert.AreEqual(proposal.Id, (await f.Publications.RequestAsync(binding.KnowledgeBaseId, binding.DocumentId, request, owner, default)).Value!.Id);
        Assert.IsFalse((await f.Publications.RequestAsync(binding.KnowledgeBaseId, binding.DocumentId, request with { EffectiveToUtc = request.EffectiveToUtc.AddHours(1) }, owner, default)).IsSuccess);
        Assert.IsFalse((await f.Publications.ReviewAsync(binding.KnowledgeBaseId, binding.DocumentId, proposal.Id, new(true, proposal.Version, proposal.SnapshotHash), owner, default)).IsSuccess);
        f.Identity.Current = reviewer;
        var approved = await f.Publications.ReviewAsync(binding.KnowledgeBaseId, binding.DocumentId, proposal.Id, new(true, proposal.Version, proposal.SnapshotHash), reviewer, default);
        Assert.IsTrue(approved.IsSuccess, approved.Error?.Code); proposal = approved.Value!;
        f.Identity.Current = owner;
        Assert.IsFalse((await f.Publications.PublishAsync(binding.KnowledgeBaseId, binding.DocumentId, proposal.Id, new(proposal.Version, proposal.SnapshotHash), owner with { SessionId = Guid.CreateVersion7() }, default)).IsSuccess);
        var published = await f.Publications.PublishAsync(binding.KnowledgeBaseId, binding.DocumentId, proposal.Id, new(proposal.Version, proposal.SnapshotHash), owner, default);
        Assert.IsTrue(published.IsSuccess, published.Error?.Code);
        var snapshot = await Retrieve(); Assert.AreEqual(proposal.Id, snapshot.Sources.Single().PublicationId);
        var lineage = new AiKnowledgeAnswerLineage(3, Guid.CreateVersion7(), Guid.CreateVersion7(), owner.UserId, owner.TenantId,
            snapshot.Sources.Select(row => new AiKnowledgeAnswerDependency(row.KnowledgeBaseId, row.DocumentId, row.VersionId, row.DocumentVersion, row.KnowledgeBaseVersion, AiKnowledgeFileSource.RequireFileId(row), row.ContentHash,
                row.TaskId!.Value, row.ActiveReceiptHash!, row.ActiveManifestHash!, row.ActivePlanHash!, row.PublicationId)).ToArray(), []);
        var history = new AiKnowledgeAnswerHistoryGuard(f.Query, f.State, f.Tenant, f.Options, f.Access, f.Permission, f.Proof);
        var json = AiKnowledgeAnswerLineageCodec.Encode(lineage);
        Assert.IsNotNull(await history.AuthorizeAsync(json, lineage.MessageId, lineage.SessionId, owner.UserId, false, default));

        // 原始文件与文档索引版本不受组织信息编辑影响，逐项失败只重试原失败身份。
        var folder = (await f.Directory.SaveFolderAsync(binding.KnowledgeBaseId, null, new("资料", null, 0), owner, default)).Value!;
        var item = new SetAiKnowledgeDirectoryItem(binding.DocumentId, Guid.CreateVersion7(), 0, folder.Id, ["Ａ", "a", " 中文 "], owner.UserId);
        var missing = item with { DocumentId = Guid.CreateVersion7(), RequestKey = Guid.CreateVersion7() };
        var batch = (await f.Directory.BatchAsync(binding.KnowledgeBaseId, new([item, missing]), owner, default)).Value!;
        Assert.IsTrue(batch[0].Succeeded); Assert.IsFalse(batch[1].Succeeded);
        var replay = (await f.Directory.BatchAsync(binding.KnowledgeBaseId, new([item]), owner, default)).Value!;
        Assert.AreEqual(1, replay[0].Version);
        Assert.IsFalse((await f.Directory.SaveFolderAsync(binding.KnowledgeBaseId, folder.Id, new("循环", folder.Id, folder.Version), owner, default)).IsSuccess);
        Assert.IsFalse((await f.Directory.DeleteFolderAsync(binding.KnowledgeBaseId, folder.Id, folder.Version, owner, default)).IsSuccess);
        var page = await f.Directory.ListAsync(binding.KnowledgeBaseId, folder.Id, "a", owner.UserId, "published", 1, 20, owner, default);
        Assert.IsTrue(page.IsSuccess, page.Error?.Code); CollectionAssert.AreEqual(new[] { "a", "中文" }, page.Value!.Items.Single().Tags.ToArray());
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT Version FROM fn_ai_knowledge_document WHERE Id=@Id", new { Id = binding.DocumentId }));
        _ = await Retrieve();
        await db.ExecuteAsync("UPDATE fn_ai_model_config SET IsEnabled=0 WHERE Id=@Id", new { model.Id });
        await Assert.ThrowsAsync<InvalidDataException>(() => Retrieve());
        await db.ExecuteAsync("UPDATE fn_ai_model_config SET IsEnabled=1 WHERE Id=@Id", new { model.Id });
        f.Identity.Current = reviewer;
        Assert.IsTrue((await f.Directory.FoldersAsync(binding.KnowledgeBaseId, reviewer, default)).IsSuccess);
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_member WHERE KnowledgeBaseId=@Id AND UserId=@User", new { Id = binding.KnowledgeBaseId, User = reviewer.UserId });
        Assert.IsFalse((await f.Directory.FoldersAsync(binding.KnowledgeBaseId, reviewer, default)).IsSuccess);
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_member (Id,KnowledgeBaseId,UserId,CreatedAtUtc) VALUES (@Id,@Base,@Actor,@Now)", new { Id = Guid.CreateVersion7(), Base = binding.KnowledgeBaseId, Actor = reviewer.UserId, Now = DateTimeOffset.UtcNow });
        f.Identity.Current = owner;

        // 两个已审核提案在各自真实数据库作用域中竞争同一发布头，只允许一个成功。
        var a = (await f.Publications.RequestAsync(binding.KnowledgeBaseId, binding.DocumentId, request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 }, owner, default)).Value!;
        var b = (await f.Publications.RequestAsync(binding.KnowledgeBaseId, binding.DocumentId, request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 }, owner, default)).Value!;
        f.Identity.Current = reviewer;
        a = (await f.Publications.ReviewAsync(binding.KnowledgeBaseId, binding.DocumentId, a.Id, new(true, a.Version, a.SnapshotHash), reviewer, default)).Value!;
        b = (await f.Publications.ReviewAsync(binding.KnowledgeBaseId, binding.DocumentId, b.Id, new(true, b.Version, b.SnapshotHash), reviewer, default)).Value!;
        f.Identity.Current = owner;
        async Task<Result<AiKnowledgePublicationResponse>> Race(AiKnowledgePublicationResponse item)
        {
            await using var scope = sp.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope(); var scoped = scope.ServiceProvider;
            var writer = scoped.GetRequiredService<ICurrentTenantContextWriter>();
            if (owner.TenantId is { } tenant) writer.SetTenant(new(tenant, "publication", "发布")); else writer.SetHost();
            try { return await new Fixture(scoped, files, owner).Publications.PublishAsync(binding.KnowledgeBaseId, binding.DocumentId, item.Id, new(item.Version, item.SnapshotHash), owner, default); }
            finally { writer.Clear(); }
        }
        var races = await Task.WhenAll(Race(a), Race(b)); Assert.AreEqual(1, races.Count(result => result.IsSuccess));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_read_lease WHERE GenerationId=@Id", new { Id = sourceTask }),
            "并发证明完成后应释放读取租约，不能因反向锁序遗留到期前的回收阻塞。");
        Assert.IsNull(await history.AuthorizeAsync(json, lineage.MessageId, lineage.SessionId, owner.UserId, false, default), "即使代次相同，历史依赖也不能借新发布身份复活。");
        snapshot = await Retrieve();

        var changedProposal = (await f.Publications.RequestAsync(binding.KnowledgeBaseId, binding.DocumentId,
            request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 2 }, owner, default)).Value!;
        f.Identity.Current = reviewer;
        changedProposal = (await f.Publications.ReviewAsync(binding.KnowledgeBaseId, binding.DocumentId, changedProposal.Id,
            new(true, changedProposal.Version, changedProposal.SnapshotHash), reviewer, default)).Value!;
        f.Identity.Current = owner;

        // 新稿处理中、失败或补偿待清理均不能使旧发布内容变成最新草稿。
        var draft = Guid.CreateVersion7();
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc) SELECT @Draft,DocumentId,@File,2,'draft.txt',ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc FROM fn_ai_knowledge_version WHERE Id=@Old", new { Draft = draft, File = Guid.CreateVersion7(), Old = binding.VersionId });
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=Version+1 WHERE Id=@Id", new { Id = binding.DocumentId });
        foreach (var state in new[] { "awaiting_upload", "failed", "cleanup_pending" })
        {
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_import WHERE VersionId=@Id", new { Id = draft });
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_import (Id,DocumentId,VersionId,RequestKey,ExpectedDocumentVersion,OriginalFileName,ContentType,ContentHash,SizeBytes,ActorUserId,StateKey,CreatedAtUtc,UpdatedAtUtc) SELECT @Draft,DocumentId,@Draft,@Draft,2,'draft.txt',ContentType,ContentHash,SizeBytes,UploadedByUserId,@State,CreatedAtUtc,CreatedAtUtc FROM fn_ai_knowledge_version WHERE Id=@Draft", new { Draft = draft, State = state });
            Assert.AreEqual(binding.VersionId, (await Retrieve()).Sources.Single().VersionId, state);
        }
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET StateKey='completed',FileId=(SELECT FileId FROM fn_ai_knowledge_version WHERE Id=@Draft) WHERE VersionId=@Draft", new { Draft = draft });
        var draftBinding = binding with { VersionId = draft, DocumentVersion = 2 };
        var draftTasks = new AiKnowledgeIndexTaskStore(f.Query, f.Command, f.Tenant, f.Clock, f.Ids);
        var cancelled = await draftTasks.EnqueueAsync(draftBinding, default); Assert.IsNotNull(cancelled);
        Assert.IsTrue(await draftTasks.CancelAsync(draftBinding, cancelled.Id, default));
        Assert.AreEqual(binding.VersionId, (await Retrieve()).Sources.Single().VersionId);
        Assert.IsFalse((await f.Publications.PublishAsync(binding.KnowledgeBaseId, binding.DocumentId, changedProposal.Id,
            new(changedProposal.Version, changedProposal.SnapshotHash), owner, default)).IsSuccess);
        var currentHead = (await f.Publications.ReadHeadAsync(binding.KnowledgeBaseId, binding.DocumentId, owner, default)).Value!;
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_publication SET EffectiveToUtc=@Expired WHERE Id=@Id", new { Id = currentHead.PublicationId, Expired = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await Assert.ThrowsAsync<InvalidDataException>(() => Retrieve());
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_publication SET EffectiveToUtc=@Expiry WHERE Id=@Id", new { Id = currentHead.PublicationId, Expiry = request.EffectiveToUtc });
        snapshot = await Retrieve();

        // 原读取租约保护在途资产；墓碑立即阻断新读，清理标记提交后可从物理删除故障恢复。
        await using (var held = await AiKnowledgeGenerationLease.AcquireAsync(snapshot.Sources.Single(), owner.UserId, f.Query, f.Command, f.Transaction, f.Tenant, f.Clock, f.Ids, f.Options, default))
        {
            Assert.IsNotNull(held);
            var release = new Dictionary<string, object?> { ["KnowledgeBaseId"] = binding.KnowledgeBaseId, ["DocumentId"] = binding.DocumentId, ["OwnerUserId"] = owner.UserId, ["Now"] = f.Clock.UtcNow };
            Assert.AreEqual(Guid.Empty, await f.Query.QuerySingleOrDefaultAsync<Guid>(owner.TenantId.HasValue ? AiKnowledgePublicationSql.SourceReleaseTenant : AiKnowledgePublicationSql.SourceReleaseHost, release, default));
            Assert.IsTrue((await f.Publications.WithdrawAsync(binding.KnowledgeBaseId, binding.DocumentId, new(currentHead.Version, currentHead.PublicationId!.Value), owner, default)).IsSuccess);
            await Assert.ThrowsAsync<InvalidDataException>(() => Retrieve());
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@Id", new { Id = binding.DocumentId });
            await f.Cleanup().CleanupTaskAsync(binding.KnowledgeBaseId, binding.DocumentId, owner.UserId, sourceTask, default);
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT CleanupState FROM fn_ai_knowledge_generation WHERE Id=@Id", new { Id = sourceTask }));
        }
        await Assert.ThrowsAsync<IOException>(() => f.Cleanup(new DeleteFault(f.Command)).CleanupTaskAsync(binding.KnowledgeBaseId, binding.DocumentId, owner.UserId, sourceTask, default));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT CleanupState FROM fn_ai_knowledge_generation WHERE Id=@Id", new { Id = sourceTask }));
        await f.Cleanup().CleanupTaskAsync(binding.KnowledgeBaseId, binding.DocumentId, owner.UserId, sourceTask, default);
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT CleanupState FROM fn_ai_knowledge_generation WHERE Id=@Id", new { Id = sourceTask }));
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_text_manifest WHERE Id=@Id", new { Id = sourceTask }));
        Assert.IsTrue(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_publication_event WHERE DocumentId=@Id", new { Id = binding.DocumentId }) >= 8);
        if (!owner.TenantId.HasValue) await RecoverMigration(db, f.Options.Value, sourceTask);
    }
    // 在已有业务行与已清理代次上模拟未记账和索引部分完成，重跑不得复活已清理载荷。
    private static async Task RecoverMigration(DbConnection db, DatabaseOptions options, Guid task)
    {
        const string index = "IX_fn_ai_knowledge_directory_FolderId";
        var sqlServer = options.Provider == DatabaseProvider.SqlServer;
        await db.ExecuteAsync(sqlServer ? $"DROP INDEX {index} ON dbo.fn_ai_knowledge_directory" : $"DROP INDEX {index} ON fn_ai_knowledge_directory");
        var publications = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_publication");
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "327_AiKnowledgePublication.sql", sqlServer);
        await ReviewFixMigrationRecoverySupport.CreateRunner(options.Provider, options.ConnectionString).MigrateAsync();
        Assert.AreEqual(1, await ReviewFixMigrationRecoverySupport.IndexExistsAsync(db, "fn_ai_knowledge_directory", index, sqlServer));
        Assert.AreEqual(publications, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_publication"));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT CleanupState FROM fn_ai_knowledge_generation WHERE Id=@Id", new { Id = task }));
        await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(db, "327_AiKnowledgePublication.sql", sqlServer);
        await ReviewFixMigrationRecoverySupport.CreateRunner(options.Provider, options.ConnectionString).MigrateAsync();
        Assert.AreEqual(publications, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_publication"));
    }
    private sealed class DeleteFault(ICommandExecutor inner) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters, CancellationToken cancellationToken)
            => statement.Name.Contains(".vectors_", StringComparison.Ordinal) ? throw new IOException("Controlled cleanup process loss after marking.") : inner.ExecuteAsync(statement, parameters, cancellationToken);
    }
    internal sealed class Fixture
    {
        internal readonly IQueryExecutor Query; internal readonly ICommandExecutor Command; internal readonly ICommandTransaction Transaction;
        internal readonly IDataTransactionState State; internal readonly ICurrentTenant Tenant; internal readonly IClock Clock;
        internal readonly IIdGenerator Ids; internal readonly IOptions<DatabaseOptions> Options; internal readonly Identity Identity;
        internal readonly AiKnowledgeReadAccess Access; internal readonly AiKnowledgePermissionGuard Permission;
        internal readonly AiKnowledgeAnswerSourceProof Proof; internal readonly AiKnowledgePublicationService Publications;
        internal readonly AiKnowledgeDirectoryService Directory; internal readonly AiKnowledgeRetrievalService Retrieval;
        private readonly AiKnowledgeIndexActivationStore text; private readonly AiKnowledgeIndexPdfActivationStore pdf;
        internal Fixture(IServiceProvider sp, AiKnowledgeIndexEmbeddingAssertions.ControlledFiles files, SessionBindingSnapshot owner,
            AiKnowledgeCorrectionActivationStore? correction = null)
        {
            Query = sp.GetRequiredService<IQueryExecutor>(); Command = sp.GetRequiredService<ICommandExecutor>(); Transaction = sp.GetRequiredService<ICommandTransaction>();
            State = sp.GetRequiredService<IDataTransactionState>(); Tenant = sp.GetRequiredService<ICurrentTenant>(); Clock = sp.GetRequiredService<IClock>(); Ids = sp.GetRequiredService<IIdGenerator>(); Options = sp.GetRequiredService<IOptions<DatabaseOptions>>();
            Identity = new(owner, State); var relations = new Relations(); Access = new(relations, relations, Tenant);
            Permission = new(Identity, new HttpContextAccessor(), new NoApiKey());
            var tasks = new AiKnowledgeIndexTaskStore(Query, Command, Tenant, Clock, Ids);
            text = new(Query, Command, Transaction, State, Tenant, Clock, Ids, (tasks).Scope, Identity, Options);
            pdf = new(Query, Command, Transaction, State, Tenant, Clock, Ids, (tasks).Scope, Identity, Options); Proof = new(text, pdf, correction: correction);
            var imports = new AiKnowledgeImportStore(Query, Command, Transaction, Tenant, Clock, Ids, Options, sp.GetRequiredService<IOutboxWriter>());
            var source = new AiKnowledgeVersionReader(imports, Query, Access, Permission, files, files, files);
            Publications = new(Query, Command, Transaction, State, Tenant, Clock, Ids, Options, Identity, Access, Proof, source);
            Directory = new(Query, Command, Transaction, State, Tenant, Clock, Ids, Options, Identity, Access);
            Retrieval = new(Query, State, Tenant, Access, Permission, text, pdf, Options,
                generationLeases: new(Query, Command, Transaction, Tenant, Clock, Ids, Options), correctionActivation: correction);
        }
        internal AiKnowledgeIndexCleanupStore Cleanup(ICommandExecutor? command = null) => new(Query, command ?? Command, Transaction, State, Tenant, Clock, Options);
    }
    internal sealed class Identity(SessionBindingSnapshot current, IDataTransactionState state) : ICurrentSessionAuthorization, IBackgroundSessionAuthorization
    {
        internal SessionBindingSnapshot Current = current;
        public Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default) => AuthorizeAllAsync([permissionCode], cancellationToken);
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default)
        { Assert.IsFalse(state.HasTransaction); return Task.FromResult<AuthorizedSessionActor?>(new(Current.UserId, Current.TenantId, Current.SessionId)); }
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default) => AuthorizeAllAsync(binding, [permissionCode], cancellationToken);
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(SessionBindingSnapshot binding, IReadOnlyCollection<string> permissionCodes, CancellationToken cancellationToken = default)
        { Assert.IsFalse(state.HasTransaction); return Task.FromResult<AuthorizedSessionActor?>(binding == Current ? new(binding.UserId, binding.TenantId, binding.SessionId) : null); }
    }
    private sealed class Relations : IActiveRoleDirectory, ICurrentTenantOrganizationUnitDirectory
    {
        public Task<IReadOnlyList<Guid>> ListActiveRoleIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IReadOnlyList<Guid>> ListActiveUnitIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<PagedResult<ActiveRoleDirectoryEntry>> ListActiveRolesAsync(int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, ActiveRoleDirectoryEntry>> FindActiveRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<TenantOrganizationUnitDirectoryEntry>> ListActiveUnitsAsync(int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, TenantOrganizationUnitDirectoryEntry>> FindActiveUnitsAsync(IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class NoApiKey : ICurrentApiKeyAuthorization
    { public Task<Guid?> AuthorizeAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(null); }
}
