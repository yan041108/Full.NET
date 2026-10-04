using System.Text.Json;

namespace Full.NET.Ai.PdfParser;

/// <summary>完整页验收后才能序列化，失败不提供部分成功结果。</summary>
internal sealed class PdfOutput
{
    internal const int MaxPages = 128;
    internal const int MaxCharacters = 2 * 1024 * 1024;
    private readonly List<(int Number, string Text)> pages = [];
    private int characters;
    private string? failure;

    internal void Add(int number, string text)
    {
        EnsureHealthy();
        if (pages.Count >= MaxPages) Reject("fn_ai_pdf_page_limit");
        if (number != pages.Count + 1) Reject("fn_ai_pdf_page_order");
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\0' || char.IsLowSurrogate(character)) Reject("fn_ai_pdf_invalid_text");
            if (char.IsHighSurrogate(character)
                && (++index == text.Length || !char.IsLowSurrogate(text[index]))) Reject("fn_ai_pdf_invalid_text");
        }
        if (string.IsNullOrWhiteSpace(text)) Reject("fn_ai_pdf_empty_page");
        if (text.Length > MaxCharacters - characters) Reject("fn_ai_pdf_output_limit");
        characters += text.Length;
        pages.Add((number, text));
    }

    internal void Write(string hash, Stream output)
    {
        EnsureHealthy();
        if (pages.Count == 0) Reject("fn_ai_pdf_empty_page");
        // 先在有字符上限的内存缓冲中完成 JSON，验证失败不向调用者泄漏部分页。
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", 1);
            writer.WriteString("parserVersion", PdfParser.Version);
            writer.WriteString("contentHash", hash);
            writer.WriteStartArray("pages");
            foreach (var page in pages)
            {
                writer.WriteStartObject();
                writer.WriteNumber("pageNumber", page.Number);
                writer.WriteString("text", page.Text);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        buffer.Position = 0;
        buffer.CopyTo(output);
    }

    private void EnsureHealthy()
    {
        if (failure is not null) throw new PdfRejectedException(failure);
    }
    private void Reject(string code)
    {
        failure ??= code;
        throw new PdfRejectedException(failure);
    }
}
