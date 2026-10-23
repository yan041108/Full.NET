using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>实际隔离输出在双库逐格式贯通收费回执、完整代次、引用及来源失效；不调用付费供应商。</summary>
internal static class AiKnowledgeDocumentFormatPersistenceAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, AiOperationBudgetAssertions.TestClock clock)
    {
        var manifestPath = Environment.GetEnvironmentVariable("FULLNET_DOCUMENT_ACCEPTANCE_MANIFEST");
        if (string.IsNullOrEmpty(manifestPath)) Assert.Inconclusive("Explicit document sandbox acceptance manifest is required.");
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath));
        var runtime = manifest.RootElement.GetProperty("imageId").GetString()!;
        var fixtureRoot = manifest.RootElement.GetProperty("fixtureRoot").GetString()!;
        Assert.AreEqual(0, manifest.RootElement.GetProperty("failures").GetArrayLength());
        var cases = manifest.RootElement.GetProperty("accepted").EnumerateArray().ToArray(); Assert.IsTrue(cases.Length is >= 40 and <= 64);
        await factory.InitializeAsync(); await using var db = AiOperationBudgetAssertions.Connection(factory);
        foreach (var scoped in new[] { false, true })
        foreach (var item in cases)
        {
            var filename = item.GetProperty("source").GetString()!; var output = item.GetProperty("output").GetString()!;
            Assert.AreEqual(Path.GetFileName(filename), filename); Assert.AreEqual(Path.GetFileName(output), output);
            var original = await File.ReadAllBytesAsync(Path.Combine(fixtureRoot, filename)); Assert.IsTrue(original.Length is > 0 and <= 8388608);
            var wire = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(manifestPath)!, output));
            var format = (AiKnowledgeParsedFormat)item.GetProperty("format").GetInt32();
            var mime = Full.NET.Modules.Ai.Domain.AiKnowledgeFilePolicy.ContentTypeFor(item.GetProperty("kind").GetString()!)!;
            var policyKey = item.GetProperty("policyKey").GetString()!;
            var parsed = AiKnowledgeParsedWireCodec.Decode(wire, format, Convert.ToHexString(SHA256.HashData(original)), original.Length, default,
                AiKnowledgeDocumentFormats.Policy(mime, policyKey)); Assert.IsNotNull(parsed, filename);
            var p = await Migration246AiKnowledgeIndexTaskRecoveryTests.SeedAsync(db, scoped);
            clock.UtcNow = new DateTimeOffset((DateTime)p["Now"]!, TimeSpan.Zero);
            p["Mime"] = mime; p["Hash"] = parsed.ContentHash; p["Size"] = parsed.SizeBytes; p["Policy"] = policyKey;
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ContentType=@Mime,ContentHash=@Hash,SizeBytes=@Size,ParsingPolicyKey=@Policy WHERE Id=@VersionId", p);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_import SET ContentType=@Mime,ContentHash=@Hash,SizeBytes=@Size,ParsingPolicyKey=@Policy WHERE VersionId=@VersionId", p);
            var binding = new AiKnowledgeIndexTaskBinding((Guid)p["KnowledgeBaseId"]!, (Guid)p["DocumentId"]!, (Guid)p["VersionId"]!, 1, 1, (Guid)p["OwnerUserId"]!);
            var session = new SessionBindingSnapshot(binding.OwnerUserId, (Guid?)p["TenantId"], Guid.CreateVersion7(), "stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            await using var scope = factory.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
            var writer = services.GetRequiredService<ICurrentTenantContextWriter>();
            if (session.TenantId is { } tenantId) writer.SetTenant(new(tenantId, "format", "格式验收")); else writer.SetHost();
            var query = services.GetRequiredService<IQueryExecutor>(); var command = services.GetRequiredService<ICommandExecutor>();
            var transaction = services.GetRequiredService<ICommandTransaction>(); var state = services.GetRequiredService<IDataTransactionState>();
            var tenant = services.GetRequiredService<ICurrentTenant>(); var ids = services.GetRequiredService<IIdGenerator>(); var options = services.GetRequiredService<IOptions<DatabaseOptions>>();
            var authorization = new Authorization(session); var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids);
            var sessions = new AiKnowledgeIndexSessionService(query, command, transaction, state, tenant, clock, ids, tasks, authorization);
            var manifests = new AiKnowledgeIndexDocumentManifestStore(query, command, transaction, state, tenant, clock, tasks, sessions, authorization, options);
            var vectors = new AiKnowledgeIndexDocumentVectorStore(query, command, transaction, state, tenant, clock, tasks, sessions, authorization, options);
            var activations = new AiKnowledgeIndexDocumentActivationStore(query, command, transaction, state, tenant, clock, ids, tasks, authorization, options);
            var model = new AiModelConfigRecord { Id = (Guid)p["ModelConfigId"]!, Version = 1, IsEnabled = true, TenantId = session.TenantId, ProviderKey = "openai_compatible", ModelId = "text-embedding-3-small", EndpointBaseUrl = "https://api.openai.com/v1" };
            var input = AiKnowledgeDocumentModelChunker.Chunk(new() { Id = binding.KnowledgeBaseId, Version = 1, IsEnabled = true, TenantId = session.TenantId, DataClassification = "internal", EmbeddingModelConfigId = model.Id, EmbeddingModelVersion = model.Version }, model, parsed, new(), default);
            Assert.IsTrue(input.IsSuccess, filename); var plan = AiKnowledgeDocumentPlan.Create(input.Output! with { ParserRuntimeId = runtime }, default); Assert.IsNotNull(plan);
            var task = await sessions.EnqueueAsync(binding, session, default); Assert.IsNotNull(task);
            var held = await tasks.AcquireAsync(binding, task.Value, default, 180); Assert.IsNotNull(held); var lease = held.LeaseToken!.Value;
            Assert.IsTrue(await manifests.PersistAsync(binding, task.Value, lease, plan, default));
            var batches = new AiKnowledgeIndexBatchStore(query, command, tasks, ids, options); var budgets = services.GetRequiredService<IAiOperationBudgetStore>();
            var restored = new List<AiKnowledgeDocumentEmbeddedChunk>();
            foreach (var batch in plan.Batches)
            {
                var request = AiEmbeddingBudgetRequest.Create(Guid.CreateVersion7(), model, batch.Chunks.Select(c => c.Text).ToArray());
                var prepared = await batches.PrepareAsync(binding, task.Value, lease, batch.Ordinal, AiOperationBudgetStore.Fingerprint(request), batch.Chunks.Count, default); Assert.IsNotNull(prepared);
                request = request with { OperationId = prepared.Id }; await budgets.ReserveAsync(request);
                var values = batch.Chunks.Select(_ => { var v = new float[1536]; v[0] = 1; return new ReadOnlyMemory<float>(v); }).ToArray();
                Assert.IsFalse(await vectors.PersistAsync(binding, task.Value, lease, prepared.Id, plan, batch.Ordinal, values, default));
                await budgets.SettleAsync(prepared.Id, new(1, 0), "succeeded");
                Assert.IsTrue(await vectors.PersistAsync(binding, task.Value, lease, prepared.Id, plan, batch.Ordinal, values, default));
                var stored = await vectors.ReadSnapshotAsync(binding, task.Value, lease, batch.Ordinal, default); Assert.IsNotNull(stored); restored.AddRange(stored.Snapshot.Chunks);
            }
            CollectionAssert.AreEqual(plan.Source.Chunks.ToArray(), restored.Select(c => c.Source).ToArray());
            Assert.IsTrue(restored.All(c => AiKnowledgeSourceLocations.Valid(AiKnowledgeSourceLocations.Create(c.Block.Sequence, c.Source.Position, plan.Source.Document))));
            var proof = await vectors.ReadWholePlanProofAsync(binding, task.Value, lease, default); Assert.IsNotNull(proof);
            Assert.IsTrue(await activations.ActivateAsync(binding, task.Value, lease, proof, default), filename);
            var read = new Dictionary<string, object?>(AiSqlParameters.Create(("OwnerUserId", binding.OwnerUserId), ("ActiveRoleIdsJson", "[]"), ("ActiveOrganizationUnitIdsJson", "[]"), ("KnowledgeBaseId", binding.KnowledgeBaseId), ("DocumentId", binding.DocumentId), ("VersionId", binding.VersionId)));
            var status = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.For(options.Value.Provider, scoped), read); Assert.IsNotNull(status);
            Assert.IsTrue(await activations.IsReadyForReadAsync(status, read, default));
            var charged = await db.QuerySingleAsync<long>("SELECT SUM(ChargedTokens) FROM fn_ai_operation_budget WHERE ModelConfigId=@Id", new { Id = model.Id });
            var consumer = new AiKnowledgeIndexDocumentPersistenceService(null!, vectors, manifests, sessions, activations);
            Assert.IsTrue(await consumer.ExecuteGenerationAsync(binding, task.Value, lease, default));
            Assert.AreEqual(charged, await db.QuerySingleAsync<long>("SELECT SUM(ChargedTokens) FROM fn_ai_operation_budget WHERE ModelConfigId=@Id", new { Id = model.Id }));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ParsingPolicyKey='unavailable_policy' WHERE Id=@VersionId", p);
            Assert.IsFalse(await activations.IsReadyForReadAsync(status, read, default));
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_version SET ParsingPolicyKey=@Policy WHERE Id=@VersionId", p);
            await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@DocumentId", p);
            Assert.IsFalse(await activations.IsReadyForReadAsync(status, read, default));
        }
    }
    private sealed class Authorization(SessionBindingSnapshot expected) : IBackgroundSessionAuthorization
    {
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permission, CancellationToken cancellationToken = default)
            => Task.FromResult<AuthorizedSessionActor?>(binding == expected ? new(binding.UserId, binding.TenantId, binding.SessionId) : null);
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(SessionBindingSnapshot binding, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
            => AuthorizeAsync(binding, "", cancellationToken);
    }
}
