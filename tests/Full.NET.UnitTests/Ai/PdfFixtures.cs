using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Full.NET.UnitTests.Ai;

// 复用仓库原创 ToUnicode 合成构造器，不复制第三方字体或 PDF 样例。
internal static class PdfFixtures
{
    internal static byte[] CreatePdf(string text, int pages, bool compressed = false)
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
