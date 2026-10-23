using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>所有字段必选，空的可选坐标也必须显式发送 null；枚举序号由私有 v1 协议固定。</summary>
internal sealed record AiKnowledgeParsedWireDocument(int ProtocolVersion, AiKnowledgeParsedFormat Format,
    string ParserVersion, string ContentHash, int SizeBytes, AiKnowledgeParsePolicy Policy, AiKnowledgeParsedBlock[] Blocks);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8)]
[JsonSerializable(typeof(AiKnowledgeParsedWireDocument))]
internal partial class AiKnowledgeParsedWireJsonContext : JsonSerializerContext;

/// <summary>共享解析进程线格式；验收完整字节和位置后才交付不可变文档，不依赖反射。</summary>
internal static class AiKnowledgeParsedWireCodec
{
    internal const int MaxWireBytes = 16 * 1024 * 1024;
    private static readonly string[] Root = ["protocolVersion", "format", "parserVersion", "contentHash", "sizeBytes", "policy", "blocks"];
    private static readonly string[] Policy = ["inputFormat", "textEncoding", "csvDelimiter", "csvHeader", "includeHidden"];
    private static readonly string[] Block = ["sequence", "text", "separator", "position"];
    private static readonly string[] Position = ["kind", "offset", "length", "pageNumber", "blockSequence", "paragraphNumber",
        "tableNumber", "rowNumber", "cellNumber", "cellParagraphCount", "isHeader", "grid", "slide", "htmlPath", "image"];
    private static readonly string[] Grid = ["sheetNumber", "sheetName", "row", "column", "cellType", "formula", "cacheState", "mergedRange", "rawValue", "numberFormat", "cachedValueType"];
    private static readonly string[] Slide = ["slideNumber", "shapeNumber", "rowNumber", "columnNumber", "rowSpan", "columnSpan"];
    private static readonly string[] Image = ["frameNumber", "imageWidth", "imageHeight", "x", "y", "width", "height", "confidence", "recognitionVersion", "originalWidth", "originalHeight", "orientation", "normalizationHash", "normalizationVersion", "tableNumber", "rowNumber", "columnNumber", "parent"];

    /// <summary>只有已验收的快照可编码；编码结果仍不表示来源授权、隔离执行或索引完成。</summary>
    internal static byte[] Encode(AiKnowledgeParsedDocument document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(document);
        var frozen = AiKnowledgeParsedDocument.FromBlocks(document.Format, document.ParserVersion,
            document.ContentHash, document.SizeBytes, document.Blocks, token, document.Policy) ?? throw new InvalidDataException("fn_ai_parse_invalid_structure");
        var wire = new AiKnowledgeParsedWireDocument(1, frozen.Format, frozen.ParserVersion, frozen.ContentHash, frozen.SizeBytes, frozen.Policy, frozen.Blocks.ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(wire, AiKnowledgeParsedWireJsonContext.Default.AiKnowledgeParsedWireDocument);
        try
        {
            token.ThrowIfCancellationRequested();
            if (bytes.Length <= MaxWireBytes) return bytes;
            throw new InvalidDataException("fn_ai_parse_output_limit");
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    /// <summary>可信格式、原摘要及大小来自授权版本；不从进程输出反推可接受来源。</summary>
    internal static AiKnowledgeParsedDocument? Decode(ReadOnlyMemory<byte> output, AiKnowledgeParsedFormat expectedFormat,
        string expectedHash, int expectedSize, CancellationToken token, AiKnowledgeParsePolicy? expectedPolicy = null)
    {
        token.ThrowIfCancellationRequested();
        if (output.Length is < 2 or > MaxWireBytes
            || !Enum.IsDefined(expectedFormat) || !(expectedPolicy ?? AiKnowledgeParsePolicy.Default(expectedFormat)).ValidFor(expectedFormat)
            || !AiKnowledgeParsedDocument.ValidIdentity(expectedHash, expectedSize, Domain.AiKnowledgeFilePolicy.MaxBytes)) return null;
        var captured = output.ToArray();
        try
        {
            using var json = JsonDocument.Parse(captured, new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            if (!Exact(root, Root) || !Exact(root.GetProperty("policy"), Policy)) return null;
            var list = root.GetProperty("blocks");
            if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 8192) return null;
            foreach (var block in list.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                if (!Exact(block, Block)) return null;
                var p = block.GetProperty("position");
                if (!PositionFields(p, true)) return null;
            }
            var wire = JsonSerializer.Deserialize(captured, AiKnowledgeParsedWireJsonContext.Default.AiKnowledgeParsedWireDocument);
            if (wire is null || wire.ProtocolVersion != 1 || wire.Format != expectedFormat || wire.SizeBytes != expectedSize
                || !string.Equals(wire.ContentHash, expectedHash, StringComparison.OrdinalIgnoreCase)
                || wire.Policy != (expectedPolicy ?? AiKnowledgeParsePolicy.Default(expectedFormat))) return null;
            return AiKnowledgeParsedDocument.FromBlocks(wire.Format, wire.ParserVersion, expectedHash, expectedSize, wire.Blocks, token, wire.Policy);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        finally { CryptographicOperations.ZeroMemory(captured); }
    }

    /// <summary>流归调用者；输出预算包含所有结构和空白，溢出立即拒绝。</summary>
    internal static async Task<AiKnowledgeParsedDocument?> ReadAsync(Stream output, AiKnowledgeParsedFormat format,
        string hash, int size, CancellationToken token, AiKnowledgeParsePolicy? policy = null)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(output);
        if (!Enum.IsDefined(format) || !(policy ?? AiKnowledgeParsePolicy.Default(format)).ValidFor(format)
            || !AiKnowledgeParsedDocument.ValidIdentity(hash, size, Domain.AiKnowledgeFilePolicy.MaxBytes)) return null;
        using var bytes = new MemoryStream(); var buffer = new byte[32768];
        try
        {
            while (true)
            {
                var count = await output.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxWireBytes + 1 - (int)bytes.Length)), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (count == 0) return Decode(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), format, hash, size, token, policy);
                if (count > MaxWireBytes - bytes.Length) return null;
                bytes.Write(buffer, 0, count);
            }
        }
        finally { CryptographicOperations.ZeroMemory(buffer); CryptographicOperations.ZeroMemory(bytes.GetBuffer()); }
    }

    private static bool Optional(JsonElement value, string[] fields) => value.ValueKind == JsonValueKind.Null || Exact(value, fields);
    private static bool PositionFields(JsonElement p, bool allowImage)
    {
        if (!Exact(p, Position) || !Optional(p.GetProperty("grid"), Grid) || !Optional(p.GetProperty("slide"), Slide)) return false;
        var image = p.GetProperty("image");
        if (image.ValueKind == JsonValueKind.Null) return true;
        if (!allowImage || !Exact(image, Image)) return false;
        var parent = image.GetProperty("parent");
        return parent.ValueKind == JsonValueKind.Null || PositionFields(parent, false);
    }
    private static bool Exact(JsonElement value, string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var seen = 0;
        foreach (var property in value.EnumerateObject())
        {
            var index = Array.IndexOf(fields, property.Name);
            if (index < 0 || (seen & (1 << index)) != 0) return false;
            seen |= 1 << index;
        }
        return seen == (1 << fields.Length) - 1;
    }
}
