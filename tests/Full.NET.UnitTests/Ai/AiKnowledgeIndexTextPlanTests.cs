using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>完整规划和批次边界不得漏掉末批，也不能由单批结果推断整代次已完成。</summary>
[TestClass]
public sealed class AiKnowledgeIndexTextPlanTests
{
    [TestMethod] [DataRow(1, 1)] [DataRow(16, 1)] [DataRow(17, 2)] [DataRow(2048, 128)]
    public void Count_boundary_covers_each_source_chunk_once(int count, int batches)
    {
        var source = Source(count); var plan = AiKnowledgeIndexTextPlan.Create(source, default);
        Assert.IsNotNull(plan); Assert.AreEqual(batches, plan.Summary.BatchCount); Assert.AreEqual(count, plan.Summary.ChunkCount);
        Assert.AreEqual(count * 2L, plan.Summary.TokenCount); Assert.AreEqual(AiKnowledgeIndexTextPlan.FormatKey, plan.Summary.FormatKey);
        CollectionAssert.AreEqual(Enumerable.Range(0, count).ToArray(), plan.Batches.SelectMany(batch => batch.Chunks).Select(chunk => chunk.Sequence).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(0, batches).ToArray(), plan.Batches.Select(batch => batch.Ordinal).ToArray());
        Assert.IsTrue(plan.Batches.All(batch => batch.Chunks.Count is >= 1 and <= 16));
    }

    [TestMethod] [DataRow(8000, 4, 1)] [DataRow(8000, 5, 2)] [DataRow(6401, 5, 2)]
    public void Full_embedding_text_controls_character_boundary(int length, int count, int batches)
    {
        var plan = AiKnowledgeIndexTextPlan.Create(Source(count, length), default);
        Assert.IsNotNull(plan); Assert.AreEqual(batches, plan.Summary.BatchCount);
        Assert.IsTrue(plan.Batches.All(batch => batch.Chunks.Sum(chunk => chunk.EmbeddingText.Length) <= 32000));
        Assert.AreEqual(count, plan.Batches.Sum(batch => batch.Chunks.Count));
    }

    [TestMethod]
    public void Plan_is_deterministic_and_detached_from_source_collections()
    {
        var source = Source(17); var originals = (AiKnowledgeStructuredChunk[])source.Chunks.Chunks;
        AiKnowledgeSourceContext[] context = [new(AiKnowledgeContextKind.Heading, 0, 1, 1)];
        originals[0] = originals[0] with { Context = context, RepeatedContext = context };
        var plan = AiKnowledgeIndexTextPlan.Create(source, default); Assert.IsNotNull(plan);
        var hash = plan.Summary.PlanHash; var batchHash = plan.Batches[0].SourceHash;
        Assert.AreEqual(hash, AiKnowledgeIndexTextPlan.Create(source, default)!.Summary.PlanHash);
        originals[0] = originals[0] with { EmbeddingText = "changed" }; context[0] = context[0] with { Level = 2 };
        Assert.AreEqual("x", plan.Batches[0].Chunks[0].EmbeddingText); Assert.AreEqual(1, plan.Batches[0].Chunks[0].Context[0].Level);
        Assert.AreEqual(hash, plan.Summary.PlanHash); Assert.AreEqual(batchHash, plan.Batches[0].SourceHash);
        Assert.IsFalse(plan.Batches is AiKnowledgeIndexTextBatch[]); Assert.IsFalse(plan.Batches[0].Chunks is AiKnowledgeStructuredChunk[]);
        Assert.IsFalse(plan.Batches[0].Chunks[0].Context is AiKnowledgeSourceContext[]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AiKnowledgeStructuredChunk>)plan.Batches[0].Chunks)[0] = originals[0]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AiKnowledgeSourceContext>)plan.Batches[0].Chunks[0].Context)[0] = context[0]);
    }

    [TestMethod]
    public void Fixed_hash_format_matches_independent_little_endian_reference()
    {
        var plan = AiKnowledgeIndexTextPlan.Create(Source(), default); Assert.IsNotNull(plan);
        Assert.AreEqual("9C1B429C96ED088AF2481975BBB41887164862CEB533409FEE695CD2C174E9B8", plan.Batches[0].SourceHash);
        Assert.AreEqual("9D183C962553ED990847F06BC9F9843BC01E8A4672F7941B68D17A6421C2E964", plan.Summary.PlanHash);
    }

    [TestMethod] [DataRow("汉")] [DataRow("😀")]
    public void Character_budget_counts_utf16_units_without_replacing_valid_unicode(string glyph)
    {
        var text = string.Concat(Enumerable.Repeat(glyph, 8000 / glyph.Length)); var source = Source(4, 8000);
        source = source with { Chunks = source.Chunks with { Chunks = source.Chunks.Chunks.Select(chunk => chunk with { Text = text, EmbeddingText = text }).ToArray() } };
        var plan = AiKnowledgeIndexTextPlan.Create(source, default); Assert.IsNotNull(plan);
        Assert.AreEqual(1, plan.Summary.BatchCount); Assert.AreEqual(text, plan.Batches[0].Chunks[^1].Text);
    }

    [TestMethod]
    public void Oversize_context_is_rejected_before_copying_or_enumeration()
    {
        var source = Source(); source = source with { Chunks = source.Chunks with { Chunks = [source.Chunks.Chunks[0] with { Context = new OversizeContext() }] } };
        Assert.IsNull(AiKnowledgeIndexTextPlan.Create(source, default));
    }

    [TestMethod] [DataRow("source")] [DataRow("knowledge")] [DataRow("knowledge_version")] [DataRow("tenant")]
    [DataRow("model")] [DataRow("model_version")] [DataRow("model_id")] [DataRow("format")]
    [DataRow("text")] [DataRow("embedding")] [DataRow("offset")] [DataRow("tokens")] [DataRow("kind")]
    [DataRow("context")] [DataRow("repeated_context")]
    public void Every_interpretation_or_binding_change_alters_complete_hash(string field)
    {
        var source = Source(); var changed = field switch
        {
            "source" => source with { Chunks = source.Chunks with { ContentHash = new string('B', 64) } },
            "knowledge" => source with { KnowledgeBaseId = Guid.CreateVersion7() },
            "knowledge_version" => source with { KnowledgeBaseVersion = 2 },
            "tenant" => source with { TenantId = Guid.CreateVersion7() },
            "model" => source with { ModelConfigId = Guid.CreateVersion7() },
            "model_version" => source with { ModelConfigVersion = 2 },
            "model_id" => source with { ModelId = "text-embedding-3-large" },
            "format" => source with { Chunks = source.Chunks with { Format = AiKnowledgeTextFormat.Markdown } },
            _ => source with { Chunks = source.Chunks with { Chunks = [Change(source.Chunks.Chunks[0], field)] } }
        };
        Assert.AreNotEqual(AiKnowledgeIndexTextPlan.Create(source, default)!.Summary.PlanHash,
            AiKnowledgeIndexTextPlan.Create(changed, default)!.Summary.PlanHash);
    }

    [TestMethod] [DataRow("empty")] [DataRow("oversize")] [DataRow("gap")] [DataRow("start")] [DataRow("duplicate")]
    [DataRow("late_invalid")] [DataRow("unicode")] [DataRow("length")] [DataRow("offset")]
    [DataRow("tokens")] [DataRow("kind")] [DataRow("context")]
    [DataRow("parser")] [DataRow("tokenizer")] [DataRow("options")] [DataRow("format")]
    [DataRow("hash")] [DataRow("knowledge")] [DataRow("model")] [DataRow("tenant")] [DataRow("provider")]
    public void Invalid_generation_never_returns_partial_plan(string fault)
    {
        var source = Source(17); var chunks = source.Chunks.Chunks.ToArray();
        switch (fault)
        {
            case "empty": chunks = []; break;
            case "oversize": source = source with { Chunks = source.Chunks with { Chunks = new OversizeChunks() } }; break;
            case "gap": chunks[^1] = chunks[^1] with { Sequence = 18 }; break;
            case "start": chunks[0] = chunks[0] with { Sequence = 1 }; break;
            case "duplicate": chunks[^1] = chunks[^1] with { Sequence = 15 }; break;
            case "late_invalid": chunks[^1] = chunks[^1] with { EmbeddingText = new string('x', 8001) }; break;
            case "unicode": chunks[^1] = chunks[^1] with { Text = "\ud800", EmbeddingText = "\ud800" }; break;
            case "length": chunks[^1] = chunks[^1] with { Length = 2 }; break;
            case "offset": chunks[^1] = chunks[^1] with { Offset = int.MaxValue }; break;
            case "tokens": chunks[^1] = chunks[^1] with { TokenCount = 513 }; break;
            case "kind": chunks[^1] = chunks[^1] with { Kind = (AiKnowledgeTextBlockKind)999 }; break;
            case "context": chunks[^1] = chunks[^1] with { Context = [new(AiKnowledgeContextKind.Heading, 0, 1, 0)] }; break;
            case "parser": source = source with { Chunks = source.Chunks with { ParserVersion = "unknown" } }; break;
            case "tokenizer": source = source with { Chunks = source.Chunks with { TokenizerVersion = "unknown" } }; break;
            case "options": source = source with { Chunks = source.Chunks with { Options = new(MaxTokens: 1024) } }; break;
            case "format": source = source with { Chunks = source.Chunks with { Format = (AiKnowledgeTextFormat)999 } }; break;
            case "hash": source = source with { Chunks = source.Chunks with { ContentHash = "bad" } }; break;
            case "knowledge": source = source with { KnowledgeBaseId = Guid.Empty }; break;
            case "model": source = source with { ModelConfigVersion = 0 }; break;
            case "tenant": source = source with { TenantId = Guid.Empty }; break;
            case "provider": source = source with { ProviderKey = "unknown" }; break;
        }
        if (fault != "oversize") source = source with { Chunks = source.Chunks with { Chunks = chunks } };
        Assert.IsNull(AiKnowledgeIndexTextPlan.Create(source, default));
    }

    [TestMethod]
    public void Cancellation_is_observed_before_enumerating_source()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexTextPlan.Create(Source() with
            { Chunks = Source().Chunks with { Chunks = new OversizeChunks() } }, cancel.Token));
    }

    private static AiKnowledgeStructuredChunk Change(AiKnowledgeStructuredChunk chunk, string field) => field switch
    {
        "text" => chunk with { Text = "y", EmbeddingText = "y" }, "embedding" => chunk with { EmbeddingText = "header\nx" },
        "offset" => chunk with { Offset = 1 }, "tokens" => chunk with { TokenCount = 3 }, "kind" => chunk with { Kind = AiKnowledgeTextBlockKind.Code },
        "context" => chunk with { Context = [new(AiKnowledgeContextKind.Heading, 0, 1, 1)] },
        "repeated_context" => chunk with { RepeatedContext = [new(AiKnowledgeContextKind.TableHeader, 0, 1)] },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
    internal static AiKnowledgeModelChunks Source(int count = 1, int length = 1) => new(
        Guid.Parse("019a2ba0-1234-7000-8000-000000000001"), 1, null, Guid.Parse("019a2ba0-1234-7000-8000-000000000002"), 1,
        "openai_compatible", "text-embedding-3-small", new(new string('A', 64), "utf8-text-v1", AiKnowledgeCl100kTokenizer.Version,
            AiKnowledgeTextFormat.PlainText, new(), Enumerable.Range(0, count).Select(n => new AiKnowledgeStructuredChunk(n, n * length, length,
                new string('x', length), new string('x', length), 2, AiKnowledgeTextBlockKind.Paragraph, [], [])).ToArray()));
    private sealed class OversizeChunks : IReadOnlyList<AiKnowledgeStructuredChunk>
    {
        public int Count => int.MaxValue;
        public AiKnowledgeStructuredChunk this[int index] => throw new InvalidOperationException("must reject before allocation");
        public IEnumerator<AiKnowledgeStructuredChunk> GetEnumerator() => throw new InvalidOperationException("must reject before enumeration");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class OversizeContext : IReadOnlyList<AiKnowledgeSourceContext>
    {
        public int Count => int.MaxValue;
        public AiKnowledgeSourceContext this[int index] => throw new InvalidOperationException("must reject before allocation");
        public IEnumerator<AiKnowledgeSourceContext> GetEnumerator() => throw new InvalidOperationException("must reject before enumeration");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
