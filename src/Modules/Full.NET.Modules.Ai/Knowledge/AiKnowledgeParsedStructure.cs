namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>统一协议的封闭位置代数；任何格式、字段或几何不一致都拒绝整个输出。</summary>
internal static class AiKnowledgeParsedStructure
{
    internal const string MultiFormatParserVersion = "fullnet-multiformat-v1";

    internal static bool KnownParser(AiKnowledgeParsedFormat format, string version) => format switch
    {
        AiKnowledgeParsedFormat.PlainText or AiKnowledgeParsedFormat.Markdown => version == "utf8-text-v1",
        AiKnowledgeParsedFormat.Pdf => version is AiKnowledgeParsedPdf.ParserVersion or MultiFormatParserVersion,
        AiKnowledgeParsedFormat.Docx => version is AiKnowledgeParsedDocx.ParserVersion or MultiFormatParserVersion,
        AiKnowledgeParsedFormat.Csv or AiKnowledgeParsedFormat.Html or AiKnowledgeParsedFormat.Excel
            or AiKnowledgeParsedFormat.Word or AiKnowledgeParsedFormat.Presentation or AiKnowledgeParsedFormat.Image
            => version == MultiFormatParserVersion,
        _ => false
    };

    internal static bool Validate(AiKnowledgeParsedFormat format, IReadOnlyList<AiKnowledgeParsedBlock> blocks, CancellationToken token, bool unified = false)
    {
        var characters = 0; var hasText = false; var cursor = 0; var page = 0;
        var cells = new HashSet<(int Sheet, int Row, int Column)>();
        var anchors = new HashSet<AiKnowledgeSourcePosition>();
        for (var index = 0; index < blocks.Count; index++)
        {
            token.ThrowIfCancellationRequested(); var block = blocks[index];
            if (block is null || block.Sequence != index || block.Text is null || block.Separator is not ("" or "\n" or "\t")
                || !AiKnowledgeParsedDocument.ValidText(block.Text, token)
                || block.Text.Length > AiKnowledgeTextParser.MaxOutputCharacters - characters) return false;
            characters += block.Text.Length;
            if (block.Separator.Length > AiKnowledgeTextParser.MaxOutputCharacters - characters) return false;
            characters += block.Separator.Length; hasText |= !string.IsNullOrWhiteSpace(block.Text);
            var p = block.Position;
            if (p is null || p.Offset < 0 || p.Length != block.Text.Length || p.Offset > int.MaxValue - p.Length
                || !ValidPosition(format, p, index, unified)) return false;
            if (p.Image is { } image)
            {
                if (p.Offset != 0 || image.Parent is { } parent && !anchors.Contains(parent)) return false;
                continue;
            }
            anchors.Add(p with { Length = 0 });
            if (format is AiKnowledgeParsedFormat.PlainText or AiKnowledgeParsedFormat.Markdown)
            { if (blocks.Count != 1 || p.Offset != 0 || block.Separator.Length != 0) return false; }
            else if (format == AiKnowledgeParsedFormat.Pdf)
            { if (p.PageNumber != ++page || p.Offset != 0 || block.Separator.Length != 0 || page > 128 || !unified && string.IsNullOrWhiteSpace(block.Text)) return false; }
            else if (format is AiKnowledgeParsedFormat.Docx or AiKnowledgeParsedFormat.Word)
            { if (p.Offset != cursor || block.Separator.Length != 1) return false; cursor += p.Length + 1; }
            else if (p.Offset != 0) return false;
            if (p.Grid is { } grid && !cells.Add((grid.SheetNumber, grid.Row, grid.Column))) return false;
            if (p.Grid is { CacheState: "missing" } && block.Text.Length != 0) return false;
        }
        // 空表格块可以保留，但没有任何可识别正文的文档不能被报告为完成。
        if (!hasText) return false;
        if (format is AiKnowledgeParsedFormat.Docx or AiKnowledgeParsedFormat.Word)
        {
            var native = blocks.Where(b => b.Position.Image is null).ToArray();
            if (native.Length == 0) return false;
            // DOCX/旧 Word 的原生段落即使全空，也必须保留；OCR 不能改变它们的原始坐标。
            return AiKnowledgeDocxStructure.Validate(Docx(new string('a', 64), 1, native), token, allowEmpty: unified) == AiKnowledgeDocxReadFailure.None;
        }
        return true;
    }

    /// <summary>仅适配具有原生段落/表格几何的 Word 来源，不将其他格式伪装成 DOCX。</summary>
    internal static AiKnowledgeParsedDocx Docx(string hash, int size, IReadOnlyList<AiKnowledgeParsedBlock> blocks)
        => new(hash, size, string.Concat(blocks.Select(b => b.Text + b.Separator)), Array.AsReadOnly(blocks.Select(b => new AiKnowledgeDocxBlock(b.Sequence,
            b.Position.Kind is AiKnowledgeSourcePositionKind.DocxParagraph or AiKnowledgeSourcePositionKind.WordParagraph ? "paragraph" : "tableCell",
            b.Position.Offset, b.Position.Length, b.Position.ParagraphNumber!.Value, b.Position.TableNumber,
            b.Position.RowNumber, b.Position.CellNumber, b.Position.CellParagraphCount, b.Position.IsHeader)).ToArray()));

    private static bool ValidPosition(AiKnowledgeParsedFormat format, AiKnowledgeSourcePosition p, int sequence, bool unified)
    {
        var kind = p.Kind;
        var docx = kind is AiKnowledgeSourcePositionKind.DocxParagraph or AiKnowledgeSourcePositionKind.DocxTableCell;
        var word = kind is AiKnowledgeSourcePositionKind.WordParagraph or AiKnowledgeSourcePositionKind.WordTableCell;
        var grid = kind is AiKnowledgeSourcePositionKind.CsvCell or AiKnowledgeSourcePositionKind.WorksheetCell;
        var slide = kind is AiKnowledgeSourcePositionKind.SlideText or AiKnowledgeSourcePositionKind.SlideNotes;
        var html = kind == AiKnowledgeSourcePositionKind.HtmlNode;
        var image = kind == AiKnowledgeSourcePositionKind.ImageRegion;
        if ((p.Grid is not null) != grid || (p.Slide is not null) != slide || (p.HtmlPath is not null) != html
            || (p.Image is not null) != image || (p.PageNumber is not null) != (kind == AiKnowledgeSourcePositionKind.PdfPage)
            || (p.BlockSequence is not null) != (docx || word) || (p.ParagraphNumber is not null) != (docx || word)) return false;
        var table = kind is AiKnowledgeSourcePositionKind.DocxTableCell or AiKnowledgeSourcePositionKind.WordTableCell;
        if ((p.TableNumber is not null) != table || (p.RowNumber is not null) != table || (p.CellNumber is not null) != table
            || !table && (p.CellParagraphCount != 0 || p.IsHeader && !grid)) return false;
        if ((docx || word) && (p.BlockSequence != sequence || p.ParagraphNumber is not (>= 1 and <= 8192))) return false;
        if (table && (p.TableNumber is not (>= 1 and <= 128) || p.RowNumber is not (>= 1 and <= 4096)
            || p.CellNumber is not (>= 1 and <= 64) || p.CellParagraphCount is < 1 or > 8192)) return false;
        if (p.Grid is { } g && (!ValidGrid(g) || kind == AiKnowledgeSourcePositionKind.CsvCell && (g.SheetNumber != 1 || g.SheetName != "CSV"
            || g.CellType != "text" || g.Formula is not null || g.CacheState is not null || g.MergedRange is not null))) return false;
        if (kind == AiKnowledgeSourcePositionKind.CsvCell && p.Grid is { } csv
            && (csv.RawValue is not null || csv.NumberFormat is not null || csv.CachedValueType is not null)) return false;
        if (p.Slide is { } s && (s.SlideNumber is < 1 or > 128 || s.ShapeNumber is < 1 or > 8192
            || (s.RowNumber is null ? s.ColumnNumber is not null || s.RowSpan != 1 || s.ColumnSpan != 1
                : s.RowNumber is < 1 or > 4096 || s.ColumnNumber is not (>= 1 and <= 64) || s.RowSpan is < 1 or > 4096
                    || s.ColumnSpan is < 1 or > 64 || s.RowNumber > 4097 - s.RowSpan || s.ColumnNumber > 65 - s.ColumnSpan))) return false;
        if (html && !ValidHtmlPath(p.HtmlPath!)) return false;
        if (p.Image is { } i && (i.FrameNumber is < 1 or > 128 || i.ImageWidth is < 1 or > 16384 || i.ImageHeight is < 1 or > 16384
            || (long)i.ImageWidth * i.ImageHeight > 40000000 || i.X < 0 || i.Y < 0 || i.Width <= 0 || i.Height <= 0
            || i.X > i.ImageWidth - i.Width || i.Y > i.ImageHeight - i.Height || !double.IsFinite(i.Confidence)
            || i.Confidence is < 0 or > 1 || i.RecognitionVersion is not { Length: > 0 and <= 128 }
            || i.RecognitionVersion.Any(char.IsControl) || !AiKnowledgeParsedDocument.ValidText(i.RecognitionVersion, CancellationToken.None) || i.Orientation is < 1 or > 8
            || i.NormalizationHash is not { Length: 64 } || !i.NormalizationHash.All(char.IsAsciiHexDigit)
            || i.NormalizationVersion != "image-rgb-exif-white-v1"
            || i.OriginalWidth != (i.Orientation >= 5 ? i.ImageHeight : i.ImageWidth)
            || i.OriginalHeight != (i.Orientation >= 5 ? i.ImageWidth : i.ImageHeight)
            || (i.Parent is not null && (i.Parent.Kind == AiKnowledgeSourcePositionKind.ImageRegion || i.Parent.Length != 0
                || !ValidPosition(format, i.Parent, i.Parent.BlockSequence ?? 0, false)))
            || (i.TableNumber is null ? i.RowNumber is not null || i.ColumnNumber is not null
                : i.TableNumber is < 1 or > 128 || i.RowNumber is not (>= 1 and <= 4096) || i.ColumnNumber is not (>= 1 and <= 64)))) return false;
        if (image) return format == AiKnowledgeParsedFormat.Image ? p.Image!.Parent is null
            : unified && p.Image!.Parent is not null;
        return format switch
        {
            AiKnowledgeParsedFormat.PlainText or AiKnowledgeParsedFormat.Markdown => kind == AiKnowledgeSourcePositionKind.Document,
            AiKnowledgeParsedFormat.Pdf => kind == AiKnowledgeSourcePositionKind.PdfPage,
            AiKnowledgeParsedFormat.Docx => docx,
            AiKnowledgeParsedFormat.Csv => kind == AiKnowledgeSourcePositionKind.CsvCell,
            AiKnowledgeParsedFormat.Excel => kind == AiKnowledgeSourcePositionKind.WorksheetCell,
            AiKnowledgeParsedFormat.Word => word,
            AiKnowledgeParsedFormat.Presentation => slide,
            AiKnowledgeParsedFormat.Html => html,
            AiKnowledgeParsedFormat.Image => image,
            _ => false
        };
    }

    private static bool ValidGrid(AiKnowledgeGridPosition g)
        => g.SheetNumber is >= 1 and <= 128 && g.SheetName is { Length: > 0 and <= 128 } && !g.SheetName.Any(char.IsControl)
            && AiKnowledgeParsedDocument.ValidText(g.SheetName, CancellationToken.None)
            && g.Row is >= 1 and <= 1048576 && g.Column is >= 1 and <= 16384
            && g.CellType is "text" or "number" or "boolean" or "date" or "error" or "blank" or "formula"
            && (g.CellType == "formula" ? g.Formula is { Length: > 0 and <= 8192 } && AiKnowledgeParsedDocument.ValidText(g.Formula, CancellationToken.None)
                && g.CacheState is "present-unverified" or "missing"
                : g.Formula is null && g.CacheState is null)
            && (g.MergedRange is null || ValidRange(g.MergedRange, g.Row, g.Column))
            && (g.RawValue is null || g.RawValue.Length <= 65536 && AiKnowledgeParsedDocument.ValidText(g.RawValue, CancellationToken.None))
            && (g.NumberFormat is null || g.NumberFormat.Length <= 256 && AiKnowledgeParsedDocument.ValidText(g.NumberFormat, CancellationToken.None))
            && (g.CellType == "formula" ? g.CacheState == "missing" ? g.RawValue is null && g.CachedValueType is null
                : g.RawValue is not null && g.CachedValueType is "text" or "number" or "boolean" or "date" or "error" or "blank"
                : g.CachedValueType is null);

    // 范围使用数值坐标，避免本地化列名、名称公式或超大字符串进入引用路径。
    private static bool ValidRange(string range, int row, int column)
    {
        var parts = range.Split(':');
        return parts.Length == 4 && int.TryParse(parts[0], out var r1) && int.TryParse(parts[1], out var c1)
            && int.TryParse(parts[2], out var r2) && int.TryParse(parts[3], out var c2)
            && r1 >= 1 && c1 >= 1 && r2 >= r1 && c2 >= c1 && r2 <= 1048576 && c2 <= 16384
            && row >= r1 && row <= r2 && column >= c1 && column <= c2;
    }

    private static bool ValidHtmlPath(string path)
    {
        if (path.Length is < 4 or > 1024 || path[0] != '/') return false;
        var segments = path[1..].Split('/');
        if (segments.Length > 32) return false;
        foreach (var segment in segments)
        {
            var bracket = segment.IndexOf('[');
            if (bracket is < 1 or > 32 || segment[^1] != ']' || !segment.AsSpan(0, bracket).ToString().All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
                || !int.TryParse(segment.AsSpan(bracket + 1, segment.Length - bracket - 2), out var ordinal) || ordinal is < 1 or > 65536) return false;
        }
        return true;
    }
}
