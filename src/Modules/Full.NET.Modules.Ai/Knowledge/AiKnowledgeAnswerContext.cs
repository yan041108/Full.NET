using Full.NET.Abstractions.Ids;
using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>服务端引用携带已确认位置；模型只可选择标识，不能重写位置或来源。</summary>
internal sealed record AiKnowledgeCitation(Guid Id, AiKnowledgeRetrievalChunk Chunk);
/// <summary>缓冲且校验过的模型结果，仍须经完整来源复核后交付。</summary>
internal sealed record AiKnowledgeAnswer(string Text, IReadOnlyList<AiKnowledgeCitation> Citations, bool Refused);

/// <summary>有界 JSON 将来源作为数据传递；只接受服务器分配的引用，不接受模型声明的文件位置。</summary>
internal sealed class AiKnowledgeAnswerContext
{
    internal string Json { get; private init; } = "";
    internal IReadOnlyList<AiKnowledgeCitation> Citations { get; private init; } = [];
    /// <summary>选择能容纳的排名前缀完整块；整个 JSON 包限额，不裁剪正文或跳过高排名块。</summary>
    internal static AiKnowledgeAnswerContext Create(string question, IReadOnlyList<AiKnowledgeRankedChunk> chunks,
        IIdGenerator ids, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(chunks); ArgumentNullException.ThrowIfNull(ids);
        if (string.IsNullOrWhiteSpace(question) || question.Length > 4096 || chunks.Count is < 1 or > 256) throw Invalid();
        var citations = new List<AiKnowledgeCitation>(); var seen = new HashSet<Guid>();
        var tokenizer = AiKnowledgeCl100kTokenizer.Get(token); string? json = null;
        // 每个前缀重新计算元数据与问题开销；遇到第一块超限停止，依赖仍由完整检索快照保留。
        foreach (var ranked in chunks.Take(8))
        {
            token.ThrowIfCancellationRequested(); var chunk = ranked.Chunk;
            if (!double.IsFinite(ranked.Score) || string.IsNullOrWhiteSpace(chunk.Text) || chunk.Text.Length > 8000
                || !AiKnowledgeAnswerLineageCodec.ValidLocation(chunk.DocxLocation, chunk.PageNumber)
                || (chunk.Source.ContentType == Domain.AiKnowledgeFilePolicy.DocxContentType) != (chunk.DocxLocation is not null)
                || chunk.Offset < 0 || chunk.Length != chunk.Text.Length || chunk.Sequence < 0 || chunk.PageNumber is < 1) throw Invalid();
            var id = ids.NewId(); if (id == Guid.Empty || id.Version != 7 || !seen.Add(id)) throw Invalid();
            citations.Add(new(id, chunk with { Vector = chunk.Vector.ToArray() }));
            var candidate = Serialize(question, citations, token);
            if (tokenizer.CountTokens(candidate, considerNormalization: false) > 4096)
            { citations.RemoveAt(citations.Count - 1); break; }
            json = candidate;
        }
        if (json is null || citations.Count == 0) throw Invalid();
        token.ThrowIfCancellationRequested(); return new() { Json = json, Citations = citations.AsReadOnly() };
    }
    private static string Serialize(string question, IReadOnlyList<AiKnowledgeCitation> citations, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject(); writer.WriteString("question", question); writer.WriteStartArray("sources");
            foreach (var citation in citations)
            {
                token.ThrowIfCancellationRequested(); var chunk = citation.Chunk;
                writer.WriteStartObject(); writer.WriteString("citationId", citation.Id);
                writer.WriteString("documentId", chunk.Source.DocumentId); writer.WriteString("versionId", chunk.Source.VersionId);
                if (chunk.PageNumber is { } page) writer.WriteNumber("page", page); else writer.WriteNull("page");
                if (chunk.DocxLocation is { } location)
                {
                    writer.WriteStartObject("docxLocation"); writer.WriteNumber("blockSequence", location.BlockSequence);
                    writer.WriteNumber("paragraphNumber", location.ParagraphNumber);
                    if (location.TableNumber is { } table) writer.WriteNumber("tableNumber", table); else writer.WriteNull("tableNumber");
                    if (location.RowNumber is { } row) writer.WriteNumber("rowNumber", row); else writer.WriteNull("rowNumber");
                    if (location.CellNumber is { } cell) writer.WriteNumber("cellNumber", cell); else writer.WriteNull("cellNumber");
                    writer.WriteEndObject();
                }
                writer.WriteNumber("offset", chunk.Offset); writer.WriteNumber("length", chunk.Length); writer.WriteString("text", chunk.Text); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        return new UTF8Encoding(false, true).GetString(buffer.ToArray());
    }
    /// <summary>冻结有界模型 JSON，拒绝未知字段与伪造引用；本方法不独立授予正文交付资格。</summary>
    internal AiKnowledgeAnswer Parse(ReadOnlyMemory<byte> response, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (response.Length is < 2 or > 65536) throw Invalid();
        var bytes = response.ToArray();
        try
        {
            // 禁止替换非法 UTF-8 字节；重复字段也不能由 JSON 最后一个值覆盖。
            _ = new UTF8Encoding(false, true).GetString(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement; if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in root.EnumerateObject())
                if (field.Name is not ("answer" or "citationIds" or "refusal") || !fields.Add(field.Name)) throw Invalid();
            if (fields.Count != 3 || root.GetProperty("answer").ValueKind != JsonValueKind.String
                || root.GetProperty("citationIds").ValueKind != JsonValueKind.Array
                || root.GetProperty("refusal").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid();
            var answer = root.GetProperty("answer").GetString()!; var refusal = root.GetProperty("refusal").GetBoolean();
            var refs = root.GetProperty("citationIds"); if (answer.Length > 16000 || refs.GetArrayLength() > 8) throw Invalid();
            var accepted = new List<AiKnowledgeCitation>(); var seen = new HashSet<Guid>();
            foreach (var item in refs.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                if (item.ValueKind != JsonValueKind.String || !item.TryGetGuid(out var id) || !seen.Add(id)) throw Invalid();
                var citation = Citations.SingleOrDefault(c => c.Id == id); if (citation is null) throw Invalid(); accepted.Add(citation);
            }
            if (refusal ? answer.Length != 0 || accepted.Count != 0 : string.IsNullOrWhiteSpace(answer) || accepted.Count == 0) throw Invalid();
            token.ThrowIfCancellationRequested(); return new(answer, accepted.AsReadOnly(), refusal);
        }
        catch (JsonException) { throw Invalid(); }
        catch (DecoderFallbackException) { throw Invalid(); }
    }
    private static InvalidDataException Invalid() => new("问答上下文或模型引用无效。");
}
