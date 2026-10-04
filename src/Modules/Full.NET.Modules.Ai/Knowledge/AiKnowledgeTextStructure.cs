namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>连续原文结构区间；表头是来源引用，RowsStart 不参与任意来源权限判断。</summary>
internal sealed record AiKnowledgeTextBlock(int Offset, int End, AiKnowledgeTextBlockKind Kind,
    IReadOnlyList<AiKnowledgeSourceContext> Context, int RowsStart = -1, bool HasRows = false);

/// <summary>纯位置扫描器；仅识别顶层 ATX、代码与管道表格子集，不是渲染器或完整 GFM 实现。</summary>
internal static class AiKnowledgeTextStructure
{
    private readonly record struct Line(int Start, int ContentEnd, int End);

    /// <summary>调用方先验证完整 UTF-16 原文；行数、块数及上下文有界，失败不提供部分结构。</summary>
    internal static IReadOnlyList<AiKnowledgeTextBlock>? Scan(string text, AiKnowledgeTextFormat format,
        CancellationToken token, out AiKnowledgeChunkFailure failure)
    {
        failure = AiKnowledgeChunkFailure.None;
        var lines = new List<Line>();
        for (var position = 0; position < text.Length;)
        {
            token.ThrowIfCancellationRequested();
            if (lines.Count >= AiKnowledgeStructuredChunker.MaxLines) { failure = AiKnowledgeChunkFailure.StructureLimitExceeded; return null; }
            var start = position;
            while (position < text.Length && text[position] is not ('\r' or '\n'))
            {
                if (position % 4096 == 0) token.ThrowIfCancellationRequested();
                position++;
            }
            var contentEnd = position;
            if (position < text.Length && text[position++] == '\r' && position < text.Length && text[position] == '\n') position++;
            lines.Add(new(start, contentEnd, position));
        }
        var blocks = new List<AiKnowledgeTextBlock>();
        var headings = new List<AiKnowledgeSourceContext>();
        var index = 0;
        while (index < lines.Count)
        {
            token.ThrowIfCancellationRequested();
            if (blocks.Count >= AiKnowledgeStructuredChunker.MaxBlocks) { failure = AiKnowledgeChunkFailure.StructureLimitExceeded; return null; }
            var start = lines[index].Start;
            var kind = AiKnowledgeTextBlockKind.Paragraph;
            var rowsStart = -1;
            var hasRows = false;
            AiKnowledgeSourceContext? header = null;
            // 标题与其首段合并；每次标题转换单独保存快照，不能让较早的块引用未来标题。
            if (format == AiKnowledgeTextFormat.Markdown)
            {
                if (index < lines.Count && Heading(Content(text, lines[index])) is var level && level > 0)
                {
                    token.ThrowIfCancellationRequested();
                    headings.RemoveAll(heading => heading.Level >= level);
                    headings.Add(new(AiKnowledgeContextKind.Heading, lines[index].Start, lines[index].End - lines[index].Start, level));
                    index++;
                    while (index < lines.Count && Blank(Content(text, lines[index]))) { token.ThrowIfCancellationRequested(); index++; }
                }
            }
            // 紧接另一个标题或表格/代码时先保存标题块，避免上下文归属跨过结构转换。
            var startsSpecial = index < lines.Count && format == AiKnowledgeTextFormat.Markdown
                && (Heading(Content(text, lines[index])) > 0 || Fence(Content(text, lines[index]), out _, out _)
                    || Indented(Content(text, lines[index])) || Table(text, lines, index));
            if (index < lines.Count && !(lines[index].Start > start && startsSpecial))
            {
                if (format == AiKnowledgeTextFormat.Markdown && Fence(Content(text, lines[index]), out var marker, out var width))
                {
                    kind = AiKnowledgeTextBlockKind.Code;
                    index++;
                    while (index < lines.Count)
                    {
                        token.ThrowIfCancellationRequested();
                        var closing = ClosingFence(Content(text, lines[index]), marker, width);
                        index++;
                        if (closing) break;
                    }
                }
                else if (format == AiKnowledgeTextFormat.Markdown && Indented(Content(text, lines[index])))
                {
                    kind = AiKnowledgeTextBlockKind.Code;
                    while (index < lines.Count && (Indented(Content(text, lines[index])) || Blank(Content(text, lines[index]))))
                    { token.ThrowIfCancellationRequested(); index++; }
                }
                else if (format == AiKnowledgeTextFormat.Markdown && Table(text, lines, index))
                {
                    kind = AiKnowledgeTextBlockKind.Table;
                    header = new(AiKnowledgeContextKind.TableHeader, lines[index].Start, lines[index + 1].End - lines[index].Start);
                    index += 2;
                    rowsStart = lines[index - 1].End;
                    while (index < lines.Count && !Blank(Content(text, lines[index])) && Heading(Content(text, lines[index])) == 0
                        && !Fence(Content(text, lines[index]), out _, out _) && !Indented(Content(text, lines[index]))
                        && CellCount(Content(text, lines[index]), false) > 0)
                    { token.ThrowIfCancellationRequested(); hasRows = true; index++; }
                }
                else
                {
                    // 普通段落只在空行或所支持结构起点切分；列表、HTML、链接等始终保留原文。
                    var first = true;
                    while (index < lines.Count && !Blank(Content(text, lines[index])))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!first && format == AiKnowledgeTextFormat.Markdown
                            && (Heading(Content(text, lines[index])) > 0 || Fence(Content(text, lines[index]), out _, out _) || Table(text, lines, index))) break;
                        index++;
                        first = false;
                    }
                }
                while (index < lines.Count && Blank(Content(text, lines[index]))) { token.ThrowIfCancellationRequested(); index++; }
            }
            var end = index == lines.Count ? text.Length : lines[index].Start;
            if (end <= start) throw new InvalidOperationException("结构扫描必须覆盖并前进。");
            var context = header is null ? headings.ToArray() : [.. headings, header];
            if (context.Sum(value => value.Length) > AiKnowledgeStructuredChunker.MaxContextCharacters)
            { failure = AiKnowledgeChunkFailure.ContextTooLarge; return null; }
            blocks.Add(new(start, end, kind, Array.AsReadOnly(context), rowsStart, hasRows));
        }
        token.ThrowIfCancellationRequested();
        return Array.AsReadOnly(blocks.ToArray());
    }

    private static ReadOnlySpan<char> Content(string text, Line line) => text.AsSpan(line.Start, line.ContentEnd - line.Start);
    private static bool Blank(ReadOnlySpan<char> line) => line.Trim(" \t").IsEmpty;
    private static bool Indented(ReadOnlySpan<char> line) => line.StartsWith("    ") || (!line.IsEmpty && line[0] == '\t');
    private static ReadOnlySpan<char> TopLevel(ReadOnlySpan<char> line)
    {
        var spaces = 0;
        while (spaces < line.Length && line[spaces] == ' ') spaces++;
        return spaces > 3 || (!line.IsEmpty && line[0] == '\t') ? ReadOnlySpan<char>.Empty : line[spaces..];
    }
    private static int Heading(ReadOnlySpan<char> line)
    {
        line = TopLevel(line);
        var level = 0;
        while (level < line.Length && line[level] == '#') level++;
        return level is >= 1 and <= 6 && (level == line.Length || line[level] is ' ' or '\t') ? level : 0;
    }
    private static bool Fence(ReadOnlySpan<char> line, out char marker, out int width)
    {
        line = TopLevel(line);
        marker = line.IsEmpty ? '\0' : line[0];
        width = 0;
        if (marker is not ('`' or '~')) return false;
        while (width < line.Length && line[width] == marker) width++;
        return width >= 3 && (marker != '`' || !line[width..].Contains('`'));
    }
    private static bool ClosingFence(ReadOnlySpan<char> line, char marker, int width)
    {
        line = TopLevel(line);
        var count = 0;
        while (count < line.Length && line[count] == marker) count++;
        return count >= width && line[count..].Trim(" \t").IsEmpty;
    }
    private static bool Table(string text, List<Line> lines, int index)
    {
        if (index + 1 >= lines.Count) return false;
        var count = CellCount(TopLevel(Content(text, lines[index])), false);
        return count > 0 && count == CellCount(TopLevel(Content(text, lines[index + 1])), true);
    }
    private static int CellCount(ReadOnlySpan<char> line, bool delimiter)
    {
        line = line.Trim(" \t");
        var hasPipe = false;
        var count = 0;
        var start = 0;
        // 转义字符保持原文；只用未转义管道分列，允许无首尾管道和不等长数据行。
        for (var position = 0; position <= line.Length; position++)
        {
            // 最后一个反斜杠没有可转义后继；仍需走到虚拟行尾结算最后一列。
            if (position + 1 < line.Length && line[position] == '\\') { position++; continue; }
            if (position < line.Length && line[position] != '|') continue;
            if (position < line.Length) hasPipe = true;
            var cell = line[start..position].Trim(" \t");
            var edgeEmpty = cell.IsEmpty && (position == 0 || position == line.Length);
            if (!edgeEmpty)
            {
                if (delimiter)
                {
                    if (!cell.IsEmpty && cell[0] == ':') cell = cell[1..];
                    if (!cell.IsEmpty && cell[^1] == ':') cell = cell[..^1];
                    if (cell.IsEmpty || cell.ContainsAnyExcept('-')) return -1;
                }
                count++;
            }
            start = position + 1;
        }
        return hasPipe ? count : 0;
    }
}
