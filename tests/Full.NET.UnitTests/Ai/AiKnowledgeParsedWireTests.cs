using System.Text;
using System.Text.Json.Nodes;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>共享进程协议拒绝部分、混合坐标和未经绑定的来源；不把解析成功当作索引完成。</summary>
[TestClass]
public sealed class AiKnowledgeParsedWireTests
{
    private static readonly string Hash = new('a', 64);

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    [DataRow(9)]
    public void Every_format_roundtrips_exact_utf16_and_source_coordinates(int formatNumber)
    {
        var format = (AiKnowledgeParsedFormat)formatNumber;
        var source = Fixture(format);
        var bytes = AiKnowledgeParsedWireCodec.Encode(source, default);
        var restored = AiKnowledgeParsedWireCodec.Decode(bytes, format, Hash, 17, default);
        Assert.IsNotNull(restored); Assert.AreEqual(source.Format, restored.Format);
        CollectionAssert.AreEqual(source.Blocks.ToArray(), restored.Blocks.ToArray());
        Assert.IsNull(restored.Blocks[0].Locate(2, 1));
        Assert.AreEqual(source.Blocks[0].Position with { Offset = 3, Length = 2 }, restored.Blocks[0].Locate(3, 2));
    }

    [TestMethod]
    [DataRow("root-unknown")]
    [DataRow("root-duplicate")]
    [DataRow("position-duplicate")]
    [DataRow("missing-field")]
    [DataRow("wrong-format")]
    [DataRow("wrong-parser")]
    [DataRow("wrong-hash")]
    [DataRow("wrong-size")]
    [DataRow("partial")]
    [DataRow("extra-coordinate")]
    [DataRow("unknown-kind")]
    [DataRow("bad-sequence")]
    [DataRow("bad-length")]
    [DataRow("missing-grid")]
    [DataRow("invalid-grid")]
    [DataRow("empty")]
    public void Rejects_entire_wire_without_returning_partial_blocks(string corruption)
    {
        var original = AiKnowledgeParsedWireCodec.Encode(Fixture(AiKnowledgeParsedFormat.Csv), default);
        var json = JsonNode.Parse(original)!.AsObject(); var block = json["blocks"]![0]!; var position = block["position"]!;
        switch (corruption)
        {
            case "root-unknown": json["unknown"] = 1; break;
            case "missing-field": position.AsObject().Remove("slide"); break;
            case "wrong-format": json["format"] = 6; break;
            case "wrong-parser": json["parserVersion"] = "unapproved"; break;
            case "wrong-hash": json["contentHash"] = new string('b', 64); break;
            case "wrong-size": json["sizeBytes"] = 18; break;
            case "extra-coordinate": position["pageNumber"] = 1; break;
            case "unknown-kind": position["kind"] = 99; break;
            case "bad-sequence": block["sequence"] = 1; break;
            case "bad-length": position["length"] = 1; break;
            case "missing-grid": position["grid"] = null; break;
            case "invalid-grid": position["grid"]!["column"] = 0; break;
            case "empty": block["text"] = ""; position["length"] = 0; break;
        }
        var text = json.ToJsonString();
        text = corruption switch
        {
            "root-duplicate" => text.Insert(1, "\"format\":4,"),
            "position-duplicate" => text.Replace("\"kind\":", "\"kind\":4,\"kind\":", StringComparison.Ordinal),
            "partial" => text[..^1], _ => text
        };
        Assert.IsNull(AiKnowledgeParsedWireCodec.Decode(Encoding.UTF8.GetBytes(text), AiKnowledgeParsedFormat.Csv, Hash, 17, default));
    }

    [TestMethod]
    public void Captures_blocks_once_and_keeps_caller_mutation_out_of_accepted_snapshot()
    {
        var original = Fixture(AiKnowledgeParsedFormat.Excel);
        var blocks = original.Blocks.ToArray();
        var copy = AiKnowledgeParsedDocument.FromBlocks(original.Format, original.ParserVersion, Hash, 17, blocks, default);
        blocks[0] = blocks[0] with { Text = "修改" };
        Assert.AreEqual("中😀正文", copy!.Blocks[0].Text);
    }

    [TestMethod]
    public void Rejects_nonfinite_or_outside_image_box_and_mixed_coordinates()
    {
        var source = Fixture(AiKnowledgeParsedFormat.Image); var block = source.Blocks[0]; var p = block.Position;
        foreach (var image in new[] { p.Image! with { Confidence = double.NaN }, p.Image! with { X = 95 }, p.Image! with { Width = 0 }, p.Image! with { FrameNumber = 129 } })
            Assert.IsNull(AiKnowledgeParsedDocument.FromBlocks(source.Format, source.ParserVersion, Hash, 17,
                [block with { Position = p with { Image = image } }], default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromBlocks(source.Format, source.ParserVersion, Hash, 17,
            [block with { Position = p with { Grid = new(1, "CSV", 1, 1, "text") } }], default));
    }

    [TestMethod]
    public async Task Cancellation_is_observed_before_source_or_stream_access()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => AiKnowledgeParsedWireCodec.ReadAsync(Stream.Null,
            AiKnowledgeParsedFormat.Csv, Hash, 17, cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeParsedWireCodec.Decode(ReadOnlyMemory<byte>.Empty,
            AiKnowledgeParsedFormat.Csv, Hash, 17, cancellation.Token));
    }

    internal static AiKnowledgeParsedDocument Reidentify(AiKnowledgeParsedDocument source, string? hash = null, int? size = null)
        => AiKnowledgeParsedDocument.FromBlocks(source.Format, source.ParserVersion, hash ?? source.ContentHash,
            size ?? source.SizeBytes, source.Blocks, default, source.Policy)!;

    internal static AiKnowledgeParsedDocument Fixture(AiKnowledgeParsedFormat format)
    {
        const string text = "中😀正文";
        var kind = format switch
        {
            AiKnowledgeParsedFormat.PlainText or AiKnowledgeParsedFormat.Markdown => AiKnowledgeSourcePositionKind.Document,
            AiKnowledgeParsedFormat.Pdf => AiKnowledgeSourcePositionKind.PdfPage,
            AiKnowledgeParsedFormat.Docx => AiKnowledgeSourcePositionKind.DocxParagraph,
            AiKnowledgeParsedFormat.Csv => AiKnowledgeSourcePositionKind.CsvCell,
            AiKnowledgeParsedFormat.Html => AiKnowledgeSourcePositionKind.HtmlNode,
            AiKnowledgeParsedFormat.Excel => AiKnowledgeSourcePositionKind.WorksheetCell,
            AiKnowledgeParsedFormat.Word => AiKnowledgeSourcePositionKind.WordParagraph,
            AiKnowledgeParsedFormat.Presentation => AiKnowledgeSourcePositionKind.SlideNotes,
            _ => AiKnowledgeSourcePositionKind.ImageRegion
        };
        var word = format is AiKnowledgeParsedFormat.Docx or AiKnowledgeParsedFormat.Word;
        var p = new AiKnowledgeSourcePosition(kind, 0, text.Length, PageNumber: format == AiKnowledgeParsedFormat.Pdf ? 1 : null,
            BlockSequence: word ? 0 : null, ParagraphNumber: word ? 1 : null,
            Grid: format is AiKnowledgeParsedFormat.Csv or AiKnowledgeParsedFormat.Excel ? new(1, format == AiKnowledgeParsedFormat.Csv ? "CSV" : "工作表", 1, 1, "text") : null,
            Slide: format == AiKnowledgeParsedFormat.Presentation ? new(1, 1) : null,
            HtmlPath: format == AiKnowledgeParsedFormat.Html ? "/html[1]/body[1]/p[1]" : null,
            Image: format == AiKnowledgeParsedFormat.Image ? new(1, 100, 100, 1, 2, 90, 80, 0.95, "test-recognizer-sha256", 100, 100, 1, Hash, "image-rgb-exif-white-v1") : null);
        var parser = format switch { AiKnowledgeParsedFormat.PlainText or AiKnowledgeParsedFormat.Markdown => "utf8-text-v1",
            AiKnowledgeParsedFormat.Pdf => AiKnowledgeParsedPdf.ParserVersion, AiKnowledgeParsedFormat.Docx => AiKnowledgeParsedDocx.ParserVersion,
            _ => AiKnowledgeParsedStructure.MultiFormatParserVersion };
        return AiKnowledgeParsedDocument.FromBlocks(format, parser, Hash, 17,
            [new(0, text, word ? "\n" : "", p)], default)!;
    }
}
