using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>只保存独立修订；原解析快照由单独的不可变来源记录提供。</summary>
internal sealed record AiKnowledgeCorrectionWireDocument(int ProtocolVersion, string SourceHash,
    AiKnowledgeCorrectionAnchor[] Anchors, AiKnowledgeCorrectionWireBlock[] Blocks);
/// <summary>持久线格式不复用客户端的自由位置声明。</summary>
internal sealed record AiKnowledgeCorrectionWireBlock(Guid Id, bool Excluded, AiKnowledgeCorrectionPart[] Parts, string Separator);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8)]
[JsonSerializable(typeof(AiKnowledgeCorrectionWireDocument))]
internal partial class AiKnowledgeCorrectionWireJsonContext : JsonSerializerContext;

/// <summary>修订与完整原解析/运行时精确绑定；坏快照不能恢复为可索引内容。</summary>
internal static class AiKnowledgeCorrectionWireCodec
{
    internal const int MaxWireBytes = 12 * 1024 * 1024;

    internal static string SourceHash(AiKnowledgeParsedDocument source, string parserRuntimeId, CancellationToken token)
    {
        if (!ValidRuntime(parserRuntimeId)) throw Invalid();
        var original = AiKnowledgeParsedWireCodec.Encode(source, token);
        try
        {
            // 分段加长度，避免运行时标识和原快照之间产生拼接歧义。
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData("knowledge-correction-source-v1\0"u8);
            var runtime = Encoding.UTF8.GetBytes(parserRuntimeId);
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, runtime.Length);
            hash.AppendData(length); hash.AppendData(runtime); hash.AppendData(original);
            token.ThrowIfCancellationRequested(); return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally { CryptographicOperations.ZeroMemory(original); }
    }

    internal static bool ValidRuntime(string parserRuntimeId) => parserRuntimeId is { Length: > 0 and <= 128 }
        && parserRuntimeId.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '.' or '-' or '_');

    internal static byte[] Encode(AiKnowledgeCorrectionDocument document, string parserRuntimeId, CancellationToken token)
    {
        var frozen = AiKnowledgeCorrectionDocument.Restore(document.Source, document.Anchors, document.Blocks, token);
        var wire = new AiKnowledgeCorrectionWireDocument(1, SourceHash(frozen.Source, parserRuntimeId, token),
            frozen.Anchors.OrderBy(anchor => anchor.BlockSequence).ThenBy(anchor => anchor.Offset).ToArray(),
            frozen.Blocks.Select(block => new AiKnowledgeCorrectionWireBlock(block.Id, block.Excluded, block.Parts.ToArray(), block.Separator)).ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(wire, AiKnowledgeCorrectionWireJsonContext.Default.AiKnowledgeCorrectionWireDocument);
        try
        {
            token.ThrowIfCancellationRequested(); if (bytes.Length > MaxWireBytes) throw Invalid(); return bytes;
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    /// <summary>先检查有界结构与全部必选字段，再反序列化和核验原文分区；任何错误拒绝整个修订。</summary>
    internal static AiKnowledgeCorrectionDocument? Decode(ReadOnlyMemory<byte> input, AiKnowledgeParsedDocument source,
        string parserRuntimeId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (input.Length is < 2 or > MaxWireBytes) return null;
        var captured = input.ToArray();
        try
        {
            using var json = JsonDocument.Parse(captured, new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            if (!Exact(root, "protocolVersion", "sourceHash", "anchors", "blocks")) return null;
            var anchors = root.GetProperty("anchors"); var blocks = root.GetProperty("blocks");
            if (anchors.ValueKind != JsonValueKind.Array || anchors.GetArrayLength() > AiKnowledgeCorrectionDocument.MaxItems
                || blocks.ValueKind != JsonValueKind.Array || blocks.GetArrayLength() > AiKnowledgeCorrectionDocument.MaxItems) return null;
            foreach (var anchor in anchors.EnumerateArray())
                if (!Exact(anchor, "id", "blockSequence", "offset", "length")) return null;
            var total = 0;
            foreach (var block in blocks.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                if (!Exact(block, "id", "excluded", "parts", "separator")) return null;
                var parts = block.GetProperty("parts");
                if (parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() is < 1 or > 32
                    || (total += parts.GetArrayLength()) > AiKnowledgeCorrectionDocument.MaxItems) return null;
                foreach (var part in parts.EnumerateArray())
                    if (!Exact(part, "id", "text", "origin", "anchorId", "prefix")) return null;
            }
            var wire = JsonSerializer.Deserialize(captured, AiKnowledgeCorrectionWireJsonContext.Default.AiKnowledgeCorrectionWireDocument);
            if (wire is null || wire.ProtocolVersion != 1 || wire.SourceHash != SourceHash(source, parserRuntimeId, token)) return null;
            return AiKnowledgeCorrectionDocument.Restore(source, wire.Anchors,
                wire.Blocks.Select(block => new AiKnowledgeCorrectionBlock(block.Id, block.Excluded, block.Parts, block.Separator)).ToArray(), token);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or ArgumentException) { return null; }
        finally { CryptographicOperations.ZeroMemory(captured); }
    }

    private static bool Exact(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) return false;
        return seen.Count == fields.Length;
    }
    private static InvalidDataException Invalid() => new("Invalid knowledge correction snapshot.");
}
