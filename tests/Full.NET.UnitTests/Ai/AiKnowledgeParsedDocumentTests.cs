using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>现有格式共同使用完整快照和真实来源位置；不调用模型、文件目录或数据库。</summary>
[TestClass]
public sealed class AiKnowledgeParsedDocumentTests
{
    private static readonly string Hash = new('A', 64);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Text_adapter_preserves_raw_digest_bom_and_utf16_offsets(bool markdown)
    {
        const string text = "# 标题😀\r\n\r\n中文\t正文\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var input = new MemoryStream(bytes);
        var read = await AiKnowledgeTextParser.ParseAsync(input, markdown ? "source.md" : "source.txt",
            markdown ? "text/markdown" : "text/plain", hash, default);
        Assert.IsTrue(read.IsSuccess);
        var parsed = AiKnowledgeParsedDocument.FromText(read.Document!, markdown ? AiKnowledgeTextFormat.Markdown : AiKnowledgeTextFormat.PlainText, default);
        Assert.IsNotNull(parsed); Assert.IsTrue(input.CanRead);
        Assert.AreEqual(markdown ? AiKnowledgeParsedFormat.Markdown : AiKnowledgeParsedFormat.PlainText, parsed.Format);
        Assert.AreEqual(hash.ToLowerInvariant(), parsed.ContentHash); Assert.AreEqual(bytes.Length, parsed.SizeBytes);
        Assert.AreEqual("utf8-text-v1", parsed.ParserVersion);
        var block = parsed.Blocks.Single(); Assert.AreEqual(text, block.Text); Assert.AreEqual("", block.Separator);
        var offset = text.IndexOf("中文", StringComparison.Ordinal);
        var location = block.Locate(offset, 2); Assert.IsNotNull(location);
        Assert.AreEqual(AiKnowledgeSourcePositionKind.Document, location.Kind);
        Assert.AreEqual(offset, location.Offset); Assert.AreEqual(2, location.Length);
        Assert.IsNull(location.PageNumber); Assert.IsNull(location.ParagraphNumber);
    }

    [TestMethod]
    public void Pdf_adapter_binds_all_pages_without_inventing_document_offsets_or_separators()
    {
        var parsed = AiKnowledgeParsedDocument.FromPdf(new(Hash, 97, [new(1, "第一页😀"), new(2, "second\r\npage")]), default);
        Assert.IsNotNull(parsed); Assert.AreEqual(AiKnowledgeParsedFormat.Pdf, parsed.Format);
        Assert.AreEqual(AiKnowledgeParsedPdf.ParserVersion, parsed.ParserVersion);
        Assert.AreEqual(Hash.ToLowerInvariant(), parsed.ContentHash); Assert.AreEqual(97, parsed.SizeBytes);
        Assert.AreEqual(2, parsed.Blocks.Count);
        for (var index = 0; index < parsed.Blocks.Count; index++)
        {
            var block = parsed.Blocks[index]; Assert.AreEqual(index, block.Sequence); Assert.AreEqual("", block.Separator);
            var location = block.Locate(0, block.Text.Length); Assert.IsNotNull(location);
            Assert.AreEqual(index + 1, location.PageNumber); Assert.AreEqual(0, location.Offset);
            Assert.AreEqual(AiKnowledgeSourcePositionKind.PdfPage, location.Kind); Assert.IsNull(location.BlockSequence);
        }
    }

    [TestMethod]
    public async Task Real_docx_adapter_preserves_table_cells_empty_blocks_headers_and_full_text()
    {
        var bytes = AiDocxParserTests.Package(AiDocxParserTests.Document("<w:p/><w:tbl><w:tr><w:trPr><w:tblHeader/></w:trPr>"
            + AiDocxParserTests.Cell("标题😀") + AiDocxParserTests.Cell("") + "</w:tr><w:tr>"
            + AiDocxParserTests.Cell("中文") + AiDocxParserTests.Cell("B") + "</w:tr></w:tbl><w:p><w:r><w:t>尾部</w:t></w:r></w:p>"));
        var wire = await AiDocxParserTests.Run(bytes); Assert.AreEqual(0, wire.Exit);
        using var stream = new MemoryStream(wire.Output);
        var read = await AiKnowledgeDocxOutputReader.ReadAsync(stream, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, default);
        Assert.IsTrue(read.IsSuccess, read.Failure.ToString());
        var parsed = AiKnowledgeParsedDocument.FromDocx(read.Document!, default); Assert.IsNotNull(parsed);
        Assert.AreEqual(AiKnowledgeParsedFormat.Docx, parsed.Format); Assert.AreEqual(AiKnowledgeParsedDocx.ParserVersion, parsed.ParserVersion);
        Assert.AreEqual(read.Document!.Text, string.Concat(parsed.Blocks.Select(block => block.Text + block.Separator)));
        Assert.AreEqual(read.Document.Blocks.Count, parsed.Blocks.Count);
        Assert.IsNull(parsed.Blocks[0].Locate(0, 1)); Assert.IsNull(parsed.Blocks[2].Locate(0, 1));
        var cell = parsed.Blocks[1]; Assert.AreEqual("\t", cell.Separator);
        var location = cell.Locate(2, 2); Assert.IsNotNull(location);
        Assert.AreEqual(AiKnowledgeSourcePositionKind.DocxTableCell, location.Kind);
        Assert.AreEqual(1, location.TableNumber); Assert.AreEqual(1, location.RowNumber); Assert.AreEqual(1, location.CellNumber);
        Assert.AreEqual(2, location.ParagraphNumber); Assert.AreEqual(1, location.BlockSequence);
        Assert.AreEqual(read.Document.Blocks[1].Offset + 2, location.Offset); Assert.IsTrue(location.IsHeader);
        Assert.IsNull(location.PageNumber);
        var tail = parsed.Blocks[^1].Locate(0, 2); Assert.IsNotNull(tail);
        Assert.AreEqual(AiKnowledgeSourcePositionKind.DocxParagraph, tail.Kind); Assert.IsNull(tail.TableNumber);
    }

    [TestMethod]
    [DataRow(-1, 1)]
    [DataRow(0, 0)]
    [DataRow(0, -1)]
    [DataRow(0, int.MaxValue)]
    [DataRow(int.MaxValue, 1)]
    [DataRow(1, 1)]
    [DataRow(2, 1)]
    [DataRow(3, 2)]
    public void Location_rejects_outside_empty_overflow_and_split_surrogate_intervals(int offset, int length)
    {
        var parsed = AiKnowledgeParsedDocument.FromPdf(new(Hash, 97, [new(1, "A😀B")]), default);
        Assert.IsNotNull(parsed); Assert.IsNull(parsed.Blocks[0].Locate(offset, length));
        var valid = parsed.Blocks[0].Locate(1, 2); Assert.IsNotNull(valid); Assert.AreEqual(2, valid.Length);
    }

    [TestMethod]
    public void Mutable_pdf_collection_is_captured_once_and_does_not_change_the_accepted_document()
    {
        var pages = new[] { new AiKnowledgePdfPage(1, "original"), new(2, "second") };
        var parsed = AiKnowledgeParsedDocument.FromPdf(new(Hash, 97, pages), default); Assert.IsNotNull(parsed);
        pages[0] = new(1, "replaced"); Assert.AreEqual("original", parsed.Blocks[0].Text);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<AiKnowledgeParsedBlock>)parsed.Blocks)[0] = parsed.Blocks[1]);
        var changing = new ChangingPages();
        parsed = AiKnowledgeParsedDocument.FromPdf(new(Hash, 97, changing), default); Assert.IsNotNull(parsed);
        Assert.AreEqual(1, changing.CountReads); Assert.AreEqual("original", parsed.Blocks[0].Text);
    }

    [TestMethod]
    public void Docx_snapshot_keeps_metadata_and_text_after_original_block_collection_changes()
    {
        var document = AiKnowledgeDocxPlanningTests.Document("first", "second");
        var blocks = document.Blocks.ToArray();
        var parsed = AiKnowledgeParsedDocument.FromDocx(document with { Blocks = blocks }, default); Assert.IsNotNull(parsed);
        blocks[0] = blocks[0] with { Offset = 500, ParagraphNumber = 900 };
        Assert.AreEqual(0, parsed.Blocks[0].Position.Offset); Assert.AreEqual(1, parsed.Blocks[0].Position.ParagraphNumber);
        Assert.AreEqual(0, parsed.Docx!.Blocks[0].Offset); Assert.AreEqual(1, parsed.Docx.Blocks[0].ParagraphNumber);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void Invalid_pdf_page_or_source_never_returns_partial_blocks(int scenario)
    {
        var source = new AiKnowledgeParsedPdf(Hash, 97, [new(1, "first"), new(2, "second")]);
        source = scenario switch
        {
            0 => source with { ContentHash = "invalid" },
            1 => source with { SizeBytes = 0 },
            2 => source with { Pages = [source.Pages[0], new(3, "second")] },
            3 => source with { Pages = [source.Pages[0], new(2, " ")] },
            4 => source with { Pages = [source.Pages[0], new(2, "\ud800")] },
            _ => source with { Pages = [source.Pages[0], new(2, "bad\0text")] }
        };
        Assert.IsNull(AiKnowledgeParsedDocument.FromPdf(source, default));
    }

    [TestMethod]
    public void Unsupported_text_sources_and_formats_are_not_relabelled_as_plain_text()
    {
        var text = new AiKnowledgeParsedText("valid", Hash, 97);
        Assert.IsNull(AiKnowledgeParsedDocument.FromText(text with { Source = AiKnowledgeTextSource.PdfPage }, AiKnowledgeTextFormat.PlainText, default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromText(text with { Source = (AiKnowledgeTextSource)99 }, AiKnowledgeTextFormat.PlainText, default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromText(text, (AiKnowledgeTextFormat)99, default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromText(text with { Text = "\ud800" }, AiKnowledgeTextFormat.PlainText, default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromText(text with { ContentHash = "" }, AiKnowledgeTextFormat.PlainText, default));
    }

    [TestMethod]
    public void Invalid_docx_geometry_is_rejected_before_any_source_mapping_is_returned()
    {
        var doc = AiKnowledgeDocxPlanningTests.Document("first", "second");
        Assert.IsNull(AiKnowledgeParsedDocument.FromDocx(doc with { Blocks = [doc.Blocks[0]] }, default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromDocx(doc with { Text = doc.Text + "unmapped" }, default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromDocx(doc with { Blocks = [doc.Blocks[0], doc.Blocks[1] with { Offset = 0 }] }, default));
    }

    [TestMethod]
    public void All_adapters_observe_cancellation_without_reading_collections()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var pages = new ChangingPages();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeParsedDocument.FromPdf(new(Hash, 97, pages), cancel.Token));
        Assert.AreEqual(0, pages.CountReads);
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeParsedDocument.FromText(new("valid", Hash, 97), AiKnowledgeTextFormat.PlainText, cancel.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeParsedDocument.FromDocx(AiKnowledgeDocxPlanningTests.Document("valid"), cancel.Token));
    }

    [TestMethod]
    public void Source_and_output_budgets_are_inclusive_and_independent()
    {
        var text = new AiKnowledgeParsedText(new('a', AiKnowledgeTextParser.MaxOutputCharacters), Hash, 8 * 1024 * 1024);
        Assert.IsNotNull(AiKnowledgeParsedDocument.FromText(text, AiKnowledgeTextFormat.PlainText, default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromText(text with { Text = text.Text + "a" }, AiKnowledgeTextFormat.PlainText, default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromText(text with { SizeBytes = text.SizeBytes + 1 }, AiKnowledgeTextFormat.PlainText, default));
        Assert.IsNotNull(AiKnowledgeParsedDocument.FromPdf(new(Hash, AiKnowledgePdfOutputReader.MaxSourceBytes,
            Enumerable.Range(1, AiKnowledgePdfOutputReader.MaxPages).Select(page => new AiKnowledgePdfPage(page, "text")).ToArray()), default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromPdf(new(Hash, 97,
            Enumerable.Range(1, AiKnowledgePdfOutputReader.MaxPages + 1).Select(page => new AiKnowledgePdfPage(page, "text")).ToArray()), default));
        Assert.IsNull(AiKnowledgeParsedDocument.FromPdf(new(Hash, 97, [new(1, text.Text), new(2, "extra")]), default));
    }

    private sealed class ChangingPages : IReadOnlyList<AiKnowledgePdfPage>
    {
        internal int CountReads;
        public int Count => ++CountReads == 1 ? 1 : int.MaxValue;
        public AiKnowledgePdfPage this[int index] => index == 0 ? new(1, "original") : throw new AssertFailedException("不能读取首次快照之外的页。");
        public IEnumerator<AiKnowledgePdfPage> GetEnumerator() => throw new AssertFailedException("不能枚举可变来源集合。");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
