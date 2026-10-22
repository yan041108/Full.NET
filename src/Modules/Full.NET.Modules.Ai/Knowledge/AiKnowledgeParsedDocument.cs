using System.Buffers;
using System.Text;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>已接线的解析格式；新增格式必须提供完整解析、来源映射和验收，不能仅扩展枚举。</summary>
internal enum AiKnowledgeParsedFormat { PlainText, Markdown, Pdf, Docx }

/// <summary>提取正文的坐标类型；PDF 使用页内位置，DOCX 使用全文位置且不冒充渲染页码。</summary>
internal enum AiKnowledgeSourcePositionKind { Document, PdfPage, DocxParagraph, DocxTableCell }

/// <summary>不可变来源区间，Offset 和 Length 均以原提取文本的 UTF-16 单元计数。</summary>
internal sealed record AiKnowledgeSourcePosition(AiKnowledgeSourcePositionKind Kind, int Offset, int Length,
    int? PageNumber = null, int? BlockSequence = null, int? ParagraphNumber = null,
    int? TableNumber = null, int? RowNumber = null, int? CellNumber = null,
    int CellParagraphCount = 0, bool IsHeader = false);

/// <summary>完整解析中的单个逻辑块；结构分隔符独立保存，空单元格仍保留真实坐标。</summary>
internal sealed record AiKnowledgeParsedBlock(int Sequence, string Text, string Separator, AiKnowledgeSourcePosition Position)
{
    /// <summary>只接受当前逻辑块内的完整 Unicode 区间，绝不生成跨页、跨单元格或半代理项引用。</summary>
    internal AiKnowledgeSourcePosition? Locate(int offset, int length)
    {
        if (offset < 0 || length < 1 || offset > Text.Length || length > Text.Length - offset
            || !Boundary(Text, offset) || !Boundary(Text, offset + length)) return null;
        return Position with { Offset = checked(Position.Offset + offset), Length = length };
    }

    private static bool Boundary(string text, int offset)
        => offset == 0 || offset == text.Length || !(char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]));
}

/// <summary>四种现有格式的统一完整快照；工厂先冻结及验收，再交给分块入口使用。</summary>
/// <remarks>本地解析证明不授予文件访问、模型外发或发布资格；不含凭据、会话和租户授权结果。</remarks>
internal sealed class AiKnowledgeParsedDocument
{
    private AiKnowledgeParsedDocument(AiKnowledgeParsedFormat format, string parserVersion, string hash, int size,
        AiKnowledgeParsedBlock[] blocks, AiKnowledgeParsedDocx? docx = null)
    {
        Format = format; ParserVersion = parserVersion; ContentHash = hash.ToLowerInvariant(); SizeBytes = size;
        Blocks = Array.AsReadOnly(blocks); Docx = docx;
    }

    internal AiKnowledgeParsedFormat Format { get; }
    internal string ParserVersion { get; }
    internal string ContentHash { get; }
    internal int SizeBytes { get; }
    internal IReadOnlyList<AiKnowledgeParsedBlock> Blocks { get; }
    // DOCX 的完整清单继续携带既有全文和结构，统一分块映射与清单消费同一冻结对象。
    internal AiKnowledgeParsedDocx? Docx { get; }

    /// <summary>普通文本和 Markdown 不归一化正文；格式由可信版本元数据提供，不从内容猜测。</summary>
    internal static AiKnowledgeParsedDocument? FromText(AiKnowledgeParsedText document, AiKnowledgeTextFormat format, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(document);
        if (document.Source != AiKnowledgeTextSource.Utf8 || format is not (AiKnowledgeTextFormat.PlainText or AiKnowledgeTextFormat.Markdown)
            || !ValidIdentity(document.ContentHash, document.SizeBytes, Domain.AiKnowledgeFilePolicy.MaxBytes)
            || !ValidText(document.Text, token) || string.IsNullOrWhiteSpace(document.Text)) return null;
        return new(format == AiKnowledgeTextFormat.Markdown ? AiKnowledgeParsedFormat.Markdown : AiKnowledgeParsedFormat.PlainText,
            document.ParserVersion, document.ContentHash, document.SizeBytes,
            [new(0, document.Text, "", new(AiKnowledgeSourcePositionKind.Document, 0, document.Text.Length))]);
    }

    /// <summary>页集合按首次有界数量复制，验证和分块均只使用快照，保留实际页内位置。</summary>
    internal static AiKnowledgeParsedDocument? FromPdf(AiKnowledgeParsedPdf document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(document);
        if (!ValidIdentity(document.ContentHash, document.SizeBytes, AiKnowledgePdfOutputReader.MaxSourceBytes)) return null;
        var count = document.Pages?.Count ?? 0;
        if (count is < 1 or > AiKnowledgePdfOutputReader.MaxPages) return null;
        var pages = new AiKnowledgePdfPage[count];
        for (var index = 0; index < count; index++) { token.ThrowIfCancellationRequested(); pages[index] = document.Pages![index]; }
        var frozen = document with { Pages = Array.AsReadOnly(pages) };
        if (AiKnowledgePdfOutputReader.ValidateDocument(frozen, token) != AiKnowledgePdfReadFailure.None) return null;
        var blocks = new AiKnowledgeParsedBlock[count];
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested(); var page = pages[index];
            blocks[index] = new(index, page.Text, "", new(AiKnowledgeSourcePositionKind.PdfPage, 0, page.Text.Length, PageNumber: page.PageNumber));
        }
        token.ThrowIfCancellationRequested();
        return new(AiKnowledgeParsedFormat.Pdf, AiKnowledgeParsedPdf.ParserVersion, document.ContentHash, document.SizeBytes, blocks);
    }

    /// <summary>复用完整 OOXML 结构验证；不丢弃空段落、表格位置或结构换行。</summary>
    internal static AiKnowledgeParsedDocument? FromDocx(AiKnowledgeParsedDocx document, CancellationToken token)
    {
        var frozen = AiKnowledgeDocxStructure.Freeze(document, token);
        if (frozen is null) return null;
        var blocks = new AiKnowledgeParsedBlock[frozen.Blocks.Count];
        for (var index = 0; index < blocks.Length; index++)
        {
            token.ThrowIfCancellationRequested(); var block = frozen.Blocks[index];
            blocks[index] = new(index, frozen.Text.Substring(block.Offset, block.Length), frozen.Text.Substring(block.Offset + block.Length, 1),
                new(block.Kind == "tableCell" ? AiKnowledgeSourcePositionKind.DocxTableCell : AiKnowledgeSourcePositionKind.DocxParagraph,
                    block.Offset, block.Length, BlockSequence: block.Sequence, ParagraphNumber: block.ParagraphNumber,
                    TableNumber: block.TableNumber, RowNumber: block.RowNumber, CellNumber: block.CellNumber,
                    CellParagraphCount: block.CellParagraphCount, IsHeader: block.IsHeader));
        }
        token.ThrowIfCancellationRequested();
        return new(AiKnowledgeParsedFormat.Docx, AiKnowledgeParsedDocx.ParserVersion, frozen.ContentHash, frozen.SizeBytes, blocks, frozen);
    }

    internal static bool ValidIdentity(string hash, int size, long maxBytes)
        => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit) && size > 0 && size <= maxBytes;

    /// <summary>字符预算独立于原字节大小；不允许替换字符掩盖非法 UTF-16，也不在此做文本归一化。</summary>
    internal static bool ValidText(string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (text is null || text.Length > AiKnowledgeTextParser.MaxOutputCharacters || text.Contains('\0')) return false;
        var checkAt = 0;
        for (var offset = 0; offset < text.Length;)
        {
            if (offset >= checkAt) { token.ThrowIfCancellationRequested(); checkAt = offset + 4096; }
            if (Rune.DecodeFromUtf16(text.AsSpan(offset), out _, out var consumed) != OperationStatus.Done) return false;
            offset += consumed;
        }
        token.ThrowIfCancellationRequested();
        return true;
    }
}
