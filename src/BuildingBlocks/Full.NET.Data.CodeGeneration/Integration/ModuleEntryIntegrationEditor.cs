using System.Collections.ObjectModel;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存模块入口源码改写的纯内存结果；失败结果不得用于写盘。
/// </summary>
public sealed class ModuleEntryIntegrationEditResult
{
    private ModuleEntryIntegrationEditResult(
        bool succeeded,
        bool changed,
        string desiredContent,
        IEnumerable<string> diagnostics)
    {
        Succeeded = succeeded;
        Changed = changed;
        DesiredContent = desiredContent;
        Diagnostics = new ReadOnlyCollection<string>(
            diagnostics.ToArray());
    }

    /// <summary>编辑是否成功；失败结果不得用于写盘。</summary>
    public bool Succeeded { get; }

    /// <summary>是否实际产生变更；幂等编辑时为 false。</summary>
    public bool Changed { get; }

    /// <summary>期望写入的候选内容；失败时回退为原始内容。</summary>
    public string DesiredContent { get; }

    /// <summary>失败时的诊断信息；成功时为空。</summary>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>构造成功结果；按原文与候选内容是否一致判定是否变更。</summary>
    public static ModuleEntryIntegrationEditResult Success(
        string originalContent,
        string desiredContent) =>
        new(
            succeeded: true,
            changed: !StringComparer.Ordinal.Equals(
                originalContent,
                desiredContent),
            desiredContent,
            diagnostics: []);

    /// <summary>构造失败结果，DesiredContent 回退为原始内容。</summary>
    public static ModuleEntryIntegrationEditResult Failure(
        string originalContent,
        params string[] diagnostics) =>
        new(
            succeeded: false,
            changed: false,
            originalContent,
            diagnostics);
}

/// <summary>
/// 只对结构可证明安全的模块入口添加生成特性聚合调用。
/// </summary>
public static class ModuleEntryIntegrationEditor
{
    private const string AddServicesMethod = "AddServices";
    private const string AddServicesParameterType = "IServiceCollection";
    private const string AddServicesInvocation =
        "AddFullNetGeneratedModuleFeatures";
    private const string MapEndpointsMethod = "MapEndpoints";
    private const string MapEndpointsParameterType = "IEndpointRouteBuilder";
    private const string MapEndpointsInvocation =
        "MapFullNetGeneratedModuleFeatures";

    /// <summary>
    /// 通过轻量词法分析向结构可证明的 AddServices/MapEndpoints 方法追加聚合调用。
    /// </summary>
    /// <param name="source">模块入口的原始 C# 源码</param>
    /// <param name="rootNamespace">模块根命名空间，用于校验文件作用域 namespace</param>
    /// <returns>包含期望内容与诊断的编辑结果，非标准形态 fail-closed</returns>
    public static ModuleEntryIntegrationEditResult Edit(
        string source,
        string rootNamespace)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootNamespace);

        var tokens = Tokenize(source);
        if (!HasFileScopedNamespace(tokens, rootNamespace))
        {
            return ModuleEntryIntegrationEditResult.Failure(
                source,
                $"模块入口必须使用文件作用域命名空间 {rootNamespace}。");
        }

        var addServices = FindMethod(
            tokens,
            AddServicesMethod,
            AddServicesParameterType);
        var addServicesFailure = ValidateMethod(
            addServices,
            AddServicesMethod);
        if (addServicesFailure is not null)
        {
            return ModuleEntryIntegrationEditResult.Failure(
                source,
                addServicesFailure);
        }

        var mapEndpoints = FindMethod(
            tokens,
            MapEndpointsMethod,
            MapEndpointsParameterType);
        var mapEndpointsFailure = ValidateMethod(
            mapEndpoints,
            MapEndpointsMethod);
        if (mapEndpointsFailure is not null)
        {
            return ModuleEntryIntegrationEditResult.Failure(
                source,
                mapEndpointsFailure);
        }

        var addServicesMethod = addServices[0];
        var mapEndpointsMethod = mapEndpoints[0];
        var edits = new List<SourceInsertion>();
        try
        {
            AddInvocationIfMissing(
                source,
                tokens,
                addServicesMethod,
                AddServicesInvocation,
                edits);
            AddInvocationIfMissing(
                source,
                tokens,
                mapEndpointsMethod,
                MapEndpointsInvocation,
                edits);
        }
        catch (InvalidOperationException exception)
        {
            return ModuleEntryIntegrationEditResult.Failure(
                source,
                exception.Message);
        }

        var generatedNamespace = $"{rootNamespace}.Generated";
        if (!HasUsing(tokens, generatedNamespace))
        {
            edits.Add(CreateUsingInsertion(
                source,
                tokens,
                generatedNamespace));
        }

        var desiredContent = source;
        foreach (var edit in edits.OrderByDescending(
                     edit => edit.Position))
        {
            desiredContent = desiredContent.Insert(
                edit.Position,
                edit.Content);
        }

        return ModuleEntryIntegrationEditResult.Success(
            source,
            desiredContent);
    }

    private static string? ValidateMethod(
        IReadOnlyList<MethodShape> methods,
        string methodName)
    {
        if (methods.Count != 1)
        {
            return $"{methodName} 必须且只能存在一个可验证声明。";
        }

        if (string.IsNullOrEmpty(methods[0].ParameterName))
        {
            return $"{methodName} 缺少预期的框架参数。";
        }

        return methods[0].BodyStartTokenIndex < 0
               || methods[0].BodyEndTokenIndex < 0
            ? $"{methodName} 必须使用可验证的块体方法。"
            : null;
    }

    private static void AddInvocationIfMissing(
        string source,
        IReadOnlyList<SourceToken> tokens,
        MethodShape method,
        string invocationName,
        ICollection<SourceInsertion> edits)
    {
        if (HasInvocation(
                tokens,
                method,
                invocationName))
        {
            return;
        }

        var openingBrace = tokens[method.BodyStartTokenIndex];
        var lineStart = source.LastIndexOf(
                '\n',
                Math.Max(0, openingBrace.Start - 1))
            + 1;
        var braceIndent = source[lineStart..openingBrace.Start];
        if (braceIndent.Any(character =>
                character is not (' ' or '\t')))
        {
            throw new InvalidOperationException(
                "块体左大括号必须独占一行。");
        }

        var lineEnd = source.IndexOf('\n', openingBrace.End);
        var remainderEnd = lineEnd < 0 ? source.Length : lineEnd;
        if (source[openingBrace.End..remainderEnd].Any(
                character => !char.IsWhiteSpace(character)))
        {
            throw new InvalidOperationException(
                "块体左大括号后不得包含其他代码。");
        }

        var newline = DetectNewline(source);
        edits.Add(new SourceInsertion(
            openingBrace.End,
            $"{newline}{braceIndent}    {method.ParameterName}."
            + $"{invocationName}();"));
    }

    private static bool HasInvocation(
        IReadOnlyList<SourceToken> tokens,
        MethodShape method,
        string invocationName)
    {
        for (var index = method.BodyStartTokenIndex + 1;
             index + 5 < method.BodyEndTokenIndex;
             index++)
        {
            if (tokens[index].Text == method.ParameterName
                && tokens[index + 1].Text == "."
                && tokens[index + 2].Text == invocationName
                && tokens[index + 3].Text == "("
                && tokens[index + 4].Text == ")"
                && tokens[index + 5].Text == ";")
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<MethodShape> FindMethod(
        IReadOnlyList<SourceToken> tokens,
        string methodName,
        string parameterType)
    {
        var methods = new List<MethodShape>();
        for (var index = 1; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].Text != methodName
                || tokens[index - 1].Text != "void"
                || tokens[index + 1].Text != "(")
            {
                continue;
            }

            var parametersEnd = FindMatching(
                tokens,
                index + 1,
                "(",
                ")");
            if (parametersEnd < 0)
            {
                methods.Add(new MethodShape("", -1, -1));
                continue;
            }

            var parameterName = FindParameterName(
                tokens,
                index + 2,
                parametersEnd,
                parameterType);
            var bodyStart = parametersEnd + 1 < tokens.Count
                && tokens[parametersEnd + 1].Text == "{"
                    ? parametersEnd + 1
                    : -1;
            var bodyEnd = bodyStart >= 0
                ? FindMatching(tokens, bodyStart, "{", "}")
                : -1;
            methods.Add(new MethodShape(
                parameterName,
                bodyStart,
                bodyEnd));
        }

        return methods;
    }

    private static string FindParameterName(
        IReadOnlyList<SourceToken> tokens,
        int start,
        int end,
        string parameterType)
    {
        for (var index = start; index + 1 < end; index++)
        {
            if (tokens[index].Text == parameterType
                && tokens[index + 1].Kind == SourceTokenKind.Identifier)
            {
                return tokens[index + 1].Text;
            }
        }

        return "";
    }

    private static int FindMatching(
        IReadOnlyList<SourceToken> tokens,
        int start,
        string opening,
        string closing)
    {
        var depth = 0;
        for (var index = start; index < tokens.Count; index++)
        {
            if (tokens[index].Text == opening)
            {
                depth++;
            }
            else if (tokens[index].Text == closing
                     && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasFileScopedNamespace(
        IReadOnlyList<SourceToken> tokens,
        string rootNamespace)
    {
        var expected = NamespaceTokens(rootNamespace);
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Text == "namespace"
                && Matches(tokens, index + 1, expected)
                && index + expected.Count + 1 < tokens.Count
                && tokens[index + expected.Count + 1].Text == ";")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUsing(
        IReadOnlyList<SourceToken> tokens,
        string namespaceName)
    {
        var expected = NamespaceTokens(namespaceName);
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Text == "using"
                && Matches(tokens, index + 1, expected)
                && index + expected.Count + 1 < tokens.Count
                && tokens[index + expected.Count + 1].Text == ";")
            {
                return true;
            }
        }

        return false;
    }

    private static SourceInsertion CreateUsingInsertion(
        string source,
        IReadOnlyList<SourceToken> tokens,
        string namespaceName)
    {
        var namespaceToken = tokens.First(token =>
            token.Text == "namespace");
        var lastUsingSemicolon = -1;
        for (var index = 0;
             index < tokens.Count
             && tokens[index].Start < namespaceToken.Start;
             index++)
        {
            if (tokens[index].Text != "using")
            {
                continue;
            }

            for (var cursor = index + 1;
                 cursor < tokens.Count
                 && tokens[cursor].Start < namespaceToken.Start;
                 cursor++)
            {
                if (tokens[cursor].Text == ";")
                {
                    lastUsingSemicolon = tokens[cursor].End;
                    break;
                }
            }
        }

        var newline = DetectNewline(source);
        return lastUsingSemicolon >= 0
            ? new SourceInsertion(
                lastUsingSemicolon,
                $"{newline}using {namespaceName};")
            : new SourceInsertion(
                namespaceToken.Start,
                $"using {namespaceName};{newline}{newline}");
    }

    private static bool Matches(
        IReadOnlyList<SourceToken> tokens,
        int start,
        IReadOnlyList<string> expected)
    {
        if (start + expected.Count > tokens.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (tokens[start + index].Text != expected[index])
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> NamespaceTokens(
        string namespaceName) =>
        namespaceName
            .Split('.')
            .SelectMany((part, index) =>
                index == 0 ? [part] : new[] { ".", part })
            .ToArray();

    private static string DetectNewline(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

    private static IReadOnlyList<SourceToken> Tokenize(string source)
    {
        var tokens = new List<SourceToken>();
        for (var index = 0; index < source.Length;)
        {
            if (char.IsWhiteSpace(source[index]))
            {
                index++;
                continue;
            }

            if (index + 1 < source.Length
                && source[index] == '/'
                && source[index + 1] == '/')
            {
                index = SkipLineComment(source, index + 2);
                continue;
            }

            if (index + 1 < source.Length
                && source[index] == '/'
                && source[index + 1] == '*')
            {
                index = SkipBlockComment(source, index + 2);
                continue;
            }

            if (source[index] == '"'
                || source[index] == '\''
                || IsStringPrefix(source, index))
            {
                index = SkipLiteral(source, index);
                continue;
            }

            if (char.IsLetter(source[index])
                || source[index] == '_')
            {
                var start = index++;
                while (index < source.Length
                       && (char.IsLetterOrDigit(source[index])
                           || source[index] == '_'))
                {
                    index++;
                }

                tokens.Add(new SourceToken(
                    source[start..index],
                    start,
                    index,
                    SourceTokenKind.Identifier));
                continue;
            }

            tokens.Add(new SourceToken(
                source[index].ToString(),
                index,
                index + 1,
                SourceTokenKind.Punctuation));
            index++;
        }

        return tokens;
    }

    private static bool IsStringPrefix(string source, int index) =>
        (source[index] is '$' or '@')
        && index + 1 < source.Length
        && (source[index + 1] == '"'
            || (source[index + 1] is '$' or '@'
                && index + 2 < source.Length
                && source[index + 2] == '"'));

    private static int SkipLiteral(string source, int start)
    {
        var quote = source[start];
        var verbatim = false;
        var index = start;
        if (quote is '$' or '@')
        {
            verbatim = quote == '@';
            index++;
            if (source[index] is '$' or '@')
            {
                verbatim |= source[index] == '@';
                index++;
            }

            quote = source[index];
        }

        if (quote == '"'
            && index + 2 < source.Length
            && source[index + 1] == '"'
            && source[index + 2] == '"')
        {
            var delimiterLength = 3;
            while (index + delimiterLength < source.Length
                   && source[index + delimiterLength] == '"')
            {
                delimiterLength++;
            }

            var delimiter = new string('"', delimiterLength);
            var end = source.IndexOf(
                delimiter,
                index + delimiterLength,
                StringComparison.Ordinal);
            return end < 0
                ? source.Length
                : end + delimiterLength;
        }

        index++;
        while (index < source.Length)
        {
            if (source[index] == quote)
            {
                if (verbatim
                    && index + 1 < source.Length
                    && source[index + 1] == quote)
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            if (!verbatim
                && source[index] == '\\'
                && index + 1 < source.Length)
            {
                index += 2;
                continue;
            }

            index++;
        }

        return source.Length;
    }

    private static int SkipLineComment(string source, int index)
    {
        while (index < source.Length && source[index] != '\n')
        {
            index++;
        }

        return index;
    }

    private static int SkipBlockComment(string source, int index)
    {
        var end = source.IndexOf(
            "*/",
            index,
            StringComparison.Ordinal);
        return end < 0 ? source.Length : end + 2;
    }

    private sealed record MethodShape(
        string ParameterName,
        int BodyStartTokenIndex,
        int BodyEndTokenIndex);

    private sealed record SourceInsertion(
        int Position,
        string Content);

    private sealed record SourceToken(
        string Text,
        int Start,
        int End,
        SourceTokenKind Kind);

    private enum SourceTokenKind
    {
        Identifier = 1,
        Punctuation = 2,
    }
}
