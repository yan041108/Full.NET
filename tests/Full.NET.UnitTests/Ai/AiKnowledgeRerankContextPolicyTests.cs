using System.Collections;
using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Modules.Ai.Knowledge;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>本地重排和上下文入口共同保护分类快照；诊断不能读取正文或改变原业务结果。</summary>
[TestClass, DoNotParallelize]
public sealed class AiKnowledgeRerankContextPolicyTests
{
    [TestMethod]
    [DataRow("text/plain", "public")] [DataRow("text/plain", "internal")] [DataRow("text/plain", "restricted")]
    [DataRow("application/pdf", "public")] [DataRow("application/pdf", "internal")] [DataRow("application/pdf", "restricted")]
    [DataRow("docx", "public")] [DataRow("docx", "internal")] [DataRow("docx", "restricted")]
    public void Rerank_context_legal_sources_keep_locations_and_fixed_observations(string format, string classification)
    {
        using var capture = new AgentTelemetryCapture(); var ids = Ids();
        var source = Chunk(classification, format);
        var ranked = AiKnowledgeVectorRanker.Rank(source.Vector, [source], default);
        var context = AiKnowledgeAnswerContext.Create("secret-question", ranked, ids, default);
        Assert.AreEqual(source.Source, context.Citations.Single().Chunk.Source);
        Assert.AreEqual(source.PageNumber, context.Citations[0].Chunk.PageNumber);
        Assert.AreEqual(source.DocxLocation, context.Citations[0].Chunk.DocxLocation);
        Assert.AreEqual(1d, ranked[0].Score, 1e-12);
        ids.Received(1).NewId(); Verify(capture, ["knowledge_rerank", "knowledge_context"], "completed");
    }

    [TestMethod]
    [DataRow("rank", "")] [DataRow("rank", "INTERNAL")] [DataRow("rank", "internal ")] [DataRow("rank", "unknown")] [DataRow("rank", null)]
    [DataRow("context", "")] [DataRow("context", "INTERNAL")] [DataRow("context", "internal ")] [DataRow("context", "unknown")] [DataRow("context", null)]
    public void Rerank_context_unknown_classification_rejects_without_citations(string stage, string? classification)
    {
        using var capture = new AgentTelemetryCapture(); var ids = Ids();
        Assert.ThrowsExactly<InvalidDataException>(() => Execute(stage, [new(Chunk(classification), 1)], ids));
        ids.DidNotReceive().NewId(); Verify(capture, [Name(stage)], "failed");
    }

    [TestMethod]
    [DataRow("rank", 0)] [DataRow("rank", 1)] [DataRow("rank", 8)] [DataRow("rank", 255)]
    [DataRow("context", 0)] [DataRow("context", 1)] [DataRow("context", 8)] [DataRow("context", 255)]
    public void Rerank_context_unknown_tail_cannot_be_hidden_by_top_eight(string stage, int position)
    {
        var rows = Enumerable.Range(0, 256).Select(_ => new AiKnowledgeRankedChunk(Chunk("internal"), 1)).ToArray();
        rows[position] = rows[position] with { Chunk = rows[position].Chunk with { Source = rows[position].Chunk.Source with { DataClassification = "unknown" } } };
        using var capture = new AgentTelemetryCapture(); var ids = Ids();
        Assert.ThrowsExactly<InvalidDataException>(() => Execute(stage, rows, ids));
        ids.DidNotReceive().NewId(); Verify(capture, [Name(stage)], "failed");
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Rerank_context_empty_vector_probe_is_not_a_rerank_attempt(bool invalid)
    {
        using var capture = new AgentTelemetryCapture(); var vector = AiKnowledgeRetrievalRuleTests.Vector(invalid ? 0 : 1, 0);
        if (invalid) Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeVectorRanker.Rank(vector, [], default));
        else Assert.HasCount(0, AiKnowledgeVectorRanker.Rank(vector, [], default));
        Assert.HasCount(0, capture.Spans); Assert.HasCount(0, capture.Measurements);
    }

    [TestMethod] [DataRow("rank")] [DataRow("context")]
    public void Rerank_context_pre_cancelled_work_has_no_attempt_or_identity(string stage)
    {
        using var capture = new AgentTelemetryCapture(); using var cancel = new CancellationTokenSource(); cancel.Cancel(); var ids = Ids();
        Assert.ThrowsExactly<OperationCanceledException>(() => Execute(stage, [new(Chunk("internal"), 1)], ids, cancel.Token));
        ids.DidNotReceive().NewId(); Assert.HasCount(0, capture.Spans); Assert.HasCount(0, capture.Measurements);
    }

    [TestMethod]
    [DataRow("rank", "attempts")] [DataRow("rank", "duration")] [DataRow("rank", "sample")] [DataRow("rank", "started")] [DataRow("rank", "stopped")]
    [DataRow("context", "attempts")] [DataRow("context", "duration")] [DataRow("context", "sample")] [DataRow("context", "started")] [DataRow("context", "stopped")]
    public void Rerank_context_listener_failure_preserves_original_result(string stage, string fault)
    {
        using var capture = new AgentTelemetryCapture(throwMeasurement: fault is "attempts" or "duration" ? fault : null,
            throwTrace: fault is "sample" or "started" or "stopped" ? fault : null);
        var ids = Ids(); Execute(stage, [new(Chunk("internal"), 1)], ids);
        ids.Received(stage == "rank" ? 0 : 1).NewId(); Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    public void Rerank_context_identity_callback_cannot_replace_frozen_source()
    {
        var rows = new[] { new AiKnowledgeRankedChunk(Chunk("internal"), 1), new(Chunk("restricted"), .5) };
        var original = rows[1].Chunk; var ids = Substitute.For<IIdGenerator>(); var calls = 0;
        ids.NewId().Returns(_ => { if (++calls == 1) rows[1] = rows[1] with { Chunk = Chunk("unknown") }; return Guid.CreateVersion7(); });
        using var capture = new AgentTelemetryCapture(); var context = AiKnowledgeAnswerContext.Create("secret-question", rows, ids, default);
        Assert.HasCount(2, context.Citations); Assert.AreEqual(original.Source, context.Citations[1].Chunk.Source);
        Verify(capture, ["knowledge_context"], "completed");
    }

    [TestMethod] [DataRow("rank")] [DataRow("context")]
    public void Rerank_context_bounded_copy_reads_count_once_without_enumeration(string stage)
    {
        var row = new AiKnowledgeRankedChunk(Chunk("internal"), 1); var values = new ChangingCount<AiKnowledgeRankedChunk>(row); var ids = Ids();
        using var capture = new AgentTelemetryCapture();
        if (stage == "context") Assert.HasCount(1, AiKnowledgeAnswerContext.Create("secret-question", values, ids, default).Citations);
        else
        {
            var chunks = new ChangingCount<AiKnowledgeRetrievalChunk>(row.Chunk);
            Assert.HasCount(1, AiKnowledgeVectorRanker.Rank(row.Chunk.Vector, chunks, default)); Assert.AreEqual(1, chunks.Reads);
        }
        if (stage == "context") Assert.AreEqual(1, values.Reads);
        Verify(capture, [Name(stage)], "completed");
    }

    private static void Execute(string stage, IReadOnlyList<AiKnowledgeRankedChunk> rows, IIdGenerator ids, CancellationToken token = default)
    {
        if (stage == "context") Assert.IsNotNull(AiKnowledgeAnswerContext.Create("secret-question", rows, ids, token));
        else Assert.HasCount(rows.Count, AiKnowledgeVectorRanker.Rank(AiKnowledgeRetrievalRuleTests.Vector(1, 0), rows.Select(r => r.Chunk).ToArray(), token));
    }
    private static string Name(string stage) => stage == "rank" ? "knowledge_rerank" : "knowledge_context";
    private static IIdGenerator Ids() { var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7()); return ids; }
    private static AiKnowledgeRetrievalChunk Chunk(string? classification, string format = "text/plain")
    {
        var row = AiKnowledgeRetrievalRuleTests.Chunk(format == "docx" ? Full.NET.Modules.Ai.Domain.AiKnowledgeFilePolicy.DocxContentType : format,
            0, AiKnowledgeRetrievalRuleTests.Vector(1, 0));
        return row with { Source = row.Source with { DataClassification = classification! }, DocxLocation = format == "docx" ? new(0, 1, null, null, null) : null };
    }
    private static void Verify(AgentTelemetryCapture capture, string[] stages, string outcome)
    {
        Assert.HasCount(stages.Length, capture.Spans); Assert.HasCount(stages.Length * 2, capture.Measurements);
        CollectionAssert.AreEquivalent(stages, capture.Spans.Select(s => (string)s.GetTagItem("ai.stage")!).ToArray());
        foreach (var span in capture.Spans)
        {
            Assert.AreEqual(outcome, span.GetTagItem("ai.outcome")); Assert.AreEqual(capture.Root.TraceId, span.TraceId);
            Assert.AreEqual(capture.Root.SpanId, span.ParentSpanId); Assert.IsNull(span.StatusDescription); Assert.IsFalse(span.Events.Any());
            Assert.IsTrue(span.TagObjects.All(t => t.Key is "ai.stage" or "ai.outcome"));
        }
        foreach (var m in capture.Measurements)
        {
            CollectionAssert.AreEquivalent(new[] { "ai.stage", "ai.outcome" }, m.Tags.Select(t => t.Key).ToArray());
            Assert.IsTrue(m.Name.EndsWith("attempts", StringComparison.Ordinal) ? m.Value == 1 : m.Value >= 0);
        }
        Assert.AreSame(capture.Root, Activity.Current);
    }
    /// <summary>调用方集合可能改变计数；快照只能按首次有界索引读取，不能枚举。</summary>
    private sealed class ChangingCount<T>(T row) : IReadOnlyList<T>
    {
        internal int Reads;
        public int Count => ++Reads == 1 ? 1 : 9999;
        public T this[int index] => index == 0 ? row : throw new AssertFailedException("只能读取首个有界快照元素。");
        public IEnumerator<T> GetEnumerator() => throw new AssertFailedException("禁止枚举外部集合。");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
