namespace Full.NET.Modules.Document.Providers.OfficePreview;

/// <summary>先解析可信参数模板再替换路径，每个替换结果作为一个 ArgumentList 元素。</summary>
internal static class DocumentPreviewProcessArguments
{
    internal static IReadOnlyList<string> Build(string template, string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{inputPath}", StringComparison.Ordinal)
            || !template.Contains("{outputPath}", StringComparison.Ordinal)) throw new InvalidDataException("Preview argument placeholders are required.");
        var result = new List<string>();
        var token = new System.Text.StringBuilder();
        char quote = '\0'; var started = false;
        for (var index = 0; index < template.Length; index++)
        {
            var character = template[index];
            if (character == '\\' && quote != '\'')
            {
                var end = index;
                while (end < template.Length && template[end] == '\\') end++;
                var count = end - index;
                if (end < template.Length && template[end] == '"')
                {
                    // 双引号前的反斜杠按奇偶数保留，兼容既有 Windows 参数模板中的转义引号。
                    token.Append('\\', count / 2); started = true;
                    if (count % 2 != 0) { token.Append('"'); index = end; continue; }
                    index = end; character = '"';
                }
                else { token.Append('\\', count); started = true; index = end - 1; continue; }
            }
            if (character == '"' || character == '\'' && (quote == '\'' || !started))
            {
                if (quote == '\0') { quote = character; started = true; continue; }
                if (quote == character) { quote = '\0'; continue; }
            }
            if (quote == '\0' && char.IsWhiteSpace(character))
            {
                if (started) { result.Add(Expand(token.ToString())); token.Clear(); started = false; }
            }
            else { token.Append(character); started = true; }
        }
        if (quote != '\0') throw new InvalidDataException("Preview argument template has an unclosed quote.");
        if (started) result.Add(Expand(token.ToString()));
        return result;

        string Expand(string value) => value.Replace("{inputPath}", inputPath, StringComparison.Ordinal).Replace("{outputPath}", outputPath, StringComparison.Ordinal);
    }

    internal static string InputFileName(string mimeType) => "source" + (mimeType.Split(';', 2)[0].Trim().ToLowerInvariant() switch
    {
        "application/msword" => ".doc",
        "application/vnd.ms-excel" => ".xls",
        "application/vnd.ms-powerpoint" => ".ppt",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
        _ => ".bin",
    });
}
