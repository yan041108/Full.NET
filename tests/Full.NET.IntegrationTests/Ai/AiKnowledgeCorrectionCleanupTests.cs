using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库删除后清理及 Worker 恢复；读取租约为明确数据库夹具，不冒充模型或实际 Native 读取。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionCleanupTests
{
    [TestMethod]
    public Task SqlServer_tombstone_cleanup_is_bounded_replayable_and_waits_for_original_version_readers() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_tombstone_cleanup_is_bounded_replayable_and_waits_for_original_version_readers() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var faults = new CleanupFaults();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services =>
        {
            new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
            services.Configure<AiKnowledgeCleanupWorkerOptions>(o => { o.Enabled = true; o.BatchSize = 1; });
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(AiKnowledgeCleanupHostedProcessor)).ToArray()) services.Remove(hosted);
            services.RemoveAll<AiKnowledgeCorrectionCleanupStore>();
            services.AddScoped(sp => new AiKnowledgeCorrectionCleanupStore(sp.GetRequiredService<IQueryExecutor>(), new FailingCommand(sp.GetRequiredService<ICommandExecutor>(), sp.GetRequiredService<IDataTransactionState>(), faults),
                sp.GetRequiredService<ICommandTransaction>(), sp.GetRequiredService<IDataTransactionState>(), sp.GetRequiredService<ICurrentTenant>(), sp.GetRequiredService<IClock>(), sp.GetRequiredService<IOptions<DatabaseOptions>>()));
        });
        await factory.InitializeAsync();
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        foreach (var scoped in new[] { false, true })
        {
            var seed = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            var binding = new AiKnowledgeCorrectionStore.Binding((Guid)seed["KnowledgeBaseId"]!, (Guid)seed["DocumentId"]!, (Guid)seed["VersionId"]!, (Guid)seed["OwnerUserId"]!, 1);
            var bytes = Encoding.UTF8.GetBytes("必须清理的原解析正文，保留费用与审计身份。"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentHash=@Hash,SizeBytes=@Size WHERE Id=@Id; UPDATE fn_ai_knowledge_import SET ContentHash=@Hash,SizeBytes=@Size WHERE VersionId=@Id", new { Hash = hash, Size = bytes.Length, Id = binding.VersionId });
            await using var scope = factory.Services.CreateAsyncScope(); var sp = scope.ServiceProvider;
            var context = sp.GetRequiredService<ICurrentTenantContextWriter>();
            if (seed["TenantId"] is Guid tenantId) context.SetTenant(new(tenantId, "cleanup", "校对清理")); else context.SetHost();
            var store = sp.GetRequiredService<AiKnowledgeCorrectionStore>(); var cleanup = sp.GetRequiredService<AiKnowledgeCorrectionCleanupStore>();
            var parsed = AiKnowledgeParsedDocument.FromText(new(Encoding.UTF8.GetString(bytes), hash, bytes.Length), AiKnowledgeTextFormat.PlainText, default)!;
            var created = await store.CreateSourceAsync(binding, Guid.CreateVersion7(), Hash("source"), parsed, "managed:utf8-text-v1", default);
            Assert.IsTrue(created.IsSuccess); var source = created.Value!;
            var draft = (await store.ReadCurrentAsync(binding, parsed.Format, hash, bytes.Length, parsed.Policy, default))!.Document;
            for (var index = 0; index < 27; index++)
            {
                var saved = await store.SaveAsync(binding, source.Id, index, Guid.CreateVersion7(), Hash($"edit-{index}"), $"校对理由 {index}", draft, source.ParserRuntimeId, default);
                Assert.IsTrue(saved.IsSuccess, saved.Error?.Code); binding = binding with { DocumentVersion = saved.Value!.DocumentVersion };
            }
            var reparse = await store.CreateSourceAsync(binding, Guid.CreateVersion7(), Hash("reparse"), parsed, "managed:utf8-text-v2", default);
            Assert.IsTrue(reparse.IsSuccess); var other = reparse.Value!;
            // 排序由数据库明确比较；来源没有索引任务，清理不能依赖索引或文件补偿候选偶然带出它。
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_source SET CreatedAtUtc=@Created WHERE Id=@Id", new { Created = DateTimeOffset.UtcNow.AddDays(-2), source.Id });
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_correction_source SET CreatedAtUtc=@Created WHERE Id=@Id", new { Created = DateTimeOffset.UtcNow.AddDays(-1), other.Id });
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_task WHERE DocumentId=@Id", new { Id = binding.DocumentId }));
            var audit = await Audit(); var file = JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_knowledge_version WHERE Id=@Id", new { Id = binding.VersionId }));
            Assert.AreEqual(0, await Run());
            await cleanup.CleanupSourceAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.OwnerUserId, source.Id, default);
            Assert.AreEqual(0, await SourceState(source.Id)); Assert.AreEqual(0, await ErasedRevisions());
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@Id; UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@Base", new { Id = binding.DocumentId, Base = binding.KnowledgeBaseId });
            await cleanup.CleanupSourceAsync(binding.KnowledgeBaseId, binding.DocumentId, Guid.CreateVersion7(), source.Id, default);
            await cleanup.CleanupSourceAsync(Guid.CreateVersion7(), binding.DocumentId, binding.OwnerUserId, source.Id, default);
            await using (var foreign = factory.Services.CreateAsyncScope())
            {
                foreign.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(new(Guid.CreateVersion7(), "foreign", "其他租户"));
                await foreign.ServiceProvider.GetRequiredService<AiKnowledgeCorrectionCleanupStore>().CleanupSourceAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.OwnerUserId, source.Id, default);
            }
            using (var cancelled = new CancellationTokenSource())
            { cancelled.Cancel(); await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cleanup.CleanupSourceAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.OwnerUserId, source.Id, cancelled.Token)); }
            Assert.AreEqual(0, await SourceState(source.Id)); CollectionAssert.AreEqual(source.OriginalPayload, await db.QuerySingleAsync<byte[]>("SELECT OriginalPayload FROM fn_ai_knowledge_correction_source WHERE Id=@Id", new { source.Id }));

            var generation = Guid.CreateVersion7(); var lease = Guid.CreateVersion7();
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_generation (Id,DocumentId,VersionId,PlanHash,ManifestHash,ReceiptHash,ReceiptJson,LeaseToken,ActivatedAtUtc,CleanupState) VALUES (@Id,@DocumentId,@VersionId,'','','','',@Id,@Now,1)",
                new { Id = generation, binding.DocumentId, binding.VersionId, Now = DateTimeOffset.UtcNow });
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_read_lease (Id,GenerationId,TenantId,ActorUserId,ExpiresAtUtc,CreatedAtUtc) VALUES (@Id,@Generation,@Tenant,@Actor,@Until,@Now)",
                new { Id = lease, Generation = generation, Tenant = (Guid?)seed["TenantId"], Actor = binding.OwnerUserId, Until = DateTimeOffset.UtcNow.AddMinutes(5), Now = DateTimeOffset.UtcNow });
            Assert.AreEqual(1, await Run()); Assert.AreEqual(1, await Run()); Assert.AreEqual(0, await Run());
            Assert.AreEqual(0, await SourceState(source.Id)); Assert.AreEqual(0, await SourceState(other.Id)); Assert.AreEqual(0, await ErasedRevisions());
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_read_lease SET ExpiresAtUtc=@Until WHERE Id=@Id", new { Until = DateTimeOffset.UtcNow.AddMinutes(-1), Id = lease });
            faults.EraseCount = 0; faults.FailAt = 2;
            Assert.AreEqual(1, await Run());
            Assert.AreEqual(1, await SourceState(source.Id)); Assert.AreEqual(1, await ErasedRevisions(), "第二份实际清空后的异常必须回滚该份标记及载荷，第一份已提交仍保留。");
            Assert.AreEqual(26, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_correction_revision WHERE SourceId=@Id AND CleanupState=0", new { Id = source.Id }));
            faults.FailAt = 0;
            Assert.AreEqual(1, await Run()); Assert.AreEqual(2, await SourceState(other.Id), "失败的大来源不能饿死后面的无修订来源。");
            Assert.AreEqual(0, await Run()); Assert.AreEqual(1, await Run());
            Assert.AreEqual(26, await ErasedRevisions()); Assert.AreEqual(1, await SourceState(source.Id), "每轮最多二十五份，剩余修订仍须保留原解析供恢复清理。");
            Assert.AreEqual(0, await Run());
            using var worker = await IntegrationWorkerHostFactory.BuildAsync(provider, connection, new Dictionary<string, string?>
            {
                [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:Enabled"] = "true", [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:BatchSize"] = "1",
                [$"{AiKnowledgeCleanupWorkerOptions.SectionName}:PollMilliseconds"] = "250",
            }, "AiCorrectionCleanupRestart", nameof(AiKnowledgeCleanupHostedProcessor));
            await worker.StartAsync();
            try
            {
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (await SourceState(source.Id) != 2) await Task.Delay(50, wait.Token);
            }
            finally { await worker.StopAsync(); }
            Assert.AreEqual(27, await ErasedRevisions());
            var length = provider == DatabaseProvider.MySql ? "OCTET_LENGTH" : "DATALENGTH";
            Assert.AreEqual(0, await db.QuerySingleAsync<int>($"SELECT SUM({length}(OriginalPayload)+{length}(InitialPayload)) FROM fn_ai_knowledge_correction_source WHERE VersionId=@Id", new { Id = binding.VersionId }));
            Assert.AreEqual(0, await db.QuerySingleAsync<int>($"SELECT SUM({length}(Payload)) FROM fn_ai_knowledge_correction_revision WHERE VersionId=@Id", new { Id = binding.VersionId }));
            Assert.AreEqual(audit, await Audit()); Assert.AreEqual(file, JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_knowledge_version WHERE Id=@Id", new { Id = binding.VersionId })));
            Assert.AreEqual(0, await Run());
            await cleanup.CleanupSourceAsync(binding.KnowledgeBaseId, binding.DocumentId, binding.OwnerUserId, source.Id, default);
            Assert.AreEqual(audit, await Audit()); Assert.IsNull(await store.ReadSourceAsync(binding, source.Id, default)); Assert.IsNull(await store.ReadCurrentAsync(binding, parsed.Format, hash, bytes.Length, parsed.Policy, default));
            Assert.IsFalse((await store.SaveAsync(binding, source.Id, 27, Guid.CreateVersion7(), Hash("after-delete"), "删除后拒绝写回", draft, source.ParserRuntimeId, default)).IsSuccess);
            Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));

            async Task<int> Run()
            {
                await using var request = factory.Services.CreateAsyncScope(); var parent = request.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                var unrelated = Guid.CreateVersion7(); parent.SetTenant(new(unrelated, "parent", "父范围"));
                var count = await request.ServiceProvider.GetRequiredService<AiKnowledgeCleanupWorkerCoordinator>().ProcessPendingAsync(default);
                Assert.AreEqual(unrelated, request.ServiceProvider.GetRequiredService<ICurrentTenant>().Id); return count;
            }
            Task<int> SourceState(Guid id) => db.QuerySingleAsync<int>("SELECT CleanupState FROM fn_ai_knowledge_correction_source WHERE Id=@Id", new { Id = id });
            Task<int> ErasedRevisions() => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_correction_revision WHERE SourceId=@Id AND CleanupState=2", new { Id = source.Id });
            async Task<string> Audit() => JsonSerializer.Serialize(await db.QueryAsync("SELECT Id,VersionId,SourceId,ParentId,RequestKey,RequestHash,PayloadHash,ReasonText,ActorUserId,CreatedAtUtc,DocumentVersion,HeadVersion FROM fn_ai_knowledge_correction_revision WHERE VersionId=@Id ORDER BY Id", new { Id = binding.VersionId }))
                + JsonSerializer.Serialize(await db.QueryAsync("SELECT Id,VersionId,RequestKey,RequestHash,FileHash,ParserRuntimeId,SourceHash,InitialPayloadHash,ActorUserId,CreatedAtUtc FROM fn_ai_knowledge_correction_source WHERE VersionId=@Id ORDER BY Id", new { Id = binding.VersionId }))
                + JsonSerializer.Serialize(await db.QueryAsync("SELECT * FROM fn_ai_knowledge_correction_head WHERE Id=@Id", new { Id = binding.VersionId }));
        }
    }
    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private sealed class CleanupFaults { internal int FailAt; internal int EraseCount; }
    private sealed class FailingCommand(ICommandExecutor inner, IDataTransactionState state, CleanupFaults faults) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(state.HasTransaction); var affected = await inner.ExecuteAsync(statement, parameters, cancellationToken);
            if (faults.FailAt > 0 && statement.Name.StartsWith("ai.knowledge_correction_cleanup.erase_revision_", StringComparison.Ordinal) && ++faults.EraseCount == faults.FailAt)
                throw new IOException("明确注入的清理事务失败。");
            return affected;
        }
    }
}
