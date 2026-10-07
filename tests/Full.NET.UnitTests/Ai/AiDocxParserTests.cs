using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Ai.DocxParser;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实 OOXML 包验证正文、单元格位置与固定进程协议；不调用 Office 或网络。</summary>
[TestClass]
public sealed class AiDocxParserTests
{
    internal const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal const string ContentTypes = "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>";
    internal const string Relationships = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";

    [TestMethod]
    public async Task Real_docx_preserves_paragraphs_table_coordinates_and_raw_source_digest()
    {
        var bytes = Package(Document("<w:p><w:r><w:t>标题😀</w:t></w:r></w:p><w:tbl><w:tr><w:trPr><w:tblHeader/></w:trPr>" + Cell("A") + Cell("B") + "</w:tr><w:tr>" + Cell("C") + Cell("D") + "</w:tr></w:tbl><w:p><w:r><w:t>正文</w:t></w:r></w:p>"));
        var result = await Run(bytes);
        Assert.AreEqual(0, result.Exit, result.Errors);
        Assert.AreEqual("", result.Errors);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.AreEqual(1, root.GetProperty("protocolVersion").GetInt32());
        Assert.AreEqual("docx-ooxml-body-v1", root.GetProperty("parserVersion").GetString());
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(bytes)), root.GetProperty("contentHash").GetString());
        Assert.AreEqual(bytes.Length, root.GetProperty("sizeBytes").GetInt32());
        var text = root.GetProperty("text").GetString()!;
        Assert.AreEqual("标题😀\nA\tB\nC\tD\n正文\n", text);
        var blocks = root.GetProperty("blocks").EnumerateArray().ToArray();
        Assert.AreEqual(6, blocks.Length);
        for (var index = 0; index < blocks.Length; index++) Assert.AreEqual(index, blocks[index].GetProperty("sequence").GetInt32());
        Assert.AreEqual("标题😀", text.Substring(blocks[0].GetProperty("offset").GetInt32(), blocks[0].GetProperty("length").GetInt32()));
        Assert.AreEqual("tableCell", blocks[2].GetProperty("kind").GetString());
        Assert.AreEqual(1, blocks[2].GetProperty("tableNumber").GetInt32());
        Assert.AreEqual(1, blocks[2].GetProperty("rowNumber").GetInt32());
        Assert.AreEqual(2, blocks[2].GetProperty("cellNumber").GetInt32());
        Assert.AreEqual(3, blocks[2].GetProperty("paragraphNumber").GetInt32());
        Assert.IsTrue(blocks[2].GetProperty("isHeader").GetBoolean());
        Assert.IsFalse(blocks[3].GetProperty("isHeader").GetBoolean());
    }

    [TestMethod]
    public async Task Runs_tabs_breaks_and_multiline_cells_keep_visible_text_without_following_links()
    {
        var bytes = Package(Document("<w:p><w:pPr/><w:hyperlink r:id=\"external\"><w:r><w:rPr/><w:t xml:space=\"preserve\"> raw </w:t><w:tab/><w:t>e&#x301;😀</w:t><w:br/><w:t>&lt;script&gt;</w:t></w:r></w:hyperlink></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>one</w:t></w:r></w:p><w:p><w:r><w:t>two</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"));
        var result = await Run(bytes); Assert.AreEqual(0, result.Exit, result.Errors);
        using var json = JsonDocument.Parse(result.Output); var root = json.RootElement;
        Assert.AreEqual(" raw \te\u0301😀\n<script>\none\ntwo\n", root.GetProperty("text").GetString());
        Assert.AreEqual(2, root.GetProperty("blocks")[1].GetProperty("cellParagraphCount").GetInt32());
    }

    internal static string Cell(string text) => "<w:tc><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:tc>";
    internal static string Document(string body) => $"<w:document xmlns:w=\"{WordNamespace}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>{body}<w:sectPr/></w:body></w:document>";
    internal static byte[] Package(string document, string? types = null, string? relationships = null, params (string Name, string Body)[] extras)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var (name, body) in new[] { ("[Content_Types].xml", types ?? ContentTypes), ("_rels/.rels", relationships ?? Relationships), ("word/document.xml", document) }.Concat(extras))
            {
                using var entry = zip.CreateEntry(name).Open(); entry.Write(Encoding.UTF8.GetBytes(body));
            }
        }
        return output.ToArray();
    }
    internal static async Task<(int Exit, byte[] Output, string Errors)> Run(byte[] source)
    {
        using var input = new MemoryStream(source); using var output = new MemoryStream(); using var errors = new StringWriter();
        var exit = await DocxParser.RunAsync(input, output, errors);
        Assert.IsTrue(input.CanRead); Assert.IsTrue(output.CanWrite);
        return (exit, output.ToArray(), errors.ToString());
    }
}
