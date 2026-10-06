using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiKnowledgeIndexPdfPlanningServiceTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Fresh_pdf_generation_freezes_complete_pages_pays_each_batch_and_activates_once(bool tenant)
    {
        using var f = new Fixture(tenant, pageCount: 17); var g = new FreshGeneration(f);
        Assert.IsTrue(await g.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.AreEqual(2, f.Generator.Calls); Assert.HasCount(2, g.Rows); Assert.HasCount(2, g.Settled);
        Assert.AreEqual(17, AiKnowledgeIndexPdfManifestCodec.Decode(f.ManifestRow!.Payload, default).Identity.PageCount);
        Assert.IsTrue(g.Completed); Assert.IsNotNull(g.Pointer); Assert.IsTrue(f.Stream.Closed);
        Assert.IsTrue(await g.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.AreEqual(2, f.Generator.Calls); Assert.AreEqual(2, g.ActivationWrites);
        await f.Budget.Received(2).ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>());
        await f.Budget.Received(2).SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), "succeeded", CancellationToken.None);
    }

    [TestMethod] [DataRow("permission")] [DataRow("cancellation")] [DataRow("invalid")]
    public async Task Fresh_pdf_generation_failure_in_later_batch_preserves_paid_prefix_without_activation(string fault)
    {
        using var f = new Fixture(false, pageCount: 17); using var cancellation = new CancellationTokenSource(); var g = new FreshGeneration(f);
        f.Generator.BeforeReturn = _ =>
        {
            if (f.Generator.Calls != 2) return;
            if (fault == "permission") f.Lose("permission");
            else if (fault == "cancellation") cancellation.Cancel();
            else f.Generator.Invalid = "nonfinite";
        };
        if (fault == "cancellation") await Assert.ThrowsAsync<OperationCanceledException>(() => g.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, cancellation.Token));
        else if (fault == "invalid") await Assert.ThrowsAsync<InvalidDataException>(() => g.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        else Assert.IsFalse(await g.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.AreEqual(2, f.Generator.Calls); Assert.HasCount(1, g.Rows); Assert.IsFalse(g.Completed); Assert.IsNull(g.Pointer);
        Assert.AreEqual(0, g.ActivationWrites);
        await f.Budget.Received(2).SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), CancellationToken.None);
    }

    /// <summary>组合真实规划、计费和代次消费者；数据库原子性由双库测试覆盖。</summary>
    private sealed class FreshGeneration
    {
        internal readonly Dictionary<int, AiKnowledgeIndexVectorRecord> Rows = new();
        internal readonly Dictionary<int, AiKnowledgeIndexBatchRecord> Batches = new();
        internal readonly HashSet<Guid> Settled = new();
        internal AiKnowledgeIndexActivationRecord? Pointer;
        internal bool Completed;
        internal int ActivationWrites;
        internal readonly AiKnowledgeIndexPdfPersistenceService Consumer;

        internal FreshGeneration(Fixture f)
        {
            var execution = f.Paid(); f.Ids.NewId().Returns(_ => Guid.CreateVersion7());
            f.AfterOpen = () => f.Stream = new(f.Bytes);
            f.Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var sql = call.ArgAt<SqlStatement>(0); var p = call.ArgAt<Dictionary<string, object?>>(1);
                if (sql.Name.Contains("knowledge_activation.complete_", StringComparison.Ordinal)) { ActivationWrites++; Completed = true; }
                else if (sql.Name.Contains("knowledge_activation.insert_", StringComparison.Ordinal))
                {
                    ActivationWrites++; Pointer = new() { Id = (Guid)p["Id"]!, DocumentId = f.Binding.DocumentId, IndexTaskId = f.TaskId,
                        VersionId = f.Binding.VersionId, PlanHash = (string)p["PlanHash"]!, ManifestHash = (string)p["ManifestHash"]!,
                        ReceiptHash = (string)p["ReceiptHash"]!, ReceiptJson = (string)p["ReceiptJson"]!, LeaseToken = f.Lease };
                }
                else if (p.ContainsKey("Payload") && (string)p["FormatKey"]! == AiKnowledgeIndexPdfVectorCodec.FormatKey)
                {
                    var ordinal = (int)p["BatchOrdinal"]!;
                    Rows[ordinal] = new() { Id = (Guid)p["OperationId"]!, IndexTaskId = f.TaskId, BatchOrdinal = ordinal,
                        ModelConfigId = f.ModelId, ModelConfigVersion = 1, ProviderKey = (string)p["ProviderKey"]!, ModelId = (string)p["ModelId"]!,
                        Dimensions = (int)p["Dimensions"]!, InputCount = (int)p["InputCount"]!, RequestHash = (string)p["RequestHash"]!,
                        FormatKey = (string)p["FormatKey"]!, DistanceKey = (string)p["DistanceKey"]!, PayloadHash = (string)p["PayloadHash"]!, Payload = (byte[])p["Payload"]! };
                }
                else if (p.ContainsKey("Payload")) f.ManifestRow = new() { Id = f.TaskId, FormatKey = (string)p["FormatKey"]!, PayloadHash = (string)p["PayloadHash"]!, Payload = (byte[])p["Payload"]! };
                else if (p.ContainsKey("BatchOrdinal"))
                {
                    var ordinal = (int)p["BatchOrdinal"]!;
                    if (!Batches.ContainsKey(ordinal)) Batches[ordinal] = new() { Id = (Guid)p["OperationId"]!, IndexTaskId = f.TaskId,
                        BatchOrdinal = ordinal, InputCount = (int)p["InputCount"]!, RequestHash = (string)p["RequestHash"]! };
                }
                return 1;
            });
            f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexActivationRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Pointer);
            f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexBatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(c => Batches.GetValueOrDefault((int)c.ArgAt<Dictionary<string, object?>>(1)["BatchOrdinal"]!));
            f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(c => Rows.GetValueOrDefault((int)c.ArgAt<Dictionary<string, object?>>(1)["BatchOrdinal"]!));
            f.Budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(c =>
                new AiOperationReservation(c.ArgAt<AiOperationRequest>(0).OperationId, true, "reserved", 100, null, null));
            f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c =>
            { if (c.ArgAt<string>(2) == "succeeded") Settled.Add(c.ArgAt<Guid>(0)); return Task.CompletedTask; });
            var query = new GenerationQuery(f.Query, this); var options = Options.Create(new DatabaseOptions());
            var manifests = new AiKnowledgeIndexPdfManifestStore(query, f.Command, f.Transaction, f.State, f.Tenant, f.Clock, f.Tasks, f.Sessions, f.Authorization, options);
            var vectors = new AiKnowledgeIndexPdfVectorStore(query, f.Command, f.Transaction, f.State, f.Tenant, f.Clock, f.Tasks, f.Sessions, f.Authorization, options);
            var activations = new AiKnowledgeIndexPdfActivationStore(query, f.Command, f.Transaction, f.State, f.Tenant, f.Clock, f.Ids, f.Tasks, f.Authorization, options);
            Consumer = new(execution, vectors, manifests, f.Sessions, activations);
        }
        private sealed class GenerationQuery(IQueryExecutor inner, FreshGeneration f) : IQueryExecutor
        {
            public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
                => inner.QuerySingleOrDefaultAsync<T>(sql, parameters, cancellationToken);
            public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? parameters = null, CancellationToken cancellationToken = default)
            {
                if (typeof(T) == typeof(AiKnowledgeIndexBatchRecord)) return Task.FromResult((IReadOnlyList<T>)(object)f.Batches.OrderBy(x => x.Key).Select(x => x.Value).ToArray());
                var receipts = f.Rows.OrderBy(x => x.Key).Select(x => x.Value).Where(r => f.Settled.Contains(r.Id)).Select(r => new AiKnowledgeIndexVectorReceiptRecord
                { Id = r.Id, BatchOrdinal = r.BatchOrdinal, RequestHash = r.RequestHash, InputCount = r.InputCount, PayloadHash = r.PayloadHash,
                    ProviderKey = r.ProviderKey, ModelId = r.ModelId, Dimensions = r.Dimensions, FormatKey = r.FormatKey, DistanceKey = r.DistanceKey }).ToArray();
                return Task.FromResult((IReadOnlyList<T>)(object)receipts);
            }
        }
    }
}
