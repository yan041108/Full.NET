using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>隔离输出消费失败，不携带来源正文。</summary>
internal enum AiKnowledgePdfReadFailure { None, InvalidSource, InvalidOutput, OutputTooLarge, UnsupportedParser, SourceMismatch, InvalidPage, EmptyPage, TooManyPages }
/// <summary>页码从一开始；文本保留解析输出的 Unicode 与换行。</summary>
internal sealed record AiKnowledgePdfPage(int PageNumber, string Text);
/// <summary>摘要绑定原始 PDF 字节，页内位置绑定已验收的提取文本。</summary>
internal sealed record AiKnowledgeParsedPdf(string ContentHash, int SizeBytes, IReadOnlyList<AiKnowledgePdfPage> Pages)
{
    internal const string ParserVersion = "pdfpig-0.1.16-content-order-v1";
}
/// <summary>只有完整输出验收成功才提供全部页。</summary>
internal sealed record AiKnowledgePdfReadResult(AiKnowledgeParsedPdf? Document, AiKnowledgePdfReadFailure Failure)
{
    internal bool IsSuccess => Document is not null;
}
/// <summary>接收隔离进程输出；不运行 PDF 库、不证明进程隔离或文件授权。</summary>
internal static class AiKnowledgePdfOutputReader
{
    internal const int MaxPages = 128;
    internal const int MaxSourceBytes = 5 * 1024 * 1024;
    // 每个 UTF-16 单元最多六个 JSON 转义字节；页元数据和封套也使用固定有界余量。
    internal const int MaxWireBytes = AiKnowledgeTextParser.MaxOutputCharacters * 6 + MaxPages * 96 + 512;

    /// <summary>接收完整的固定版本 JSON；拒绝未知/重复字段和任何部分结果，不接管流。</summary>
    /// <remarks>输出摘要只能证明声明与可信输入相符；执行器仍须隔离进程、绑定实际文件字节并设置硬超时。</remarks>
    /// <param name="output">调用者拥有的输出流；调用者负责截止时间、强制终止和退出码核对。</param>
    /// <param name="expectedContentHash">来源授权边界对原始 PDF 字节计算的 SHA-256，不能取自输出本身。</param>
    /// <param name="sourceSizeBytes">同一已授权文件版本的原始字节数，不是提取文本的编码大小。</param>
    /// <param name="token">取消读流或有界验证；不声称能终止解析进程。</param>
    internal static async Task<AiKnowledgePdfReadResult> ReadAsync(Stream output, string expectedContentHash, int sourceSizeBytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(output);
        if (!ValidSource(expectedContentHash, sourceSizeBytes)) return Rejected(AiKnowledgePdfReadFailure.InvalidSource);
        using var bytes = new MemoryStream();
        var buffer = new byte[32768];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = await output.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxWireBytes + 1 - (int)bytes.Length)), token);
            token.ThrowIfCancellationRequested();
            if (count == 0) break;
            if (count > MaxWireBytes - bytes.Length) return Rejected(AiKnowledgePdfReadFailure.OutputTooLarge);
            bytes.Write(buffer, 0, count);
        }
        return Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), expectedContentHash, sourceSizeBytes, token);
    }

    private static AiKnowledgePdfReadResult Parse(ReadOnlyMemory<byte> bytes, string expectedHash, int sourceSize, CancellationToken token)
    {
        try
        {
            // 直接使用静态 JSON DOM，不反射反序列化；深度、线字节及页/字符数分别约束。
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            if (!ExactFields(root, ["protocolVersion", "parserVersion", "contentHash", "pages"])) return Rejected(AiKnowledgePdfReadFailure.InvalidOutput);
            var protocol = root.GetProperty("protocolVersion");
            if (protocol.ValueKind != JsonValueKind.Number || !protocol.TryGetInt32(out var version)) return Rejected(AiKnowledgePdfReadFailure.InvalidOutput);
            var parser = root.GetProperty("parserVersion");
            var hash = root.GetProperty("contentHash");
            var pagesJson = root.GetProperty("pages");
            if (parser.ValueKind != JsonValueKind.String || hash.ValueKind != JsonValueKind.String || pagesJson.ValueKind != JsonValueKind.Array)
                return Rejected(AiKnowledgePdfReadFailure.InvalidOutput);
            if (version != 1 || parser.GetString() != AiKnowledgeParsedPdf.ParserVersion) return Rejected(AiKnowledgePdfReadFailure.UnsupportedParser);
            if (!string.Equals(hash.GetString(), expectedHash, StringComparison.OrdinalIgnoreCase)) return Rejected(AiKnowledgePdfReadFailure.SourceMismatch);
            if (pagesJson.GetArrayLength() > MaxPages) return Rejected(AiKnowledgePdfReadFailure.TooManyPages);
            var pages = new List<AiKnowledgePdfPage>();
            var total = 0;
            foreach (var page in pagesJson.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                if (!ExactFields(page, ["pageNumber", "text"])) return Rejected(AiKnowledgePdfReadFailure.InvalidOutput);
                var numberJson = page.GetProperty("pageNumber");
                var textJson = page.GetProperty("text");
                if (numberJson.ValueKind != JsonValueKind.Number || !numberJson.TryGetInt32(out var number) || textJson.ValueKind != JsonValueKind.String)
                    return Rejected(AiKnowledgePdfReadFailure.InvalidOutput);
                if (number != pages.Count + 1) return Rejected(AiKnowledgePdfReadFailure.InvalidPage);
                var text = textJson.GetString()!;
                var failure = ValidateText(text, ref total, token);
                if (failure != AiKnowledgePdfReadFailure.None) return Rejected(failure);
                pages.Add(new(number, text));
            }
            if (pages.Count == 0) return Rejected(AiKnowledgePdfReadFailure.InvalidPage);
            token.ThrowIfCancellationRequested();
            return new(new(expectedHash.ToLowerInvariant(), sourceSize, Array.AsReadOnly(pages.ToArray())), AiKnowledgePdfReadFailure.None);
        }
        catch (JsonException) { return Rejected(AiKnowledgePdfReadFailure.InvalidOutput); }
        // System.Text.Json 对无配对代理项等无效字符串会在 GetString 时拒绝。
        catch (InvalidOperationException) { return Rejected(AiKnowledgePdfReadFailure.InvalidOutput); }
    }

    private static bool ExactFields(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = 0;
        foreach (var property in element.EnumerateObject())
        {
            var index = Array.IndexOf(names, property.Name);
            if (index < 0 || (seen & (1 << index)) != 0) return false;
            seen |= 1 << index;
        }
        return seen == (1 << names.Length) - 1;
    }

    /// <summary>内部调用也复核全部页，避免伪造记录绕过消费边界。</summary>
    internal static AiKnowledgePdfReadFailure ValidateDocument(AiKnowledgeParsedPdf document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);
        if (!ValidSource(document.ContentHash, document.SizeBytes)) return AiKnowledgePdfReadFailure.InvalidSource;
        if (document.Pages is null || document.Pages.Count == 0) return AiKnowledgePdfReadFailure.InvalidPage;
        if (document.Pages.Count > MaxPages) return AiKnowledgePdfReadFailure.TooManyPages;
        var total = 0;
        for (var index = 0; index < document.Pages.Count; index++)
        {
            var page = document.Pages[index];
            if (page is null || page.PageNumber != index + 1) return AiKnowledgePdfReadFailure.InvalidPage;
            var failure = ValidateText(page.Text, ref total, token);
            if (failure != AiKnowledgePdfReadFailure.None) return failure;
        }
        token.ThrowIfCancellationRequested();
        return AiKnowledgePdfReadFailure.None;
    }

    private static AiKnowledgePdfReadFailure ValidateText(string text, ref int total, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (text is null) return AiKnowledgePdfReadFailure.InvalidPage;
        if (text.Length > AiKnowledgeTextParser.MaxOutputCharacters - total) return AiKnowledgePdfReadFailure.OutputTooLarge;
        if (string.IsNullOrWhiteSpace(text)) return AiKnowledgePdfReadFailure.EmptyPage;
        if (text.Contains('\0')) return AiKnowledgePdfReadFailure.InvalidPage;
        var nextCancellationCheck = 0;
        for (var position = 0; position < text.Length;)
        {
            if (position >= nextCancellationCheck) { token.ThrowIfCancellationRequested(); nextCancellationCheck = position + 4096; }
            if (Rune.DecodeFromUtf16(text.AsSpan(position), out _, out var consumed) != OperationStatus.Done) return AiKnowledgePdfReadFailure.InvalidPage;
            position += consumed;
        }
        total += text.Length;
        return AiKnowledgePdfReadFailure.None;
    }

    private static bool ValidSource(string hash, int size) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit) && size > 0 && size <= MaxSourceBytes;
    private static AiKnowledgePdfReadResult Rejected(AiKnowledgePdfReadFailure failure) => new(null, failure);
}
