using System.Text;
using Full.NET.Ai.DocxParser;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实压缩包及 XML 攻击夹具验证拒绝语义，失败不能输出私有正文。</summary>
[TestClass]
public sealed class AiDocxParserBoundaryTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("private text")]
    [DataRow("PK broken private payload")]
    public async Task Nonzip_and_corrupt_sources_return_fixed_error_without_body(string source)
        => Failed(await AiDocxParserTests.Run(Encoding.UTF8.GetBytes(source)), "fn_ai_docx_invalid_package");

    [TestMethod]
    [DataRow("")]
    [DataRow("<w:p/>")]
    [DataRow("<w:p><w:r><w:t> &#x9;&#xA; </w:t></w:r></w:p>")]
    public async Task Empty_body_never_becomes_successful_index_input(string body)
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document(body))), "fn_ai_docx_empty_document");

    [TestMethod]
    [DataRow("../escape.xml")]
    [DataRow("/absolute.xml")]
    [DataRow("word\\other.xml")]
    [DataRow("word//other.xml")]
    [DataRow("word/./other.xml")]
    [DataRow("C:secret.xml")]
    [DataRow("word/document.xml")]
    [DataRow("WORD/DOCUMENT.XML")]
    public async Task Unsafe_or_ambiguous_archive_names_are_rejected_without_extraction(string name)
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), extras: [(name, "private")])), "fn_ai_docx_invalid_package");

    [TestMethod]
    [DataRow("word/vbaProject.bin")]
    [DataRow("word/embeddings/object.bin")]
    [DataRow("word/header1.xml")]
    [DataRow("word/footer1.xml")]
    [DataRow("word/footnotes.xml")]
    [DataRow("word/endnotes.xml")]
    public async Task Unsupported_visible_parts_or_active_payloads_fail_closed(string name)
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), extras: [(name, "private")])), "fn_ai_docx_unsupported_structure");

    [TestMethod]
    [DataRow("<w:p><w:fldSimple w:instr=\"INCLUDETEXT external\"/></w:p>")]
    [DataRow("<w:p><w:r><w:instrText>external</w:instrText></w:r></w:p>")]
    [DataRow("<w:p><w:r><w:drawing/></w:r></w:p>")]
    [DataRow("<w:p><w:ins><w:r><w:t>revision</w:t></w:r></w:ins></w:p>")]
    [DataRow("<w:sdt><w:sdtContent><w:p/></w:sdtContent></w:sdt>")]
    [DataRow("<w:altChunk/>")]
    [DataRow("<w:p><w:r><w:object/></w:r></w:p>")]
    [DataRow("<w:tbl><w:tr><w:tc><w:tbl/></w:tc></w:tr></w:tbl>")]
    [DataRow("<w:tbl><w:tr><w:tc><w:tcPr><w:gridSpan w:val=\"2\"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>")]
    [DataRow("<w:tbl><w:tr><w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>")]
    public async Task Unsupported_body_content_cannot_be_silently_dropped(string body)
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document(body))), "fn_ai_docx_unsupported_structure");

    [TestMethod]
    [DataRow("<w:p><w:pPr><w:pPrChange><w:pPr/></w:pPrChange></w:pPr><w:r><w:t>revision</w:t></w:r></w:p>")]
    [DataRow("<w:p><w:r><w:rPr><w:rPrChange><w:rPr/></w:rPrChange></w:rPr><w:t>revision</w:t></w:r></w:p>")]
    [DataRow("<w:p><w:r><w:rPr><w:vanish/></w:rPr><w:t>hidden</w:t></w:r></w:p>")]
    [DataRow("<w:p><w:pPr><w:numPr/></w:pPr><w:r><w:t>implicit numbering</w:t></w:r></w:p>")]
    [DataRow("<w:sectPr><w:headerReference r:id=\"customHeader\"/></w:sectPr>")]
    [DataRow("<w:sectPr><w:footerReference r:id=\"customFooter\"/></w:sectPr>")]
    [DataRow("<w:tbl><w:tr><w:trPr><w:gridBefore w:val=\"1\"/></w:trPr><w:tc><w:p/></w:tc></w:tr></w:tbl>")]
    [DataRow("<w:tbl><w:tr><w:trPr><w:gridAfter w:val=\"1\"/></w:trPr><w:tc><w:p/></w:tc></w:tr></w:tbl>")]
    public async Task Property_subtrees_cannot_hide_revisions_parts_or_shift_table_columns(string body)
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document(body))), "fn_ai_docx_unsupported_structure");

    [TestMethod]
    [DataRow("<w:trPr><w:tblHeader/></w:trPr><w:trPr><w:tblHeader w:val=\"0\"/></w:trPr>")]
    [DataRow("<w:trPr><w:tblHeader/><w:tblHeader w:val=\"0\"/></w:trPr>")]
    public async Task Ambiguous_table_header_properties_are_rejected(string properties)
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document("<w:tbl><w:tr>" + properties + AiDocxParserTests.Cell("header") + "</w:tr></w:tbl>"))), "fn_ai_docx_unsupported_structure");

    [TestMethod]
    [DataRow("macro")]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("wrong-namespace")]
    public async Task Main_document_content_type_must_be_unique_and_exact(string scenario)
    {
        var types = scenario switch
        {
            "macro" => AiDocxParserTests.ContentTypes.Replace("wordprocessingml.document.main+xml", "ms-word.document.macroEnabled.main+xml"),
            "missing" => AiDocxParserTests.ContentTypes.Replace("/word/document.xml", "/word/other.xml"),
            "duplicate" => AiDocxParserTests.ContentTypes.Replace("</Types>", AiDocxParserTests.ContentTypes[(AiDocxParserTests.ContentTypes.IndexOf("<Override", StringComparison.Ordinal))..].Replace("</Types>", "") + "</Types>"),
            _ => AiDocxParserTests.ContentTypes.Replace("http://schemas.openxmlformats.org/package/2006/content-types", "urn:wrong"),
        };
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), types)), "fn_ai_docx_invalid_package");
    }

    [TestMethod]
    [DataRow("external")]
    [DataRow("wrong-target")]
    [DataRow("duplicate")]
    public async Task Main_document_relationship_cannot_redirect_or_be_ambiguous(string scenario)
    {
        var relationships = scenario switch
        {
            "external" => AiDocxParserTests.Relationships.Replace("Target=", "TargetMode=\"External\" Target="),
            "wrong-target" => AiDocxParserTests.Relationships.Replace("word/document.xml", "../secret.xml"),
            _ => AiDocxParserTests.Relationships.Replace("</Relationships>", AiDocxParserTests.Relationships[(AiDocxParserTests.Relationships.IndexOf("<Relationship Id", StringComparison.Ordinal))..].Replace("</Relationships>", "") + "</Relationships>"),
        };
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), relationships: relationships)), "fn_ai_docx_invalid_package");
    }

    [TestMethod]
    [DataRow("<!DOCTYPE w:document [<!ENTITY external SYSTEM 'file:///private'>]>")]
    [DataRow("<!DOCTYPE w:document [<!ENTITY a 'private'>]>")]
    public async Task Dtd_and_entities_are_prohibited_before_reading_document_text(string declaration)
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(declaration + Body())), "fn_ai_docx_invalid_xml");

    [TestMethod]
    [DataRow("<w:document>")]
    [DataRow("<document xmlns='urn:wrong'/>")]
    [DataRow("<w:p xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'/>")]
    public async Task Malformed_or_wrong_root_never_produces_partial_text(string document)
    {
        var result = await AiDocxParserTests.Run(AiDocxParserTests.Package(document));
        Assert.AreEqual(2, result.Exit); Assert.AreEqual(0, result.Output.Length);
        Assert.IsTrue(result.Errors is "fn_ai_docx_invalid_xml\r\n" or "fn_ai_docx_invalid_xml\n" or "fn_ai_docx_invalid_package\r\n" or "fn_ai_docx_invalid_package\n");
    }

    [TestMethod]
    public async Task Actual_input_and_decompressed_entry_limits_are_enforced()
    {
        Failed(await AiDocxParserTests.Run(new byte[5 * 1024 * 1024 + 1]), "fn_ai_docx_input_limit");
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), extras: [("word/unused.bin", new string('a', 8 * 1024 * 1024 + 1))])), "fn_ai_docx_entry_limit");
    }

    [TestMethod]
    public async Task Total_inflated_budget_includes_unconsumed_parts()
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), extras: [("word/a.bin", new string('a', 6 * 1024 * 1024)), ("word/b.bin", new string('b', 6 * 1024 * 1024)), ("word/c.bin", new string('c', 6 * 1024 * 1024))])), "fn_ai_docx_inflated_limit");

    [TestMethod]
    public async Task Depth_node_entry_and_paragraph_limits_fail_without_truncation()
    {
        var nested = string.Concat(Enumerable.Repeat("<w:r>", 40)) + "private" + string.Concat(Enumerable.Repeat("</w:r>", 40));
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document(nested))), "fn_ai_docx_xml_depth_limit");
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document("<w:p>" + string.Concat(Enumerable.Repeat("<w:r/>", 65537)) + "</w:p>"))), "fn_ai_docx_xml_node_limit");
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), extras: Enumerable.Range(0, 254).Select(i => ($"word/extra{i}.xml", "x")).ToArray())), "fn_ai_docx_entry_count_limit");
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document(string.Concat(Enumerable.Repeat("<w:p/>", 8193))))), "fn_ai_docx_structure_limit");
    }

    [TestMethod]
    public async Task Output_character_budget_rejects_instead_of_truncating()
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document("<w:p><w:r><w:t>" + new string('a', 2 * 1024 * 1024) + "</w:t></w:r></w:p>"))), "fn_ai_docx_output_limit");

    [TestMethod]
    public async Task Xml_attributes_are_included_in_node_budget_before_dom_allocation()
    {
        var attributes = string.Join(' ', Enumerable.Range(0, 70000).Select(i => $"a{i}=\"x\""));
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document("<w:p " + attributes + "><w:r><w:t>private</w:t></w:r></w:p>"))), "fn_ai_docx_xml_node_limit");
    }

    [TestMethod]
    [DataRow("cellIns")]
    [DataRow("cellDel")]
    [DataRow("cellMerge")]
    public async Task Cell_property_revisions_are_rejected(string revision)
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(AiDocxParserTests.Document("<w:tbl><w:tr><w:tc><w:tcPr><w:" + revision + "/></w:tcPr><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"))), "fn_ai_docx_unsupported_structure");

    [TestMethod]
    [DataRow("word/styles.xml", "<w:styles xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:docDefaults><w:rPrDefault><w:rPr><w:vanish/></w:rPr></w:rPrDefault></w:docDefaults></w:styles>")]
    [DataRow("word/customStyles.xml", "<w:styles xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:style><w:pPr><w:numPr/></w:pPr></w:style></w:styles>")]
    [DataRow("word/h1.xml", "<w:hdr xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:p><w:r><w:t>private header</w:t></w:r></w:p></w:hdr>")]
    public async Task Other_xml_parts_cannot_hide_visible_content_or_unsupported_inherited_styles(string name, string xml)
        => Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), extras: [(name, xml)])), "fn_ai_docx_unsupported_structure");

    [TestMethod]
    public async Task Renamed_active_payload_is_rejected_by_content_type()
    {
        var types = AiDocxParserTests.ContentTypes.Replace("</Types>", "<Override PartName=\"/word/custom.bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/></Types>");
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), types, extras: [("word/custom.bin", "macro")])), "fn_ai_docx_unsupported_structure");
    }

    [TestMethod]
    [DataRow("word/style.bin")]
    [DataRow("word/STYLE.BIN")]
    public async Task Styles_with_a_nonxml_file_name_are_inspected_by_content_type(string name)
    {
        var types = AiDocxParserTests.ContentTypes.Replace("</Types>", "<Override PartName=\"/word/style.bin\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
        var styles = "<w:styles xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:docDefaults><w:rPrDefault><w:rPr><w:vanish/></w:rPr></w:rPrDefault></w:docDefaults></w:styles>";
        Failed(await AiDocxParserTests.Run(AiDocxParserTests.Package(Body(), types, extras: [(name, styles)])), "fn_ai_docx_unsupported_structure");
    }

    [TestMethod]
    public async Task Cancellation_and_input_io_failures_preserve_caller_stream_ownership()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var input = new MemoryStream(AiDocxParserTests.Package(Body())); using var output = new MemoryStream(); using var errors = new StringWriter();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => DocxParser.RunAsync(input, output, errors, cancelled.Token));
        Assert.IsTrue(input.CanRead); Assert.AreEqual(0, errors.ToString().Length); Assert.AreEqual(0L, output.Length);
        using var broken = new BrokenStream();
        Assert.AreEqual(2, await DocxParser.RunAsync(broken, output, errors));
        Assert.AreEqual("fn_ai_docx_parse_failed", errors.ToString().Trim()); Assert.AreEqual(0L, output.Length);
    }

    private static string Body() => AiDocxParserTests.Document("<w:p><w:r><w:t>private source</w:t></w:r></w:p>");
    private static void Failed((int Exit, byte[] Output, string Errors) result, string code)
    { Assert.AreEqual(2, result.Exit); Assert.AreEqual(0, result.Output.Length); Assert.AreEqual(code, result.Errors.Trim()); }
    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
            => ValueTask.FromException<int>(new IOException("private failure"));
    }
}
