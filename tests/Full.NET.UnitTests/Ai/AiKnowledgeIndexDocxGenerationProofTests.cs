using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiKnowledgeIndexDocxVectorStoreTests
{
    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public async Task Complete_docx_generation_proves_every_logical_block_and_settled_operation(bool tenant, bool large)
    {
        var f = Whole(tenant, large); var result = await Proof(f);
        Assert.IsNotNull(result); Assert.AreEqual(f.Manifest!.PayloadHash, result.Manifest.PayloadHash);
        Assert.AreEqual(f.Plan.Summary.PlanHash, result.Manifest.Manifest.Summary.PlanHash);
        Assert.HasCount(2, result.Receipts); Assert.AreEqual(0, result.Receipts[0].BatchOrdinal);
        Assert.AreEqual(1, result.Receipts[1].BatchOrdinal); Assert.AreEqual(large ? 3072 : 1536, result.Receipts[1].Dimensions);
        Assert.AreEqual(1, result.Receipts[1].InputCount); Assert.AreEqual(4, f.VectorReads);
        Assert.AreEqual(2, f.ReceiptReads); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod]
    [DataRow("missing")] [DataRow("extra")] [DataRow("duplicate")] [DataRow("order")]
    [DataRow("operation")] [DataRow("hash")] [DataRow("request")] [DataRow("dimensions")] [DataRow("format")]
    public async Task Incomplete_or_inconsistent_docx_receipt_set_is_rejected(string fault)
    {
        var f = Whole(false); f.ChangeReceipts = rows => fault switch
        {
            "missing" => rows.Take(1).ToArray(), "extra" => rows.Append(rows[1] with { BatchOrdinal = 2 }).ToArray(),
            "duplicate" => [rows[0], rows[0]], "order" => rows.Reverse().ToArray(),
            "operation" => [rows[0] with { Id = Guid.CreateVersion7() }, rows[1]],
            "hash" => [rows[0] with { PayloadHash = new string('0', 64) }, rows[1]],
            "request" => [rows[0] with { RequestHash = new string('0', 64) }, rows[1]],
            "dimensions" => [rows[0] with { Dimensions = 3072 }, rows[1]],
            _ => [rows[0] with { FormatKey = "knowledge-vector-batch-v1" }, rows[1]]
        };
        Assert.IsNull(await Proof(f));
    }

    [TestMethod] [DataRow("deleted")] [DataRow("bytes")] [DataRow("replacement")] [DataRow("ledger")]
    public async Task Earlier_docx_batch_changed_while_last_batch_waits_is_rejected(string fault)
    {
        var f = Whole(true); f.AfterRead = () =>
        {
            if (f.VectorReads != 3) return;
            var row = f.Rows![0];
            if (fault == "deleted") f.Rows.Remove(0);
            else if (fault == "bytes") f.Rows[0] = row with { Payload = new byte[8], PayloadHash = Hash(new byte[8]) };
            else if (fault == "replacement") f.Rows[0] = row with { Id = Guid.CreateVersion7() };
            else f.LedgerDenied = true;
        };
        Assert.IsNull(await Proof(f));
    }

    [TestMethod]
    [DataRow("permission", 1)] [DataRow("session", 1)] [DataRow("source", 1)] [DataRow("model", 1)] [DataRow("lease", 1)]
    [DataRow("permission", 2)] [DataRow("session", 2)] [DataRow("source", 2)] [DataRow("model", 2)] [DataRow("lease", 2)]
    public async Task Authority_loss_during_whole_docx_receipts_wait_denies_proof(string fault, int read)
    {
        var f = Whole(false); f.AfterReceipts = () => { if (f.ReceiptReads == read) f.Lose(fault); };
        Assert.IsNull(await Proof(f));
    }

    [TestMethod] [DataRow(1)] [DataRow(2)]
    public async Task Manifest_replaced_during_whole_docx_receipts_wait_denies_proof(int read)
    {
        var f = Whole(true); f.AfterReceipts = () =>
        {
            if (f.ReceiptReads != read) return;
            var alternate = AiKnowledgeIndexDocxCodecTests.Plan(altered: true).Source;
            f.Plan = AiKnowledgeIndexDocxPlan.Create(f.Input with { Document = alternate.Document, Chunks = alternate.Chunks }, default)!;
            Assert.IsNotNull(f.Plan); f.Manifest = f.NewManifest();
        };
        Assert.IsNull(await Proof(f));
    }

    [TestMethod] [DataRow("endpoint", 1)] [DataRow("endpoint", 2)] [DataRow("organization", 1)] [DataRow("organization", 2)]
    public async Task Same_version_model_binding_changed_during_docx_receipts_wait_denies_proof(string fault, int read)
    {
        var f = Whole(false); f.AfterReceipts = () =>
        {
            if (f.ReceiptReads != read) return;
            var m = f.Model;
            f.Model = new AiModelConfigRecord { Id = m.Id, Version = m.Version, IsEnabled = m.IsEnabled, TenantId = m.TenantId,
                ProviderKey = m.ProviderKey, ModelId = m.ModelId,
                EndpointBaseUrl = fault == "endpoint" ? "https://other.example/v1" : m.EndpointBaseUrl,
                OrganizationId = fault == "organization" ? "other" : m.OrganizationId };
        };
        Assert.IsNull(await Proof(f));
    }

    [TestMethod] [DataRow(1)] [DataRow(2)]
    public async Task Cancellation_during_docx_receipts_wait_propagates(int read)
    {
        var f = Whole(true); using var cancellation = new CancellationTokenSource();
        f.AfterReceipts = () => { if (f.ReceiptReads == read) cancellation.Cancel(); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => Proof(f, cancellation.Token));
        Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("missing")] [DataRow("corrupt")] [DataRow("hash")]
    public async Task Whole_docx_proof_requires_actual_complete_manifest(string fault)
    {
        var f = Whole(false); var m = f.Manifest!;
        f.Manifest = fault switch { "missing" => null, "hash" => m with { PayloadHash = new string('0', 64) },
            _ => m with { Payload = new byte[8], PayloadHash = Hash(new byte[8]) } };
        Assert.IsNull(await Proof(f)); Assert.AreEqual(0, f.ReceiptReads);
    }

    [TestMethod]
    public async Task Whole_docx_proof_propagates_cancellation_and_query_failure_and_rejects_transaction()
    {
        var f = Whole(false); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Proof(f, cancellation.Token));
        f.AfterReceipts = () => throw new IOException(); await Assert.ThrowsAsync<IOException>(() => Proof(f));
        f.State.HasTransaction.Returns(true); await Assert.ThrowsAsync<InvalidOperationException>(() => Proof(f));
    }

    private static Fixture Whole(bool tenant, bool large = false, int blockCount = 17)
    {
        var f = new Fixture(tenant, large, blockCount: blockCount); f.Rows = new();
        for (var ordinal = 0; ordinal < f.Plan.Batches.Count; ordinal++)
        {
            f.Ordinal = ordinal; f.Operation = Guid.CreateVersion7();
            f.Vectors = f.Plan.Batches[ordinal].Chunks.Select(_ => { var v = new float[large ? 3072 : 1536]; v[0] = 1; return new ReadOnlyMemory<float>(v); }).ToArray();
            f.Payload = AiKnowledgeIndexDocxVectorCodec.Encode(f.Plan, ordinal, f.Vectors, default); f.Rows[ordinal] = f.NewRow();
        }
        return f;
    }
    private static Task<AiKnowledgeIndexDocxGenerationProof?> Proof(Fixture f, CancellationToken token = default) => f.Store.ReadWholePlanProofAsync(f.Binding, f.TaskId, f.Lease, token);
    private static AiKnowledgeIndexVectorReceiptRecord ToReceipt(AiKnowledgeIndexVectorRecord r) => new()
    { Id = r.Id, BatchOrdinal = r.BatchOrdinal, RequestHash = r.RequestHash, InputCount = r.InputCount, PayloadHash = r.PayloadHash,
        ProviderKey = r.ProviderKey, ModelId = r.ModelId, Dimensions = r.Dimensions, FormatKey = r.FormatKey, DistanceKey = r.DistanceKey };
}
