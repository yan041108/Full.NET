using Full.NET.Modules.Auditing.Features.ExportHostAuditLogs;
using System.IO.Compression;
using System.Xml.Linq;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditLogWorkbookCodecTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Export_preserves_unicode_and_removes_only_invalid_xml_text(bool malformed)
    {
        // 孤立代理项在方法体构造，避免特性元数据编码替换待测字符。
        var input = malformed ? " 前\0😀\uD800𠮷\uDC00后 " : " 中文 😀 𠮷 \t";
        var expected = malformed ? " 前😀𠮷后 " : input;
        using var stream = new MemoryStream(AuditLogWorkbookCodec.Export("Audit", [input], [[input]]));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cells = XDocument.Load(sheet).Descendants(ns + "t").ToArray();
        CollectionAssert.AreEqual(new[] { expected, expected }, cells.Select(cell => cell.Value).ToArray());
        Assert.IsTrue(cells.All(cell => (string?)cell.Attribute(XNamespace.Xml + "space") == "preserve"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sheet_name_truncation_does_not_split_a_surrogate_pair(bool splitAtBoundary)
    {
        var prefix = new string('x', splitAtBoundary ? 30 : 29);
        var actual = ReadSheetName(AuditLogWorkbookCodec.Export(prefix + "😀tail", ["value"], []));
        Assert.AreEqual(splitAtBoundary ? prefix : prefix + "😀", actual);
        Assert.IsTrue(actual.Length <= 31);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Sheet_name_removed_to_empty_uses_a_valid_fallback(bool malformed)
    {
        var source = malformed ? ":[]\0\uD800" : "  :[]  ";
        Assert.AreEqual("Export", ReadSheetName(AuditLogWorkbookCodec.Export(source, ["value"], [])));
    }

    private static string ReadSheetName(byte[] workbook)
    {
        using var stream = new MemoryStream(workbook);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = archive.GetEntry("xl/workbook.xml")!.Open();
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return (string)XDocument.Load(entry).Descendants(ns + "sheet").Single().Attribute("name")!;
    }

    [TestMethod]
    public void EscapeFormulaText_prefixes_formula_like_values()
    {
        Assert.AreEqual("'=1+1", AuditLogWorkbookCodec.EscapeFormulaText("=1+1"));
    }

    [TestMethod]
    public void Export_returns_non_empty_workbook_bytes()
    {
        var bytes = AuditLogWorkbookCodec.Export(
            "AccessLogs",
            ["value"],
            [["sample"]]);

        Assert.IsTrue(bytes.Length > 0);
    }
}
