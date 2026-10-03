using static Full.NET.Data.CodeGeneration.Integration.ModuleEntryIntegrationEditor;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 向唯一标准授权集合插入生成块；部分接入、人工漂移或不明确的结构保持原文失败关闭。
/// </summary>
internal static class AuthorizationContributorIntegrationEditor
{
    private static readonly (string Type, string Property, string Section)[] Collections =
    [
        ("PermissionDefinition", "Permissions", "permissions"),
        ("NavigationDefinition", "Navigation", "navigation"),
        ("AuthorizationActionDefinition", "Actions", "actions"),
    ];

    /// <summary>校验三个生成块及其归属，保持手写元素，仅完整且相同的接入可幂等跳过。</summary>
    public static ClientRouteIntegrationEditResult Edit(string source, string contributorPath, string fragment)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(contributorPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragment);
        var sourceTokens = Tokenize(source, includeLineComments: true);
        var tokens = sourceTokens.Where(token => token.Kind != SourceTokenKind.LineComment).ToArray();
        var comments = sourceTokens.Where(token => token.Kind == SourceTokenKind.LineComment).ToArray();
        var fragmentComments = Tokenize(fragment, includeLineComments: true)
            .Where(token => token.Kind == SourceTokenKind.LineComment).ToArray();
        if (tokens.Any(token => token.Text == "#")
            || tokens.Count(token => token.Text == "class") != 1
            || fragmentComments.Length != 6)
        {
            return Failure(source, "授权接入只支持唯一类型中的标准集合与完整生成块。");
        }

        var classIndex = Array.FindIndex(tokens, token => token.Text == "class");
        var classOpen = Array.FindIndex(tokens, classIndex, token => token.Text == "{");
        var classClose = FindEnd(tokens, classOpen, "{", "}");
        if (classClose < 0) return Failure(source, "授权类型边界不完整。");

        var edits = new List<(int Position, string Content)>();
        var existingBlocks = 0;
        string? identity = null;
        for (var sectionIndex = 0; sectionIndex < Collections.Length; sectionIndex++)
        {
            var (type, property, section) = Collections[sectionIndex];
            var expected = new[] { "IReadOnlyCollection", "<", type, ">", property, "{", "get", ";", "}", "=", "[" };
            var declarations = new[] { "IReadOnlyCollection", "<", type, ">", property };
            var candidates = Enumerable.Range(0, tokens.Length)
                .Where(index => Matches(tokens, index, expected)).ToArray();
            if (candidates.Length != 1
                || Enumerable.Range(0, tokens.Length).Count(index => Matches(tokens, index, declarations)) != 1)
            {
                return Failure(source, $"授权集合 {property} 必须是唯一标准自动属性集合表达式。");
            }

            var propertyIndex = candidates[0];
            var depth = 0;
            for (var index = classOpen; index < propertyIndex; index++)
            {
                depth += tokens[index].Text == "{" ? 1 : tokens[index].Text == "}" ? -1 : 0;
            }
            var open = propertyIndex + expected.Length - 1;
            var close = FindEnd(tokens, open, "[", "]");
            if (propertyIndex <= classOpen || depth != 1 || close < 0
                || close >= classClose || close + 1 >= tokens.Length || tokens[close + 1].Text != ";")
            {
                return Failure(source, $"授权集合 {property} 不在可证明的类型边界内。");
            }

            var begin = fragmentComments[sectionIndex * 2];
            var end = fragmentComments[sectionIndex * 2 + 1];
            const string prefix = "// <fullnet-generated ";
            if (!begin.Text.StartsWith(prefix, StringComparison.Ordinal)
                || !begin.Text.EndsWith($" {section}>", StringComparison.Ordinal))
            {
                return Failure(source, "授权生成块标记不完整或顺序无效。");
            }
            var blockIdentity = begin.Text[prefix.Length..^(section.Length + 2)];
            identity ??= blockIdentity;
            if (identity != blockIdentity || end.Text != $"// </fullnet-generated {identity} {section}>")
            {
                return Failure(source, "授权生成块归属不一致。");
            }
            var block = fragment[begin.Start..end.End].TrimEnd('\r', '\n');
            var beginMatches = comments.Where(token => token.Text.Trim() == begin.Text).ToArray();
            var endMatches = comments.Where(token => token.Text.Trim() == end.Text).ToArray();
            if (beginMatches.Length != 0 || endMatches.Length != 0)
            {
                if (beginMatches.Length != 1 || endMatches.Length != 1
                    || beginMatches[0].Start <= tokens[open].End
                    || endMatches[0].End > tokens[close].Start
                    || endMatches[0].Start <= beginMatches[0].Start
                    || !IsElementBoundary(tokens, open, beginMatches[0].Start)
                    || !IsElementBoundary(tokens, open, endMatches[0].Start)
                    || Normalize(source[beginMatches[0].Start..endMatches[0].End]) != Normalize(block))
                {
                    return Failure(source, $"授权集合 {property} 的生成块缺失、重复、越界或已人工修改。");
                }
                existingBlocks++;
                continue;
            }

            // 给没有尾逗号的手写末项补分隔符，再插入生成块；不重写其他文本。
            if (close > open + 1 && tokens[close - 1].Text != ",")
            {
                edits.Add((tokens[close - 1].End, ","));
            }
            var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var indentedBlock = string.Join(newline, block.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n').Select(line => "        " + line));
            edits.Add((tokens[close].Start, newline + indentedBlock + newline + "    "));
        }

        if (existingBlocks is > 0 and < 3)
        {
            return Failure(source, "授权生成块仅部分存在，拒绝自动补写以免覆盖人工接入。");
        }
        var desired = source;
        // 手写末项紧贴 ] 时，补逗号与生成块位置相同；逆序插入才能让逗号留在生成块之前。
        foreach (var edit in edits.Select((edit, index) => (edit.Position, edit.Content, Index: index))
                     .OrderByDescending(edit => edit.Position).ThenByDescending(edit => edit.Index))
        {
            desired = desired.Insert(edit.Position, edit.Content);
        }
        return ClientRouteIntegrationEditResult.Success(source, desired);
    }

    private static string Normalize(string block) => string.Join("\n",
        block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.Trim())).Trim();

    // 标记必须在直属元素之间，不能藏进被丢弃的嵌套集合、lambda 或条件表达式。
    private static bool IsElementBoundary(SourceToken[] tokens, int open, int position)
    {
        var depth = 0;
        var previous = tokens[open];
        for (var index = open + 1; index < tokens.Length && tokens[index].Start < position; index++)
        {
            var token = tokens[index];
            depth += token.Text is "[" or "(" or "{" ? 1
                : token.Text is "]" or ")" or "}" ? -1 : 0;
            if (depth < 0) return false;
            previous = token;
        }
        return depth == 0 && previous.Text is "[" or ",";
    }

    private static bool Matches(SourceToken[] tokens, int start, string[] expected) =>
        start + expected.Length <= tokens.Length
        && expected.Select((text, offset) => tokens[start + offset].Text == text).All(matches => matches);

    private static int FindEnd(SourceToken[] tokens, int open, string begin, string end)
    {
        if (open < 0) return -1;
        var depth = 0;
        for (var index = open; index < tokens.Length; index++)
        {
            if (tokens[index].Text == begin) depth++;
            if (tokens[index].Text == end && --depth == 0) return index;
        }
        return -1;
    }

    private static ClientRouteIntegrationEditResult Failure(string source, string diagnostic) =>
        ClientRouteIntegrationEditResult.Failure(source, diagnostic);
}
