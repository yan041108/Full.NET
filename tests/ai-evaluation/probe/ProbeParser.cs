using System.Globalization;
using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Full.NET.AiRetrieval.Probe;

/// <summary>受控合成解析实验；单页解压与解析的硬时限、内存必须由外部进程边界限制。</summary>
internal static class ProbeParser
{
    internal static void Run(List<ProbeObservation> observations)
    {
        var text = "合成制度 FN-DEMO-042：三个工作日。";
        var pdf = CreatePdf(text, 2);
        observations.Add(new("text-pdf-chinese", Parse(pdf, ".pdf").Contains(text, StringComparison.Ordinal), "ToUnicode Chinese text extracted"));
        observations.Add(new("page-location", Parse(pdf, ".pdf").Contains("[page:2]", StringComparison.Ordinal), "one-based page positions retained"));
        observations.Add(new("compressed-text-pdf", Parse(CreatePdf(text, 1, compressed: true), ".pdf").Contains(text, StringComparison.Ordinal), "Flate-compressed Chinese text extracted"));
        observations.Add(new("markdown", Parse(Encoding.UTF8.GetBytes("# 合成手册\n|套餐|上限|\n|专业|80|"), ".md").Contains("|专业|80|", StringComparison.Ordinal), "headings/table kept as text"));
        observations.Add(new("utf8-text", Parse(Encoding.UTF8.GetBytes(text), ".txt") == text, "Chinese UTF-8 retained"));
        Reject("unsupported-docx", () => Parse([1, 2, 3], ".docx"));
        Reject("scan-or-empty-pdf", () => Parse(CreatePdf("", 1), ".pdf"));
        Reject("invalid-utf8", () => Parse([0xff], ".txt"));
        Reject("file-size-limit", () => Parse(new byte[1025], ".txt", maxBytes: 1024));
        Reject("page-count-limit", () => Parse(pdf, ".pdf", maxPages: 1));
        Reject("output-limit", () => Parse(pdf, ".pdf", maxOutput: 5));
        void Reject(string scenario, Action action)
        {
            try { action(); observations.Add(new(scenario, false, "unsupported or unbounded content accepted")); }
            catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException)
            { observations.Add(new(scenario, true, "rejected before indexing")); }
        }
    }

    private static string Parse(byte[] bytes, string extension, int maxBytes = 5 * 1024 * 1024, int maxPages = 128, int maxOutput = 2 * 1024 * 1024)
    {
        if (bytes.Length > maxBytes) throw new InvalidDataException("文件超过上限。");
        if (extension is ".txt" or ".md")
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (string.IsNullOrWhiteSpace(text) || text.Length > maxOutput) throw new InvalidDataException("文本输出无效。");
            return text;
        }
        if (extension != ".pdf") throw new InvalidDataException("不支持该格式。");
        using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
        if (document.NumberOfPages > maxPages) throw new InvalidDataException("页数超过上限。");
        var output = new StringBuilder();
        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            var text = ContentOrderTextExtractor.GetText(document.GetPage(pageNumber));
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("扫描或空白页面不支持首期文本导入。");
            if (output.Length + text.Length + 32 > maxOutput) throw new InvalidDataException("解析输出超过上限。");
            output.Append(CultureInfo.InvariantCulture, $"[page:{pageNumber}]\n{text}\n");
        }
        return output.ToString();
    }

    private static byte[] CreatePdf(string text, int pages, bool compressed = false)
    {
        // 原创最小 PDF，内置 ToUnicode 映射；不嵌入或复制第三方字体及文档资源。
        var characters = text.Distinct().ToArray();
        var map = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /R03 def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        if (characters.Length > 0)
        {
            map.Append(CultureInfo.InvariantCulture, $"{characters.Length} beginbfchar\n");
            foreach (var character in characters) map.Append(CultureInfo.InvariantCulture, $"<{(int)character:X4}> <{(int)character:X4}>\n");
            map.Append("endbfchar\n");
        }
        map.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Count {pages} /Kids [{string.Join(" ", Enumerable.Range(0, pages).Select(i => $"{7 + i * 2} 0 R"))}] >>",
            "<< /Type /Font /Subtype /Type0 /BaseFont /STSong-Light /Encoding /Identity-H /DescendantFonts [4 0 R] /ToUnicode 5 0 R >>",
            "<< /Type /Font /Subtype /CIDFontType0 /BaseFont /STSong-Light /CIDSystemInfo << /Registry (Adobe) /Ordering (GB1) /Supplement 4 >> /FontDescriptor 6 0 R /DW 1000 >>",
            Stream(map.ToString()),
            "<< /Type /FontDescriptor /FontName /STSong-Light /Flags 4 /FontBBox [0 -200 1000 1000] /ItalicAngle 0 /Ascent 880 /Descent -120 /CapHeight 880 /StemV 80 >>"
        };
        var hex = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text));
        for (var i = 0; i < pages; i++)
        {
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {8 + i * 2} 0 R >>");
            objects.Add(Stream($"BT /F1 12 Tf 40 750 Td <{hex}> Tj ET\n", compressed));
        }
        var document = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(document.ToString()));
            document.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(document.ToString());
        document.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) document.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        document.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(document.ToString());
        static string Stream(string content, bool compressed = false)
        {
            if (!compressed) return $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream";
            using var output = new MemoryStream();
            using (var compressor = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                compressor.Write(Encoding.ASCII.GetBytes(content));
            var encoded = Convert.ToHexString(output.ToArray()) + ">\n";
            return $"<< /Length {encoded.Length} /Filter [/ASCIIHexDecode /FlateDecode] >>\nstream\n{encoded}endstream";
        }
    }
}
