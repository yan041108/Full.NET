using System.IO.Compression;

namespace Full.NET.Ai.DocxParser;

/// <summary>仅在内存有界读取 OPC 部件；所有实际解压计入预算，不写文件或访问关系目标。</summary>
internal static class DocxArchive
{
    internal const int MaxEntries = 256;
    internal const int MaxEntryBytes = 8 * 1024 * 1024;
    internal const int MaxInflatedBytes = 16 * 1024 * 1024;
    private const int MaxMetadataBytes = 256 * 1024;

    internal static async Task<IReadOnlyDictionary<string, byte[]>> ReadAsync(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length < 4 || !bytes.AsSpan(0, 4).SequenceEqual("PK\x03\x04"u8)) Reject("fn_ai_docx_invalid_package");
        using var source = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, false);
        if (archive.Entries.Count > MaxEntries) Reject("fn_ai_docx_entry_count_limit");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var buffer = new byte[8192];
        var total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName;
            if (!SafeName(name) || !names.Add(name)) Reject("fn_ai_docx_invalid_package");
            // 宏、嵌入对象及未支持的可见部件不能静默丢弃后报告完整正文。
            if (name.Contains("vba", StringComparison.OrdinalIgnoreCase) || name.StartsWith("word/embeddings/", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) || name.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase)
                || name.Equals("word/footnotes.xml", StringComparison.OrdinalIgnoreCase) || name.Equals("word/endnotes.xml", StringComparison.OrdinalIgnoreCase))
                Reject("fn_ai_docx_unsupported_structure");
            var limit = name is "[Content_Types].xml" or "_rels/.rels" ? MaxMetadataBytes : MaxEntryBytes;
            if (entry.Length > limit) Reject("fn_ai_docx_entry_limit");
            using var inflated = entry.Open();
            // 保存全部有界部件，以内容类型而非扩展名识别别名样式；总分配仍受合计解压上限约束。
            using var saved = new MemoryStream();
            var count = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                // 中央目录声明不构成安全证明；逐次读取实际字节，上限加一立即拒绝。
                var remaining = Math.Min(limit - count, MaxInflatedBytes - total);
                var read = await inflated.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (read == 0) break;
                if (read > limit - count) Reject("fn_ai_docx_entry_limit");
                if (read > MaxInflatedBytes - total) Reject("fn_ai_docx_inflated_limit");
                count += read; total += read;
                saved.Write(buffer, 0, read);
            }
            if (count != entry.Length) Reject("fn_ai_docx_invalid_package");
            parts.Add(name, saved.ToArray());
        }
        if (!parts.ContainsKey("[Content_Types].xml") || !parts.ContainsKey("_rels/.rels") || !parts.ContainsKey("word/document.xml")) Reject("fn_ai_docx_invalid_package");
        token.ThrowIfCancellationRequested();
        return parts;
    }

    private static bool SafeName(string name)
    {
        if (name.Length is 0 or > 256 || name.StartsWith('/') || name.IndexOfAny(['\\', ':', '%']) >= 0
            || name.Any(value => value < 32 || value > 126)) return false;
        var path = name.EndsWith('/') ? name[..^1] : name;
        return path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
    }
    private static void Reject(string code) => throw new DocxRejectedException(code);
}
