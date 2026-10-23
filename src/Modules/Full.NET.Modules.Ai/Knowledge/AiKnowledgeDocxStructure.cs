using System.Buffers;
using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>校验提取正文的完整逻辑坐标；不能替代隔离工具对 OOXML 的验证。</summary>
internal static class AiKnowledgeDocxStructure
{
    internal static AiKnowledgeDocxReadFailure Validate(AiKnowledgeParsedDocx document, CancellationToken token, bool allowEmpty = false)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(document);
        if (!AiKnowledgeDocxOutputReader.ValidSource(document.ContentHash, document.SizeBytes)) return AiKnowledgeDocxReadFailure.InvalidSource;
        if (document.Text is null || document.Text.Length > AiKnowledgeTextParser.MaxOutputCharacters) return AiKnowledgeDocxReadFailure.OutputTooLarge;
        var count = document.Blocks?.Count ?? 0;
        if (!allowEmpty && string.IsNullOrWhiteSpace(document.Text) || document.Text.Contains('\0') || count is < 1 or > 8192) return Invalid();
        var checkAt = 0;
        for (var position = 0; position < document.Text.Length;)
        {
            if (position >= checkAt) { token.ThrowIfCancellationRequested(); checkAt = position + 4096; }
            if (Rune.DecodeFromUtf16(document.Text.AsSpan(position), out _, out var consumed) != OperationStatus.Done) return Invalid();
            position += consumed;
        }
        var cursor = 0; var paragraph = 1; var table = 0; var row = 0; var cell = 0; var rows = 0; var closed = false; var header = false;
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested(); var block = document.Blocks![index];
            if (block is null || block.Sequence != index || block.Offset != cursor || block.Length < 0 || block.Length >= document.Text.Length - cursor
                || block.ParagraphNumber != paragraph) return Invalid();
            if (block.Kind == "paragraph")
            {
                if (block.TableNumber is not null || block.RowNumber is not null || block.CellNumber is not null || block.CellParagraphCount != 0 || block.IsHeader) return Invalid();
                paragraph++; closed = true;
            }
            else if (block.Kind == "tableCell")
            {
                if (block.TableNumber is not (>= 1 and <= 128) || block.RowNumber is not (>= 1 and <= 4096) || block.CellNumber is not (>= 1 and <= 64)
                    || block.CellParagraphCount is < 1 or > 8192 || block.CellParagraphCount > 8193 - paragraph) return Invalid();
                // 单元格内每两个真实段落至少有一个结构换行；行内换行可以更多，不反推精确段落数。
                if (block.CellParagraphCount - 1 > document.Text.AsSpan(block.Offset, block.Length).Count('\n')) return Invalid();
                if (block.TableNumber != table)
                {
                    if (block.TableNumber != table + 1 || block.RowNumber != 1 || block.CellNumber != 1) return Invalid();
                    table++; row = 0; cell = 0; closed = false;
                }
                else if (closed) return Invalid();
                if (block.RowNumber != row)
                { if (block.RowNumber != row + 1 || block.CellNumber != 1 || ++rows > 4096) return Invalid(); row++; cell = 0; header = block.IsHeader; }
                if (block.CellNumber != cell + 1 || block.IsHeader != header) return Invalid();
                cell++; paragraph += block.CellParagraphCount;
            }
            else return Invalid();
            if (paragraph > 8193) return Invalid();
            var next = index + 1 < count ? document.Blocks[index + 1] : null;
            var separator = block.Kind == "tableCell" && next is not null && next.Kind == "tableCell" && next.TableNumber == block.TableNumber && next.RowNumber == block.RowNumber ? '\t' : '\n';
            cursor += block.Length;
            if (document.Text[cursor] != separator) return Invalid();
            cursor++;
        }
        token.ThrowIfCancellationRequested(); return cursor == document.Text.Length ? AiKnowledgeDocxReadFailure.None : Invalid();
    }

    /// <summary>先按固定数量逐项复制，再校验快照，避免来源集合后续变更改变已验收几何。</summary>
    internal static AiKnowledgeParsedDocx? Freeze(AiKnowledgeParsedDocx document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(document);
        var count = document.Blocks?.Count ?? 0;
        if (count is < 1 or > 8192) return null;
        var blocks = new AiKnowledgeDocxBlock[count];
        for (var index = 0; index < blocks.Length; index++) { token.ThrowIfCancellationRequested(); blocks[index] = document.Blocks![index]; }
        var frozen = document with { ContentHash = document.ContentHash?.ToLowerInvariant()!, Blocks = Array.AsReadOnly(blocks) };
        return Validate(frozen, token) == AiKnowledgeDocxReadFailure.None ? frozen : null;
    }
    private static AiKnowledgeDocxReadFailure Invalid() => AiKnowledgeDocxReadFailure.InvalidStructure;
}
