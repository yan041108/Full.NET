using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeIndexPdfPlanTests
{
    [TestMethod]
    public void Fixed_v1_digests_match_an_independent_little_endian_reference()
    {
        var plan = AiKnowledgeIndexPdfPlan.Create(Source(), default)!;
        Assert.AreEqual("D12578E28142946585760C4E8405206163738D9345FE8C0572857AE88259E08E", plan.Batches[0].SourceHash);
        Assert.AreEqual("A3641C7BD77F1C6682DF786F462BA1FEA08B0C4359A358D5D02177CC50AA0A64", plan.Summary.PlanHash);
    }

    [TestMethod] [DataRow(2)] [DataRow(17)] [DataRow(2048)]
    public void Complete_pdf_pages_produce_a_frozen_bounded_plan(int count)
    {
        var source = Source(count);
        var plan = AiKnowledgeIndexPdfPlan.Create(source, default);
        Assert.IsNotNull(plan);
        Assert.AreEqual(count, plan.Summary.ChunkCount);
        Assert.AreEqual(count * 2L, plan.Summary.TokenCount);
        Assert.AreEqual((count + 15) / 16, plan.Identity.PageCount);
        Assert.AreEqual((count + 15) / 16, plan.Summary.BatchCount);
        CollectionAssert.AreEqual(Enumerable.Range(0, count).ToArray(), plan.Batches.SelectMany(b => b.Chunks).Select(c => c.Sequence).ToArray());
        foreach (var batch in plan.Batches)
        {
            Assert.IsTrue(batch.Chunks.Count <= 16 && batch.Chunks.Sum(c => c.EmbeddingText.Length) <= 32000);
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AiKnowledgePdfChunk>)batch.Chunks)[0] = batch.Chunks[0]);
        }
        var first = plan.Batches[0].Chunks[0];
        ((AiKnowledgePdfChunk[])source.Chunks)[0] = first with { Text = "changed" };
        Assert.AreEqual(first, plan.Batches[0].Chunks[0]);
    }

    [TestMethod]
    public void Batch_character_budget_and_cross_page_offsets_are_preserved()
    {
        var source = Source(17, 8000);
        var plan = AiKnowledgeIndexPdfPlan.Create(source, default)!;
        CollectionAssert.AreEqual(new[] { 4, 4, 4, 4, 1 }, plan.Batches.Select(b => b.Chunks.Count).ToArray());
        Assert.AreEqual(2, plan.Batches[^1].Chunks[0].PageNumber);
        Assert.AreEqual(0, plan.Batches[^1].Chunks[0].Offset);
    }

    [TestMethod] [DataRow("sequence")] [DataRow("first-page")] [DataRow("gap-page")] [DataRow("back-page")]
    [DataRow("same-offset")] [DataRow("end-regression")] [DataRow("negative-offset")] [DataRow("overflow")]
    [DataRow("length")] [DataRow("blank")] [DataRow("surrogate")] [DataRow("nul")]
    [DataRow("embedding")] [DataRow("input-limit")] [DataRow("zero-token")] [DataRow("token-limit")]
    public void Any_invalid_chunk_rejects_the_whole_plan(string fault)
    {
        var source = Source(); var chunks = source.Chunks.ToArray(); var chunk = chunks[1];
        chunks[1] = fault switch
        {
            "sequence" => chunk with { Sequence = 0 }, "first-page" => chunk with { PageNumber = 0 },
            "gap-page" => chunk with { PageNumber = 3 }, "back-page" => chunk with { PageNumber = -1 },
            "same-offset" => chunk with { Offset = 0 }, "end-regression" => chunk with { Offset = 1, Length = 1, Text = "x", EmbeddingText = "x" },
            "negative-offset" => chunk with { Offset = -1 }, "overflow" => chunk with { Offset = int.MaxValue },
            "length" => chunk with { Length = 1 }, "blank" => chunk with { Text = new string(' ', 20), EmbeddingText = new string(' ', 20) },
            "surrogate" => chunk with { Text = new string('\ud800', 20), EmbeddingText = new string('\ud800', 20) },
            "nul" => chunk with { Text = new string('\0', 20), EmbeddingText = new string('\0', 20) },
            "embedding" => chunk with { EmbeddingText = "context" + chunk.Text },
            "input-limit" => chunk with { Length = 8001, Text = new string('x', 8001), EmbeddingText = new string('x', 8001) },
            "zero-token" => chunk with { TokenCount = 0 }, _ => chunk with { TokenCount = 513 }
        };
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(source with { Chunks = chunks }, default));
    }

    [TestMethod] [DataRow("knowledge")] [DataRow("knowledge-version")] [DataRow("model")]
    [DataRow("model-version")] [DataRow("tenant")] [DataRow("provider")] [DataRow("model-name")]
    [DataRow("parser")] [DataRow("tokenizer")] [DataRow("options")] [DataRow("hash")]
    public void Unsupported_or_incomplete_identity_has_no_plan(string fault)
    {
        var source = Source(); source = fault switch
        {
            "knowledge" => source with { KnowledgeBaseId = Guid.Empty }, "knowledge-version" => source with { KnowledgeBaseVersion = 0 },
            "model" => source with { ModelConfigId = Guid.Empty }, "model-version" => source with { ModelConfigVersion = 0 },
            "tenant" => source with { TenantId = Guid.Empty }, "provider" => source with { ProviderKey = "other" },
            "model-name" => source with { ModelId = "unknown" }, "parser" => source with { ParserVersion = "utf8-text-v1" },
            "tokenizer" => source with { TokenizerVersion = "unknown" }, "options" => source with { Options = new(MaxTokens: 256) },
            _ => source with { ContentHash = new string('g', 64) }
        };
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(source, default));
    }

    [TestMethod]
    public void Overlap_is_allowed_only_when_page_coverage_advances()
    {
        var source = Source(); var chunks = source.Chunks.ToArray(); chunks[1] = chunks[1] with { Offset = 10 };
        Assert.IsNotNull(AiKnowledgeIndexPdfPlan.Create(source with { Chunks = chunks }, default));
        chunks[0] = chunks[0] with { PageNumber = 2 };
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(source with { Chunks = chunks }, default));
    }

    [TestMethod]
    public void Page_and_global_output_budgets_are_enforced()
    {
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(Source(2049), default));
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(Source() with { Chunks = [] }, default));
        var source = Source(129) with { Chunks = Enumerable.Range(0, 129).Select(i => new AiKnowledgePdfChunk(i, i + 1, 0, 1, "x", "x", 1)).ToArray() };
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(source, default));
        Assert.IsNotNull(AiKnowledgeIndexPdfPlan.Create(source with { Chunks = source.Chunks.Take(128).ToArray() }, default));
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(Source(2048, 8000), default));
        // 各页可独立合法，但累计页内覆盖终点仍不得超过原始提取字符预算。
        var pages = Source(128) with { Chunks = Enumerable.Range(0, 128).Select(i => new AiKnowledgePdfChunk(i, i + 1, 16384, 1, "x", "x", 1)).ToArray() };
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(pages, default));
        Assert.IsNotNull(AiKnowledgeIndexPdfPlan.Create(pages with { Chunks = pages.Chunks.Select(c => c with { Offset = 16383 }).ToArray() }, default));
        var overlap = Source(2048, 4000);
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(overlap with { Chunks = overlap.Chunks.Select(c => c with { Offset = c.Sequence % 16 }).ToArray() }, default));
    }

    [TestMethod] [DataRow("page")] [DataRow("offset")] [DataRow("knowledge-version")] [DataRow("model-version")]
    [DataRow("tenant")] [DataRow("hash")] [DataRow("model-name")]
    public void Page_positions_and_approved_source_identity_change_both_digests(string change)
    {
        var source = Source(); var changed = source; var chunks = source.Chunks.ToArray();
        if (change == "page") { chunks[1] = chunks[1] with { PageNumber = 2, Offset = 0 }; changed = source with { Chunks = chunks }; }
        else if (change == "offset") { chunks[1] = chunks[1] with { Offset = 21 }; changed = source with { Chunks = chunks }; }
        else changed = change switch
        {
            "knowledge-version" => source with { KnowledgeBaseVersion = 4 }, "model-version" => source with { ModelConfigVersion = 4 },
            "tenant" => source with { TenantId = Guid.Parse("019a2ba0-1234-7000-8000-000000000003") },
            "hash" => source with { ContentHash = new string('b', 64) }, _ => source with { ModelId = "text-embedding-3-large" }
        };
        var original = AiKnowledgeIndexPdfPlan.Create(source, default)!; var altered = AiKnowledgeIndexPdfPlan.Create(changed, default)!;
        Assert.AreNotEqual(original.Summary.PlanHash, altered.Summary.PlanHash);
        Assert.AreNotEqual(original.Batches[0].SourceHash, altered.Batches[0].SourceHash);
        Assert.AreEqual(original.Summary, AiKnowledgeIndexPdfPlan.Create(source with { ContentHash = source.ContentHash.ToUpperInvariant() }, default)!.Summary);
    }

    [TestMethod]
    public async Task Real_parser_pages_and_approved_model_chunks_are_consumed_without_flattening()
    {
        var source = Source(); var pages = new[] { "中文 😀\r\n第一页", "第二页 e\u0301\n原始文字" };
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new { protocolVersion = 1, contentHash = source.ContentHash,
            parserVersion = AiKnowledgeParsedPdf.ParserVersion, pages = pages.Select((text, i) => new { pageNumber = i + 1, text }) }));
        var parsed = await AiKnowledgePdfOutputReader.ReadAsync(stream, source.ContentHash, 97, default);
        var knowledge = new AiKnowledgeBaseRecord { Id = source.KnowledgeBaseId, Version = 2, IsEnabled = true, DataClassification = "internal", EmbeddingModelConfigId = source.ModelConfigId, EmbeddingModelVersion = 3 };
        var model = new AiModelConfigRecord { Id = source.ModelConfigId, Version = 3, IsEnabled = true, ProviderKey = source.ProviderKey, ModelId = source.ModelId, EndpointBaseUrl = "https://api.openai.com/v1" };
        var chunks = AiKnowledgePdfModelChunker.Chunk(knowledge, model, parsed.Document!, new(), default);
        Assert.IsTrue(chunks.IsSuccess);
        var plan = AiKnowledgeIndexPdfPlan.Create(chunks.Output!, default)!;
        var manifest = AiKnowledgeIndexPdfManifestCodec.Decode(AiKnowledgeIndexPdfManifestCodec.Encode(plan, default), default);
        Assert.AreEqual(2, manifest.Identity.PageCount);
        CollectionAssert.AreEqual(pages, plan.Batches.SelectMany(b => b.Chunks).Select(c => c.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 0 }, plan.Batches.SelectMany(b => b.Chunks).Select(c => c.Offset).ToArray());
    }

    [TestMethod] [DataRow(2)] [DataRow(17)] [DataRow(2048)]
    public void Manifest_round_trips_a_complete_immutable_collection_without_body(int count)
    {
        var plan = AiKnowledgeIndexPdfPlan.Create(Source(count), default)!;
        var bytes = AiKnowledgeIndexPdfManifestCodec.Encode(plan, default);
        var manifest = AiKnowledgeIndexPdfManifestCodec.Decode(bytes, default);
        Assert.AreEqual(plan.Summary, manifest.Summary); Assert.AreEqual(plan.Identity, manifest.Identity);
        Assert.AreEqual(count, manifest.Batches.Sum(b => b.ChunkCount));
        Assert.AreEqual(plan.Identity.PageCount, manifest.Batches[^1].LastPageNumber);
        CollectionAssert.AreEqual(plan.Batches.Select(b => b.SourceHash).ToArray(), manifest.Batches.Select(b => b.SourceHash).ToArray());
        Assert.IsFalse(Encoding.UTF8.GetString(bytes).Contains(new string('x', 20), StringComparison.Ordinal));
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AiKnowledgeIndexPdfManifestBatch>)manifest.Batches)[0] = manifest.Batches[0]);
        CollectionAssert.AreEqual(bytes, AiKnowledgeIndexPdfManifestCodec.Encode(plan, default));
    }

    [TestMethod] [DataRow("empty")] [DataRow("truncated")] [DataRow("trailing")] [DataRow("large")]
    [DataRow("hash")] [DataRow("negative-length")] [DataRow("huge-length")] [DataRow("utf8")]
    [DataRow("page-range")] [DataRow("first-sequence")] [DataRow("count")] [DataRow("version")]
    public void Malformed_manifest_never_yields_partial_batches(string fault)
    {
        var plan = AiKnowledgeIndexPdfPlan.Create(Source(17), default)!;
        var bytes = AiKnowledgeIndexPdfManifestCodec.Encode(plan, default);
        // 最末批次固定为 ordinal/sequence/count/token/firstPage/lastPage/hashLength/hash。
        var last = bytes.Length - 96;
        switch (fault)
        {
            case "empty": bytes = []; break; case "truncated": bytes = bytes[..^1]; break;
            case "trailing": bytes = [.. bytes, 0]; break; case "large": bytes = new byte[131073]; break;
            case "hash": bytes[^1] = bytes[^1] == (byte)'A' ? (byte)'B' : (byte)'A'; break;
            case "negative-length": BinaryPrimitives.WriteInt32LittleEndian(bytes, -1); break;
            case "huge-length": BinaryPrimitives.WriteInt32LittleEndian(bytes, int.MaxValue); break;
            case "utf8": bytes[4] = 0xff; break;
            case "page-range": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(last + 24), 1); break;
            case "first-sequence": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(last + 4), 0); break;
            case "count": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(last + 8), int.MaxValue); break;
            case "version": bytes[4 + AiKnowledgeIndexPdfManifestCodec.FormatKey.Length - 1] = (byte)'2'; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfManifestCodec.Decode(bytes, default));
    }

    [TestMethod]
    public void Legacy_text_and_pdf_wire_formats_reject_each_other()
    {
        var text = AiKnowledgeIndexTextManifestCodec.Encode(AiKnowledgeIndexTextPlan.Create(AiKnowledgeIndexTextPlanTests.Source(), default)!, default);
        var pdf = AiKnowledgeIndexPdfManifestCodec.Encode(AiKnowledgeIndexPdfPlan.Create(Source(), default)!, default);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfManifestCodec.Decode(text, default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexTextManifestCodec.Decode(pdf, default));
    }

    [TestMethod] [DataRow("ordinal")] [DataRow("sequence")] [DataRow("size")] [DataRow("tokens")]
    [DataRow("page-zero")] [DataRow("page-gap")] [DataRow("page-end")] [DataRow("page-count")]
    public void Correctly_hashed_but_impossible_manifest_metadata_is_rejected(string fault)
    {
        var plan = AiKnowledgeIndexPdfPlan.Create(Source(17), default)!;
        var manifest = AiKnowledgeIndexPdfManifestCodec.Decode(AiKnowledgeIndexPdfManifestCodec.Encode(plan, default), default);
        var batches = manifest.Batches.ToArray(); var batch = batches[1]; var identity = manifest.Identity;
        if (fault == "page-count") identity = identity with { PageCount = 3 };
        else batches[1] = fault switch
        {
            "ordinal" => batch with { Ordinal = 0 }, "sequence" => batch with { FirstSequence = 0 },
            "size" => batch with { ChunkCount = 17 }, "tokens" => batch with { TokenCount = 513 },
            "page-zero" => batch with { FirstPageNumber = 0 }, "page-gap" => batch with { FirstPageNumber = 3, LastPageNumber = 3 },
            _ => batch with { LastPageNumber = 1 }
        };
        var summary = manifest.Summary with { PlanHash = AiKnowledgeIndexPdfPlan.ComputePlanHash(identity, manifest.Summary, batches, default) };
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        Text(writer, AiKnowledgeIndexPdfManifestCodec.FormatKey); AiKnowledgeIndexPdfPlan.WriteHeader(writer, identity);
        Text(writer, summary.PlanHash); writer.Write(summary.BatchCount); writer.Write(summary.ChunkCount); writer.Write(summary.TokenCount);
        foreach (var item in batches)
        {
            writer.Write(item.Ordinal); writer.Write(item.FirstSequence); writer.Write(item.ChunkCount); writer.Write(item.TokenCount);
            writer.Write(item.FirstPageNumber); writer.Write(item.LastPageNumber); Text(writer, item.SourceHash);
        }
        writer.Flush(); Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexPdfManifestCodec.Decode(stream.ToArray(), default));
    }

    private static void Text(BinaryWriter writer, string value)
    { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }

    [TestMethod]
    public void Cancellation_precedes_input_enumeration_and_wire_allocation()
    {
        var plan = AiKnowledgeIndexPdfPlan.Create(Source(), default)!;
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexPdfPlan.Create(Source() with { Chunks = new NeverRead() }, cancel.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexPdfManifestCodec.Encode(plan, cancel.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexPdfManifestCodec.Decode(new byte[131073], cancel.Token));
        Assert.IsNull(AiKnowledgeIndexPdfPlan.Create(Source() with { Chunks = new NeverRead() }, default));
    }

    internal static AiKnowledgePdfModelChunks Source(int count = 2, int length = 20) => new(
        Guid.Parse("019a2ba0-1234-7000-8000-000000000001"), 2, null,
        Guid.Parse("019a2ba0-1234-7000-8000-000000000002"), 3,
        "openai_compatible", "text-embedding-3-small", new string('a', 64),
        AiKnowledgeParsedPdf.ParserVersion, AiKnowledgeCl100kTokenizer.Version, new(),
        Enumerable.Range(0, count).Select(i => new AiKnowledgePdfChunk(i, i / 16 + 1, i % 16 * length, length,
            new string('x', length), new string('x', length), 2)).ToArray());

    private sealed class NeverRead : IReadOnlyList<AiKnowledgePdfChunk>
    {
        public int Count => 2049;
        public AiKnowledgePdfChunk this[int index] => throw new AssertFailedException("超限或已取消集合不可读取。");
        public IEnumerator<AiKnowledgePdfChunk> GetEnumerator() => throw new AssertFailedException("超限或已取消集合不可枚举。");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
