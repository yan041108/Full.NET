using System.Security.Cryptography;
using System.Text.Json;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>私有解析验收失败；不含文件名、正文或底层异常。</summary>
internal enum AiKnowledgeDocxReadFailure { None, InvalidSource, InvalidOutput, OutputTooLarge, UnsupportedParser, SourceMismatch, InvalidStructure }
/// <summary>提取正文中的 UTF-16 位置；段落及表行列均从一开始，块序号从零开始。</summary>
internal sealed record AiKnowledgeDocxBlock(int Sequence, string Kind, int Offset, int Length, int ParagraphNumber,
    int? TableNumber, int? RowNumber, int? CellNumber, int CellParagraphCount, bool IsHeader);
/// <summary>绑定原始 DOCX 摘要及全部提取正文；位置不能解释为 ZIP 字节或渲染页码。</summary>
internal sealed record AiKnowledgeParsedDocx(string ContentHash, int SizeBytes, string Text, IReadOnlyList<AiKnowledgeDocxBlock> Blocks)
{
    internal const string ParserVersion = "docx-ooxml-body-v1";
}
/// <summary>只有完整验收成功才提供文档，不返回部分结果。</summary>
internal sealed record AiKnowledgeDocxReadResult(AiKnowledgeParsedDocx? Document, AiKnowledgeDocxReadFailure Failure)
{
    internal bool IsSuccess => Document is not null;
}
/// <summary>消费固定进程协议，不证明执行隔离、来源授权或模型派发资格。</summary>
internal static class AiKnowledgeDocxOutputReader
{
    internal const int MaxWireBytes = 16 * 1024 * 1024;
    internal const int MaxSourceBytes = 5 * 1024 * 1024;
    /// <summary>有界读取完整输出；可信摘要和大小须由来源授权边界提供，流仍归调用者。</summary>
    internal static async Task<AiKnowledgeDocxReadResult> ReadAsync(Stream output, string hash, int size, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(output);
        if (!ValidSource(hash, size)) return Rejected(AiKnowledgeDocxReadFailure.InvalidSource);
        using var bytes = new MemoryStream(); var buffer = new byte[32768];
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var count = await output.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxWireBytes + 1 - (int)bytes.Length)), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested(); if (count == 0) break;
                if (count > MaxWireBytes - bytes.Length) return Rejected(AiKnowledgeDocxReadFailure.OutputTooLarge);
                bytes.Write(buffer, 0, count);
            }
            return Parse(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), hash, size, token);
        }
        finally { CryptographicOperations.ZeroMemory(buffer); CryptographicOperations.ZeroMemory(bytes.GetBuffer()); }
    }

    private static AiKnowledgeDocxReadResult Parse(ReadOnlyMemory<byte> bytes, string hash, int size, CancellationToken token)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 }); var root = json.RootElement;
            if (!ExactFields(root, ["protocolVersion", "parserVersion", "contentHash", "sizeBytes", "text", "blocks"])) return Rejected(AiKnowledgeDocxReadFailure.InvalidOutput);
            if (root.GetProperty("protocolVersion").GetInt32() != 1 || root.GetProperty("parserVersion").GetString() != AiKnowledgeParsedDocx.ParserVersion)
                return Rejected(AiKnowledgeDocxReadFailure.UnsupportedParser);
            if (!string.Equals(root.GetProperty("contentHash").GetString(), hash, StringComparison.OrdinalIgnoreCase) || root.GetProperty("sizeBytes").GetInt32() != size)
                return Rejected(AiKnowledgeDocxReadFailure.SourceMismatch);
            var text = root.GetProperty("text").GetString(); var list = root.GetProperty("blocks");
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 8192) return Rejected(AiKnowledgeDocxReadFailure.InvalidStructure);
            var blocks = new List<AiKnowledgeDocxBlock>();
            foreach (var block in list.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                if (!ExactFields(block, ["sequence", "kind", "offset", "length", "paragraphNumber", "tableNumber", "rowNumber", "cellNumber", "cellParagraphCount", "isHeader"]))
                    return Rejected(AiKnowledgeDocxReadFailure.InvalidOutput);
                blocks.Add(new(block.GetProperty("sequence").GetInt32(), block.GetProperty("kind").GetString()!, block.GetProperty("offset").GetInt32(),
                    block.GetProperty("length").GetInt32(), block.GetProperty("paragraphNumber").GetInt32(), Number(block, "tableNumber"), Number(block, "rowNumber"),
                    Number(block, "cellNumber"), block.GetProperty("cellParagraphCount").GetInt32(), block.GetProperty("isHeader").GetBoolean()));
            }
            var document = new AiKnowledgeParsedDocx(hash.ToLowerInvariant(), size, text!, Array.AsReadOnly(blocks.ToArray()));
            var failure = ValidateDocument(document, token);
            return failure == AiKnowledgeDocxReadFailure.None ? new(document, failure) : Rejected(failure);
        }
        catch (JsonException) { return Rejected(AiKnowledgeDocxReadFailure.InvalidOutput); }
        catch (InvalidOperationException) { return Rejected(AiKnowledgeDocxReadFailure.InvalidOutput); }
        catch (FormatException) { return Rejected(AiKnowledgeDocxReadFailure.InvalidOutput); }
    }

    /// <summary>直接构造的内部记录也完整复核；冻结与几何检查由同一边界维护。</summary>
    internal static AiKnowledgeDocxReadFailure ValidateDocument(AiKnowledgeParsedDocx document, CancellationToken token)
        => AiKnowledgeDocxStructure.Validate(document, token);

    internal static bool ValidSource(string hash, int size) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit) && size is > 0 and <= MaxSourceBytes;
    private static int? Number(JsonElement block, string field) => block.GetProperty(field).ValueKind == JsonValueKind.Null ? null : block.GetProperty(field).GetInt32();
    private static bool ExactFields(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = 0;
        foreach (var property in element.EnumerateObject())
        { var index = Array.IndexOf(names, property.Name); if (index < 0 || (seen & (1 << index)) != 0) return false; seen |= 1 << index; }
        return seen == (1 << names.Length) - 1;
    }
    private static AiKnowledgeDocxReadResult Rejected(AiKnowledgeDocxReadFailure failure) => new(null, failure);
}
