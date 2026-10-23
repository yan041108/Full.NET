using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>只投影可公开的坐标；完整解析身份和原文仍由不可变清单证明。</summary>
internal static class AiKnowledgeSourceLocations
{
    private static readonly string[] Kinds = ["document", "pdf_page", "docx_paragraph", "docx_table_cell", "csv_cell",
        "worksheet_cell", "word_paragraph", "word_table_cell", "slide_text", "slide_notes", "html_node", "image_region"];
    internal static AiKnowledgeSourceLocationResponse Create(int block, AiKnowledgeSourcePosition p, AiKnowledgeParsedDocument? document = null) => new(block, Kinds[(int)p.Kind],
        p.PageNumber, p.ParagraphNumber, p.TableNumber, p.RowNumber, p.CellNumber,
        p.Grid is { } g ? new(g.SheetNumber, g.SheetName, g.Row, g.Column) : null,
        p.Slide is { } s ? new(s.SlideNumber, s.ShapeNumber, s.RowNumber, s.ColumnNumber, s.RowSpan, s.ColumnSpan) : null,
        p.HtmlPath, p.Image is { } i ? new(i.FrameNumber, i.ImageWidth, i.ImageHeight, i.X, i.Y, i.Width, i.Height, i.Confidence,
            i.TableNumber, i.RowNumber, i.ColumnNumber, i.Parent is { } parent ? Create(document?.Blocks.First(b => b.Position with { Length = 0 } == parent).Sequence ?? parent.BlockSequence ?? block, parent) : null) : null);

    internal static bool Valid(AiKnowledgeSourceLocationResponse? value, bool parent = false)
    {
        if (value is null) return true;
        var kind = Array.IndexOf(Kinds, value.Kind);
        if (value.BlockSequence is < 0 or >= 8192 || kind < 0 || parent && kind == 11) return false;
        var paragraph = kind is 2 or 3 or 6 or 7; var table = kind is 3 or 7;
        if ((value.PageNumber is not null) != (kind == 1) || value.PageNumber is < 1 or > 128
            || (value.ParagraphNumber is not null) != paragraph || value.ParagraphNumber is < 1 or > 8192
            || (value.TableNumber is not null) != table || (value.RowNumber is not null) != table || (value.CellNumber is not null) != table
            || value.TableNumber is < 1 or > 128 || value.RowNumber is < 1 or > 4096 || value.CellNumber is < 1 or > 64
            || (value.Grid is not null) != (kind is 4 or 5) || (value.Slide is not null) != (kind is 8 or 9)
            || (value.HtmlPath is not null) != (kind == 10) || (value.Image is not null) != (kind == 11)) return false;
        if (value.Grid is { } g && (g.SheetNumber is < 1 or > 128 || g.SheetName is not { Length: > 0 and <= 128 }
            || g.SheetName.Any(char.IsControl) || !AiKnowledgeParsedDocument.ValidText(g.SheetName, CancellationToken.None)
            || g.Row is < 1 or > 1048576 || g.Column is < 1 or > 16384 || kind == 4 && (g.SheetNumber != 1 || g.SheetName != "CSV"))) return false;
        if (value.Slide is { } s && (s.SlideNumber is < 1 or > 128 || s.ShapeNumber is < 1 or > 8192
            || (s.RowNumber is null ? s.ColumnNumber is not null || s.RowSpan != 1 || s.ColumnSpan != 1
                : s.RowNumber is < 1 or > 4096 || s.ColumnNumber is not (>= 1 and <= 64) || s.RowSpan is < 1 or > 4096
                    || s.ColumnSpan is < 1 or > 64 || s.RowNumber > 4097 - s.RowSpan || s.ColumnNumber > 65 - s.ColumnSpan))) return false;
        if (value.HtmlPath is { } path && (path.Length is < 1 or > 1024 || !path.StartsWith("/", StringComparison.Ordinal)
            || path.Any(char.IsControl) || !AiKnowledgeParsedDocument.ValidText(path, CancellationToken.None))) return false;
        return value.Image is not { } i || i.FrameNumber is >= 1 and <= 128 && i.ImageWidth is >= 1 and <= 16384
            && i.ImageHeight is >= 1 and <= 16384 && (long)i.ImageWidth * i.ImageHeight <= 40000000
            && i.X >= 0 && i.Y >= 0 && i.Width > 0 && i.Height > 0 && i.X <= i.ImageWidth - i.Width && i.Y <= i.ImageHeight - i.Height
            && double.IsFinite(i.Confidence) && i.Confidence is >= 0.8 and <= 1 && Valid(i.Parent, true)
            && (i.TableNumber is null ? i.RowNumber is null && i.ColumnNumber is null
                : i.TableNumber is >= 1 and <= 128 && i.RowNumber is >= 1 and <= 4096 && i.ColumnNumber is >= 1 and <= 64);
    }
}
