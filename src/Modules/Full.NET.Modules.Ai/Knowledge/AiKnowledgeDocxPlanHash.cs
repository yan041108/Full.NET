using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>独立 v1 规划摘要：固定小端整数、长度前缀 UTF-8、规范 Guid 和全部逻辑位置。</summary>
internal static class AiKnowledgeDocxPlanHash
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal static string Document(AiKnowledgeParsedDocx document, CancellationToken token) => Digest(writer =>
    {
        Text(writer, AiKnowledgeParsedDocx.ParserVersion); Text(writer, document.ContentHash); writer.Write(document.SizeBytes); Text(writer, document.Text);
        writer.Write(document.Blocks.Count);
        foreach (var block in document.Blocks)
        {
            token.ThrowIfCancellationRequested(); writer.Write(block.Sequence); Text(writer, block.Kind); writer.Write(block.Offset); writer.Write(block.Length);
            writer.Write(block.ParagraphNumber); writer.Write(block.TableNumber ?? 0); writer.Write(block.RowNumber ?? 0); writer.Write(block.CellNumber ?? 0);
            writer.Write(block.CellParagraphCount); writer.Write(block.IsHeader);
        }
    });
    internal static string Batch(AiKnowledgeDocxModelChunks source, string documentHash, int ordinal, IReadOnlyList<AiKnowledgeDocxChunk> chunks, CancellationToken token) => Digest(writer =>
    {
        Header(writer, source, documentHash); writer.Write(ordinal); writer.Write(chunks.Count);
        foreach (var chunk in chunks)
        { token.ThrowIfCancellationRequested(); writer.Write(chunk.Sequence); writer.Write(chunk.BlockSequence); writer.Write(chunk.Offset); writer.Write(chunk.Length); writer.Write(chunk.TokenCount); Text(writer, chunk.Text); }
    });
    internal static string Plan(AiKnowledgeDocxModelChunks source, string documentHash, IReadOnlyList<AiKnowledgeIndexDocxBatch> batches, long tokens, CancellationToken token) => Digest(writer =>
    {
        Header(writer, source, documentHash); writer.Write(source.Chunks.Count); writer.Write(tokens); writer.Write(batches.Count);
        foreach (var batch in batches) { token.ThrowIfCancellationRequested(); writer.Write(batch.Ordinal); writer.Write(batch.Chunks.Count); Text(writer, batch.SourceHash); }
    });
    private static void Header(BinaryWriter writer, AiKnowledgeDocxModelChunks source, string documentHash)
    {
        Text(writer, AiKnowledgeIndexDocxPlan.FormatKey); Text(writer, "docx-logical-window-v1"); Text(writer, documentHash);
        Text(writer, source.KnowledgeBaseId.ToString("N")); writer.Write(source.KnowledgeBaseVersion); Text(writer, source.TenantId?.ToString("N") ?? "host");
        Text(writer, source.ModelConfigId.ToString("N")); writer.Write(source.ModelConfigVersion); Text(writer, source.ProviderKey); Text(writer, source.ModelId); Text(writer, source.TokenizerVersion);
        writer.Write(source.Options.MaxTokens); writer.Write(source.Options.OverlapTokens); writer.Write(source.Options.MaxChunks); writer.Write(source.Options.MaxTotalTokens);
    }
    private static void Text(BinaryWriter writer, string value)
    {
        var bytes = Utf8.GetBytes(value);
        try { writer.Write(bytes.Length); writer.Write(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static string Digest(Action<BinaryWriter> write)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); using var stream = new HashStream(hash); using var writer = new BinaryWriter(stream, Utf8);
        write(writer); writer.Flush(); return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private sealed class HashStream(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
