using System.Text;
using System.Text.Json;

namespace Full.NET.Ai.DocxParser;

/// <summary>提取正文中的区间与 OOXML 逻辑坐标，DOCX 不提供渲染页码或原 ZIP 字节位置。</summary>
internal sealed record DocxBlock(string Kind, int Offset, int Length, int ParagraphNumber,
    int? TableNumber = null, int? RowNumber = null, int? CellNumber = null, int CellParagraphCount = 0, bool IsHeader = false);

/// <summary>完整结果才进入标准输出；文本只存一次，区间仅引用提取正文，失败不截断。</summary>
internal sealed class DocxOutput
{
    internal const int MaxCharacters = 2 * 1024 * 1024;
    internal const int MaxBlocks = 8192;
    private readonly StringBuilder text = new();
    private readonly List<DocxBlock> blocks = [];

    internal void Add(string value, string separator, int paragraph, int? table = null, int? row = null, int? cell = null, int cellParagraphCount = 0, bool header = false)
    {
        if (blocks.Count >= MaxBlocks) Reject("fn_ai_docx_structure_limit");
        if (value.Length > MaxCharacters - text.Length || separator.Length > MaxCharacters - text.Length - value.Length)
            Reject("fn_ai_docx_output_limit");
        blocks.Add(new(table is null ? "paragraph" : "tableCell", text.Length, value.Length, paragraph, table, row, cell, cellParagraphCount, header));
        text.Append(value); text.Append(separator);
    }

    internal byte[] Serialize(string hash, int sizeBytes, CancellationToken token)
    {
        if (blocks.Count == 0 || string.IsNullOrWhiteSpace(text.ToString())) Reject("fn_ai_docx_empty_document");
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject(); writer.WriteNumber("protocolVersion", 1); writer.WriteString("parserVersion", DocxParser.Version);
            writer.WriteString("contentHash", hash); writer.WriteNumber("sizeBytes", sizeBytes); writer.WriteString("text", text.ToString());
            writer.WriteStartArray("blocks");
            for (var index = 0; index < blocks.Count; index++)
            {
                token.ThrowIfCancellationRequested(); var block = blocks[index];
                writer.WriteStartObject(); writer.WriteNumber("sequence", index); writer.WriteString("kind", block.Kind);
                writer.WriteNumber("offset", block.Offset); writer.WriteNumber("length", block.Length); writer.WriteNumber("paragraphNumber", block.ParagraphNumber);
                Nullable(writer, "tableNumber", block.TableNumber); Nullable(writer, "rowNumber", block.RowNumber); Nullable(writer, "cellNumber", block.CellNumber);
                writer.WriteNumber("cellParagraphCount", block.CellParagraphCount); writer.WriteBoolean("isHeader", block.IsHeader); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        }
        // 字符上限已经在分配前生效；完整转义 JSON 另设上限，不输出部分对象。
        if (output.Length > 16 * 1024 * 1024) Reject("fn_ai_docx_output_limit");
        token.ThrowIfCancellationRequested();
        return output.ToArray();
    }
    private static void Nullable(Utf8JsonWriter writer, string name, int? value)
    { if (value is { } number) writer.WriteNumber(name, number); else writer.WriteNull(name); }
    private static void Reject(string code) => throw new DocxRejectedException(code);
}
