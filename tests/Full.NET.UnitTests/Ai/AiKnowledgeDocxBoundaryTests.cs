using System.Text;
using System.Text.Json.Nodes;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>进程输出及直接内部记录都不可信，任何失败不留下部分计划。</summary>
[TestClass]
public sealed class AiKnowledgeDocxBoundaryTests
{
    [TestMethod]
    [DataRow("protocolVersion", "2")]
    [DataRow("parserVersion", "\"future-v2\"")]
    [DataRow("contentHash", "\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"")]
    [DataRow("sizeBytes", "98")]
    [DataRow("sizeBytes", "1.5")]
    [DataRow("text", "\"\"" )]
    [DataRow("text", "\"hello\\u0000\\n\"")]
    [DataRow("text", "\"hello\"")]
    [DataRow("blocks", "[]")]
    [DataRow("extra", "true")]
    public async Task Invalid_root_fields_never_deliver_a_document(string field, string json)
    {
        var root = JsonNode.Parse(AiKnowledgeDocxPlanningTests.Wire(AiKnowledgeDocxPlanningTests.Document("hello")))!.AsObject();
        root[field] = JsonNode.Parse(json); Rejected(await AiKnowledgeDocxPlanningTests.Read(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [TestMethod]
    [DataRow("sequence", "1")]
    [DataRow("kind", "\"page\"")]
    [DataRow("offset", "1")]
    [DataRow("length", "6")]
    [DataRow("paragraphNumber", "2")]
    [DataRow("tableNumber", "1")]
    [DataRow("rowNumber", "1")]
    [DataRow("cellNumber", "1")]
    [DataRow("cellParagraphCount", "1")]
    [DataRow("isHeader", "true")]
    [DataRow("extra", "true")]
    public async Task Forged_block_coordinates_or_fields_never_deliver_partial_output(string field, string json)
    {
        var root = JsonNode.Parse(AiKnowledgeDocxPlanningTests.Wire(AiKnowledgeDocxPlanningTests.Document("hello")))!;
        root["blocks"]![0]![field] = JsonNode.Parse(json); Rejected(await AiKnowledgeDocxPlanningTests.Read(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [TestMethod]
    [DataRow("{\"protocolVersion\":1,", "{\"protocolVersion\":1,\"protocolVersion\":1,")]
    [DataRow("\"sequence\":0,", "\"sequence\":0,\"sequence\":0,")]
    [DataRow("\"length\":5,", "")]
    public async Task Duplicate_and_missing_fields_are_rejected(string from, string to)
        => Rejected(await AiKnowledgeDocxPlanningTests.Read(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(AiKnowledgeDocxPlanningTests.Wire(AiKnowledgeDocxPlanningTests.Document("hello"))).Replace(from, to))));

    [TestMethod]
    public async Task Wire_budget_uses_actual_reads_and_does_not_take_stream_ownership()
    {
        using var stream = new RepeatingStream(AiKnowledgeDocxOutputReader.MaxWireBytes + 5);
        var result = await AiKnowledgeDocxOutputReader.ReadAsync(stream, new string('a', 64), 97, default);
        Assert.AreEqual(AiKnowledgeDocxReadFailure.OutputTooLarge, result.Failure);
        Assert.AreEqual(AiKnowledgeDocxOutputReader.MaxWireBytes + 1, stream.ReadBytes); Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public async Task Invalid_source_does_not_read_and_cancellation_or_io_failure_propagates()
    {
        using var stream = new BrokenStream();
        Rejected(await AiKnowledgeDocxOutputReader.ReadAsync(stream, "bad", 97, default));
        Rejected(await AiKnowledgeDocxOutputReader.ReadAsync(stream, new string('a', 64), 0, default));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => AiKnowledgeDocxOutputReader.ReadAsync(stream, new string('a', 64), 97, cancel.Token));
        await Assert.ThrowsExactlyAsync<IOException>(() => AiKnowledgeDocxOutputReader.ReadAsync(stream, new string('a', 64), 97, default));
    }

    [TestMethod]
    public void Direct_docx_records_validate_unicode_structure_and_full_coverage()
    {
        var doc = AiKnowledgeDocxPlanningTests.Document("😀");
        Assert.AreEqual(AiKnowledgeDocxReadFailure.None, AiKnowledgeDocxOutputReader.ValidateDocument(doc, default));
        foreach (var invalid in new[] { doc with { Text = "\ud800\n" }, doc with { Blocks = [doc.Blocks[0] with { Length = 1 }] }, doc with { Text = doc.Text + "extra" }, doc with { Blocks = [doc.Blocks[0] with { ParagraphNumber = 8193 }] } })
            Assert.AreNotEqual(AiKnowledgeDocxReadFailure.None, AiKnowledgeDocxOutputReader.ValidateDocument(invalid, default));
    }

    [TestMethod]
    public void Actual_approved_model_chunks_have_no_cross_block_overlap_and_bind_exact_scope()
    {
        var doc = AiKnowledgeDocxPlanningTests.Document("hello world", "second");
        Assert.IsTrue(Chunk(doc).IsSuccess);
        var tenant = Guid.CreateVersion7(); var model = AiKnowledgeDocxPlanningTests.Model(tenant);
        Assert.AreEqual(AiKnowledgeModelChunkFailure.ModelScopeMismatch, AiKnowledgeDocxModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(), model, doc, new(), default).Failure);
        Assert.IsTrue(AiKnowledgeDocxModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(tenant), model, doc, new(), default).IsSuccess);
        var knowledge = AiKnowledgeDocxPlanningTests.Knowledge(tenant, 4);
        Assert.AreEqual(AiKnowledgeModelChunkFailure.ModelNotApproved, AiKnowledgeDocxModelChunker.Chunk(knowledge, model, doc, new(), default).Failure);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void Budgets_are_global_even_across_many_small_blocks(int scenario)
    {
        var options = scenario switch { 0 => new AiKnowledgeChunkOptions(MaxChunks: 1), 1 => new AiKnowledgeChunkOptions(MaxTotalTokens: 1), _ => new AiKnowledgeChunkOptions(MaxChunks: 0) };
        var result = Chunk(AiKnowledgeDocxPlanningTests.Document("hello", "world"), options);
        Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Output);
        Assert.AreEqual(scenario switch { 0 => AiKnowledgeChunkFailure.TooManyChunks, 1 => AiKnowledgeChunkFailure.TotalTokenBudgetExceeded, _ => AiKnowledgeChunkFailure.InvalidOptions }, result.ChunkFailure);
    }

    [TestMethod]
    public void Plan_recounts_tokens_freezes_collections_and_binds_document_coordinates()
    {
        var doc = AiKnowledgeDocxPlanningTests.Document("hello world");
        var approved = Chunk(doc).Output!;
        var chunks = approved.Chunks.ToArray();
        var source = approved with { Chunks = chunks, ParserRuntimeId = AiKnowledgeIndexDocumentCodecTests.RuntimeId };
        var plan = AiKnowledgeDocumentPlan.Create(source, default); Assert.IsNotNull(plan);
        Assert.IsNull(AiKnowledgeDocumentPlan.Create(source with { Chunks = [chunks[0] with { TokenCount = chunks[0].TokenCount + 1 }] }, default));
        Assert.IsNull(AiKnowledgeDocumentPlan.Create(source with { Chunks = [chunks[0] with { Position = chunks[0].Position with { Offset = 1 } }] }, default));
        Assert.IsNull(AiKnowledgeDocumentPlan.Create(source with { Chunks = [chunks[0] with { Text = "other world" }] }, default));
        var changed = AiKnowledgeDocumentPlan.Create(source with { ModelConfigVersion = 4 }, default); Assert.IsNotNull(changed); Assert.AreNotEqual(plan.Summary.PlanHash, changed.Summary.PlanHash);
        chunks[0] = chunks[0] with { Text = "mutated" }; Assert.AreEqual("hello world", plan.Batches[0].Chunks[0].Text);
    }

    private static AiKnowledgeDocxModelChunkResult Chunk(AiKnowledgeParsedDocx doc, AiKnowledgeChunkOptions? options = null)
        => AiKnowledgeDocxModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(), AiKnowledgeDocxPlanningTests.Model(), doc, options ?? new(), default);
    [TestMethod]
    public void Source_collection_count_is_captured_once_before_bounded_copy()
    {
        var doc = AiKnowledgeDocxPlanningTests.Document("hello");
        Assert.IsTrue(Chunk(doc with { Blocks = new ChangingCount(doc.Blocks[0]) }).IsSuccess);
    }
    [TestMethod]
    public void Plan_rejects_surrogate_halves_before_token_count_or_hash_encoding()
    {
        var doc = AiKnowledgeDocxPlanningTests.Document("😀");
        var approved = Chunk(doc).Output!; var original = approved.Chunks[0];
        var chunks = new[] { original with { Text = "\ud83d", TokenCount = 3, Position = original.Position with { Length = 1 } },
            original with { Sequence = 1, Text = "\ude00", TokenCount = 3, Position = original.Position with { Offset = 1, Length = 1 } } };
        var source = approved with { Chunks = chunks, ParserRuntimeId = AiKnowledgeIndexDocumentCodecTests.RuntimeId };
        Assert.IsNull(AiKnowledgeDocumentPlan.Create(source, default));
    }
    [TestMethod]
    public void Claimed_cell_paragraphs_require_at_least_their_structural_newlines()
    {
        var doc = new AiKnowledgeParsedDocx(new string('a', 64), 97, "A\n", [new(0, "tableCell", 0, 1, 1, 1, 1, 1, 2, false)]);
        Assert.AreNotEqual(AiKnowledgeDocxReadFailure.None, AiKnowledgeDocxOutputReader.ValidateDocument(doc, default));
    }
    private sealed class ChangingCount(AiKnowledgeDocxBlock block) : IReadOnlyList<AiKnowledgeDocxBlock>
    {
        private int calls;
        public int Count => ++calls == 1 ? 1 : 8193;
        public AiKnowledgeDocxBlock this[int index] => index == 0 ? block : throw new AssertFailedException("只允许首次有界快照中的一个块。");
        public IEnumerator<AiKnowledgeDocxBlock> GetEnumerator() => throw new AssertFailedException("不能枚举可变集合。");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static void Rejected(AiKnowledgeDocxReadResult result) { Assert.IsFalse(result.IsSuccess); Assert.IsNull(result.Document); }
    private sealed class BrokenStream : MemoryStream
    { public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => ValueTask.FromException<int>(new IOException("private")); }
    private sealed class RepeatingStream(int remaining) : MemoryStream
    {
        internal int ReadBytes;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); var count = Math.Min(remaining, buffer.Length); buffer.Span[..count].Fill((byte)' '); remaining -= count; ReadBytes += count; return ValueTask.FromResult(count); }
    }
}
