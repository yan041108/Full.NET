using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>来源 MIME 和服务端登记的封闭策略决定解析协议；请求不能指定程序、模型地址或物理路径。</summary>
internal static class AiKnowledgeDocumentFormats
{
    internal static AiKnowledgeParsedFormat? Format(string contentType) => AiKnowledgeFilePolicy.InputFormatFor(contentType) switch
    {
        "txt" => AiKnowledgeParsedFormat.PlainText, "md" => AiKnowledgeParsedFormat.Markdown,
        "pdf" => AiKnowledgeParsedFormat.Pdf, "docx" => AiKnowledgeParsedFormat.Docx,
        "doc" => AiKnowledgeParsedFormat.Word, "csv" => AiKnowledgeParsedFormat.Csv,
        "html" => AiKnowledgeParsedFormat.Html, "xls" or "xlsx" => AiKnowledgeParsedFormat.Excel,
        "ppt" or "pptx" => AiKnowledgeParsedFormat.Presentation,
        null => null, _ => AiKnowledgeParsedFormat.Image
    };

    internal static bool IsDocument(string contentType) => Format(contentType) is { } format
        && format is not (AiKnowledgeParsedFormat.PlainText or AiKnowledgeParsedFormat.Markdown);

    internal static AiKnowledgeParsePolicy? Policy(string contentType, string key)
    {
        if (Format(contentType) is not { } format || key is null || key.Length > 64) return null;
        var policy = AiKnowledgeParsePolicy.Default(format) with { InputFormat = AiKnowledgeFilePolicy.InputFormatFor(contentType)! };
        if (key == "default") return policy;
        if (key == "include_hidden" && format is AiKnowledgeParsedFormat.Excel or AiKnowledgeParsedFormat.Presentation)
            return policy with { IncludeHidden = true };
        if (key == "html_gb18030" && format == AiKnowledgeParsedFormat.Html) return policy with { TextEncoding = "gb18030" };
        if (format != AiKnowledgeParsedFormat.Csv) return null;
        var parts = key.Split('_');
        if (parts.Length != 4 || parts[0] != "csv" || parts[1] is not ("utf8" or "gb18030")
            || parts[2] is not ("comma" or "semicolon" or "tab") || parts[3] is not ("header" or "plain")) return null;
        return policy with { TextEncoding = parts[1] == "utf8" ? "utf-8" : "gb18030",
            CsvDelimiter = parts[2] switch { "comma" => ",", "semicolon" => ";", _ => "\t" }, CsvHeader = parts[3] == "header" };
    }

    internal static bool Matches(AiKnowledgeParsedDocument document, string contentType, string policyKey)
        => Format(contentType) == document.Format && Policy(contentType, policyKey) == document.Policy;

    internal static string ContentType(AiKnowledgeParsedDocument document)
        => AiKnowledgeFilePolicy.ContentTypeFor(document.Policy.InputFormat) ?? throw new InvalidDataException("fn_ai_parse_invalid_policy");

    internal static string PolicyKey(AiKnowledgeParsedDocument document)
    {
        var type = ContentType(document); var p = document.Policy;
        if (p == Policy(type, "default")) return "default";
        if (document.Format == AiKnowledgeParsedFormat.Csv)
            return "csv_" + (p.TextEncoding == "utf-8" ? "utf8" : "gb18030") + "_" + (p.CsvDelimiter switch { "," => "comma", ";" => "semicolon", _ => "tab" }) + "_" + (p.CsvHeader ? "header" : "plain");
        if (document.Format == AiKnowledgeParsedFormat.Html && p.TextEncoding == "gb18030") return "html_gb18030";
        if (p.IncludeHidden && document.Format is AiKnowledgeParsedFormat.Excel or AiKnowledgeParsedFormat.Presentation) return "include_hidden";
        throw new InvalidDataException("fn_ai_parse_invalid_policy");
    }
}
