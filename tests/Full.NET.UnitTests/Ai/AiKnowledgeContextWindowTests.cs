using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>连续窗口、完整代次和独立正文预算；不猜测父子关系或伪造坐标。</summary>
[TestClass]
public sealed class AiKnowledgeContextWindowTests
{
    [TestMethod]
    [DataRow(0, 1, "0,1", false, true)]
    [DataRow(2, 2, "0,1,2,3,4", false, false)]
    [DataRow(4, 1, "3,4", true, false)]
    [DataRow(2, 0, "2", true, true)]
    public void Window_uses_actual_adjacent_sequences_and_explicit_edges(int anchor, int radius, string expected, bool before, bool after)
    {
        var chunks = Chunks(); var result = AiKnowledgeContextWindow.Select(chunks.Reverse().ToArray(), anchor, radius, default);
        Assert.AreEqual(expected, string.Join(',', result.Blocks.Select(chunk => chunk.Sequence)));
        Assert.AreEqual(before, result.HasPrevious); Assert.AreEqual(after, result.HasNext);
        foreach (var block in result.Blocks) { Assert.AreEqual(chunks[block.Sequence].Text, block.Text); Assert.AreEqual(chunks[block.Sequence].Offset, block.Offset); Assert.AreEqual(0, block.Vector.Length); }
    }
    [TestMethod] [DataRow("gap")] [DataRow("duplicate")] [DataRow("document")] [DataRow("publication")] [DataRow("empty")]
    public void Window_rejects_incomplete_or_mixed_generations(string fault)
    {
        var chunks = Chunks();
        if (fault == "gap") chunks[2] = chunks[2] with { Sequence = 7 };
        if (fault == "duplicate") chunks[2] = chunks[2] with { Sequence = 1 };
        if (fault == "document") chunks[2] = chunks[2] with { Source = chunks[2].Source with { DocumentId = Guid.CreateVersion7() } };
        if (fault == "publication") chunks[2] = chunks[2] with { Source = chunks[2].Source with { PublicationId = Guid.CreateVersion7() } };
        if (fault == "empty") chunks[2] = chunks[2] with { Text = "" };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeContextWindow.Select(chunks, 0, 1, default));
    }
    [TestMethod]
    public void Window_budget_rejects_whole_window_without_truncating_text()
    {
        var chunks = Chunks(); chunks[0] = chunks[0] with { Text = new string('字', 32768) };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeContextWindow.Select(chunks, 0, 1, default));
        Assert.AreEqual(32768, AiKnowledgeContextWindow.Select(chunks, 0, 0, default).Blocks.Single().Text.Length);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeContextWindow.Select(chunks, 5, 1, default));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeContextWindow.Select(chunks, 0, 3, default));
    }
    [TestMethod]
    public void Window_honors_cancellation_before_returning_any_body()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeContextWindow.Select(Chunks(), 0, 1, cancelled.Token));
    }
    private static AiKnowledgeRetrievalChunk[] Chunks()
    {
        var source = new AiKnowledgeIndexStatusRecord { DocumentId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(), TaskId = Guid.CreateVersion7(), PublicationId = Guid.CreateVersion7() };
        return Enumerable.Range(0, 5).Select(sequence => new AiKnowledgeRetrievalChunk(source, sequence, null, sequence * 10, 2, 2, "事实", new float[1536])).ToArray();
    }
}
