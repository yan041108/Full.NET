namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 只向标准模块目录追加一个精确模块构造，不猜测自定义目录实现。
/// </summary>
public static class CompositionCatalogEditor
{
    private const string CreateModulesSignature =
        "private static IReadOnlyList<IFullNetModule> CreateModules() =>";

    public static CompositionIntegrationEditResult Edit(
        string source,
        string rootNamespace,
        string moduleName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

        if (source.Contains("\"\"\"", StringComparison.Ordinal)
            || source.Contains("@\"", StringComparison.Ordinal))
        {
            return Failure(source);
        }

        var lines = SplitLines(source);
        var sanitized = Sanitize(lines);
        var signatures = sanitized
            .Select((line, index) => new { line, index })
            .Where(item =>
                item.line.Trim() == CreateModulesSignature)
            .Select(item => item.index)
            .ToArray();
        if (signatures.Length != 1)
        {
            return Failure(source);
        }

        var opening = NextSignificantLine(
            sanitized,
            signatures[0] + 1);
        if (opening < 0 || sanitized[opening].Trim() != "[")
        {
            return Failure(source);
        }

        var closingCandidates = sanitized
            .Select((line, index) => new { line, index })
            .Where(item =>
                item.index > opening
                && item.line.Trim() == "];")
            .Select(item => item.index)
            .ToArray();
        if (closingCandidates.Length != 1)
        {
            return Failure(source);
        }

        var closing = closingCandidates[0];
        var expectedConstruction = $"new {moduleName}Module(),";
        var hasConstruction = sanitized
            .Skip(opening + 1)
            .Take(closing - opening - 1)
            .Any(line =>
                line.Trim() == expectedConstruction);
        var expectedUsing = $"using {rootNamespace};";
        var hasUsing = sanitized
            .Take(signatures[0])
            .Any(line => line.Trim() == expectedUsing);
        if (hasConstruction && hasUsing)
        {
            return CompositionIntegrationEditResult.Success(
                source,
                source);
        }

        var newline = DetectNewline(source);
        var insertions = new List<SourceInsertion>();
        if (!hasConstruction)
        {
            var closingIndent = LeadingWhitespace(
                lines[closing].Content);
            insertions.Add(new SourceInsertion(
                lines[closing].Start,
                $"{closingIndent}    {expectedConstruction}{newline}"));
        }

        if (!hasUsing)
        {
            var usingLines = sanitized
                .Take(signatures[0])
                .Select((line, index) => new { line, index })
                .Where(item =>
                    item.line.TrimStart().StartsWith(
                        "using ",
                        StringComparison.Ordinal)
                    && item.line.TrimEnd().EndsWith(
                        ';'))
                .Select(item => item.index)
                .ToArray();
            if (usingLines.Length == 0)
            {
                return Failure(source);
            }

            var lastUsing = usingLines[^1];
            insertions.Add(new SourceInsertion(
                lines[lastUsing].EndWithNewline,
                $"{expectedUsing}{newline}"));
        }

        var desired = source;
        foreach (var insertion in insertions.OrderByDescending(
                     insertion => insertion.Position))
        {
            desired = desired.Insert(
                insertion.Position,
                insertion.Content);
        }

        return CompositionIntegrationEditResult.Success(
            source,
            desired);
    }

    private static CompositionIntegrationEditResult Failure(
        string source) =>
        CompositionIntegrationEditResult.Failure(
            source,
            "Composition Catalog 必须包含唯一的标准 CreateModules() => [ ... ];。");

    private static int NextSignificantLine(
        IReadOnlyList<string> lines,
        int start)
    {
        for (var index = start; index < lines.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> Sanitize(
        IReadOnlyList<SourceLine> lines)
    {
        var sanitized = new List<string>(lines.Count);
        var inBlockComment = false;
        foreach (var line in lines)
        {
            var result = new char[line.Content.Length];
            Array.Fill(result, ' ');
            var inString = false;
            var inCharacter = false;
            for (var index = 0; index < line.Content.Length; index++)
            {
                if (inBlockComment)
                {
                    if (index + 1 < line.Content.Length
                        && line.Content[index] == '*'
                        && line.Content[index + 1] == '/')
                    {
                        inBlockComment = false;
                        index++;
                    }

                    continue;
                }

                if (inString)
                {
                    if (line.Content[index] == '\\')
                    {
                        index++;
                    }
                    else if (line.Content[index] == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (inCharacter)
                {
                    if (line.Content[index] == '\\')
                    {
                        index++;
                    }
                    else if (line.Content[index] == '\'')
                    {
                        inCharacter = false;
                    }

                    continue;
                }

                if (index + 1 < line.Content.Length
                    && line.Content[index] == '/'
                    && line.Content[index + 1] == '/')
                {
                    break;
                }

                if (index + 1 < line.Content.Length
                    && line.Content[index] == '/'
                    && line.Content[index + 1] == '*')
                {
                    inBlockComment = true;
                    index++;
                    continue;
                }

                if (line.Content[index] == '"')
                {
                    inString = true;
                    continue;
                }

                if (line.Content[index] == '\'')
                {
                    inCharacter = true;
                    continue;
                }

                result[index] = line.Content[index];
            }

            sanitized.Add(new string(result));
        }

        return sanitized;
    }

    private static IReadOnlyList<SourceLine> SplitLines(string source)
    {
        var lines = new List<SourceLine>();
        var start = 0;
        while (start < source.Length)
        {
            var end = source.IndexOf('\n', start);
            if (end < 0)
            {
                lines.Add(new SourceLine(
                    start,
                    source.Length,
                    source[start..].TrimEnd('\r')));
                return lines;
            }

            lines.Add(new SourceLine(
                start,
                end + 1,
                source[start..end].TrimEnd('\r')));
            start = end + 1;
        }

        return lines;
    }

    private static string LeadingWhitespace(string line) =>
        line[..line.TakeWhile(character =>
            character is ' ' or '\t').Count()];

    private static string DetectNewline(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

    private sealed record SourceLine(
        int Start,
        int EndWithNewline,
        string Content);

    private sealed record SourceInsertion(
        int Position,
        string Content);
}
