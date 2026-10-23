namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>解析范围和编码是来源证明的一部分；同一原文件不能在不改变计划摘要的情况下换用策略。</summary>
internal sealed record AiKnowledgeParsePolicy(string InputFormat, string? TextEncoding, string? CsvDelimiter, bool CsvHeader, bool IncludeHidden)
{
    internal static AiKnowledgeParsePolicy Default(AiKnowledgeParsedFormat format) => format switch
    {
        AiKnowledgeParsedFormat.PlainText => new("txt", "utf-8", null, false, false),
        AiKnowledgeParsedFormat.Markdown => new("md", "utf-8", null, false, false),
        AiKnowledgeParsedFormat.Pdf => new("pdf", null, null, false, false),
        AiKnowledgeParsedFormat.Docx => new("docx", null, null, false, false),
        AiKnowledgeParsedFormat.Csv => new("csv", "utf-8", ",", false, false),
        AiKnowledgeParsedFormat.Html => new("html", "utf-8", null, false, false),
        AiKnowledgeParsedFormat.Excel => new("xlsx", null, null, false, false),
        AiKnowledgeParsedFormat.Word => new("doc", null, null, false, false),
        AiKnowledgeParsedFormat.Presentation => new("pptx", null, null, false, false),
        _ => new("png", null, null, false, false)
    };

    internal bool ValidFor(AiKnowledgeParsedFormat format)
    {
        var expected = Default(format);
        if (format == AiKnowledgeParsedFormat.Csv)
            return InputFormat == "csv" && TextEncoding is "utf-8" or "gb18030" && CsvDelimiter is "," or ";" or "\t" && !IncludeHidden;
        if (CsvDelimiter is not null || CsvHeader || IncludeHidden && format is not (AiKnowledgeParsedFormat.Excel or AiKnowledgeParsedFormat.Presentation)) return false;
        if (format == AiKnowledgeParsedFormat.Html) return InputFormat == "html" && TextEncoding is "utf-8" or "gb18030";
        if (format is AiKnowledgeParsedFormat.PlainText or AiKnowledgeParsedFormat.Markdown) return this == expected;
        if (TextEncoding is not null) return false;
        return format switch
        {
            AiKnowledgeParsedFormat.Excel => InputFormat is "xls" or "xlsx",
            AiKnowledgeParsedFormat.Presentation => InputFormat is "ppt" or "pptx",
            AiKnowledgeParsedFormat.Image => InputFormat is "jpg" or "jpeg" or "jfif" or "png" or "apng" or "webp" or "bmp" or "tif" or "tiff" or "gif"
                or "heic" or "heif" or "avif" or "jp2" or "j2k" or "jxl" or "ico" or "tga" or "pcx" or "pnm" or "pbm" or "pgm" or "ppm" or "svg" or "svgz" or "wmf" or "emf" or "psd",
            _ => InputFormat == expected.InputFormat
        };
    }
}
