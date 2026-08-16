using System.Collections.ObjectModel;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存客户端路由纯内存编辑的候选文本与失败诊断。
/// </summary>
internal sealed class ClientRouteIntegrationEditResult
{
    private ClientRouteIntegrationEditResult(
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

    public bool Succeeded { get; }

    public bool Changed { get; }

    public string DesiredContent { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public static ClientRouteIntegrationEditResult Success(
        string originalContent,
        string desiredContent) =>
        new(
            succeeded: true,
            changed: !StringComparer.Ordinal.Equals(
                originalContent,
                desiredContent),
            desiredContent,
            diagnostics: []);

    public static ClientRouteIntegrationEditResult Failure(
        string originalContent,
        string diagnostic) =>
        new(
            succeeded: false,
            changed: false,
            originalContent,
            [diagnostic]);
}

/// <summary>
/// 只向当前标准 Vue Router 静态数组增加显式本地 View 映射。
/// </summary>
internal static class VueRouteIntegrationEditor
{
    public static ClientRouteIntegrationEditResult Edit(
        string source,
        string routerPath,
        ModuleClientRouteTarget route)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(routerPath);
        ArgumentNullException.ThrowIfNull(route);

        if (source.Contains('`'))
        {
            return Failure(source);
        }

        var lines = ClientRouteText.SplitLines(source);
        var sanitized = ClientRouteText.RemoveComments(lines);
        var routeArrays = sanitized
            .Select((line, index) => new { line, index })
            .Where(item => item.line.Trim() == "routes: [")
            .Select(item => item.index)
            .ToArray();
        var statusAnchors = sanitized
            .Select((line, index) => new { line, index })
            .Where(item => item.line.TrimStart().StartsWith(
                "{ path: '/403',",
                StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();
        if (routeArrays.Length != 1
            || statusAnchors.Length != 1
            || statusAnchors[0] <= routeArrays[0])
        {
            return Failure(source);
        }

        var relativeImport = ClientRouteText.RelativeImport(
            routerPath,
            route.VueComponentPath);
        var nameNeedle = $"name: '{route.VueRouteName}'";
        var pathNeedle = $"path: '{route.RoutePath}'";
        var importNeedle = $"import('{relativeImport}')";
        var nameLines = sanitized
            .Select((line, index) => new { line, index })
            .Where(item => item.line.Contains(
                nameNeedle,
                StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();
        var pathLines = sanitized
            .Select((line, index) => new { line, index })
            .Where(item => item.line.Contains(
                pathNeedle,
                StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();
        if (nameLines.Length > 1 || pathLines.Length > 1)
        {
            return ClientRouteIntegrationEditResult.Failure(
                source,
                "Vue 路由包含重复的目标名称或路径。");
        }

        if (nameLines.Length == 1 || pathLines.Length == 1)
        {
            if (nameLines.Length == 1
                && pathLines.Length == 1
                && IsGeneratedRouteBlock(
                    sanitized,
                    nameLines[0],
                    nameNeedle,
                    pathNeedle,
                    importNeedle))
            {
                return ClientRouteIntegrationEditResult.Success(
                    source,
                    source);
            }

            return ClientRouteIntegrationEditResult.Failure(
                source,
                "Vue 路由名称或路径已被其他本地映射占用。");
        }

        var newline = ClientRouteText.DetectNewline(source);
        var anchor = lines[statusAnchors[0]];
        var indent = ClientRouteText.LeadingWhitespace(
            anchor.Content);
        var block =
            $"{indent}{{{newline}"
            + $"{indent}  name: '{route.VueRouteName}',{newline}"
            + $"{indent}  path: '{route.RoutePath}',{newline}"
            + $"{indent}  component: () => import('{relativeImport}'){newline}"
            + $"{indent}}},{newline}";
        return ClientRouteIntegrationEditResult.Success(
            source,
            source.Insert(anchor.Start, block));
    }

    private static bool IsGeneratedRouteBlock(
        IReadOnlyList<string> lines,
        int nameLine,
        string nameNeedle,
        string pathNeedle,
        string importNeedle)
    {
        var openingLine = nameLine - 1;
        return openingLine >= 0
            && openingLine + 4 < lines.Count
            && lines[openingLine].Trim() == "{"
            && lines[openingLine + 1].Trim() == $"{nameNeedle},"
            && lines[openingLine + 2].Trim() == $"{pathNeedle},"
            && lines[openingLine + 3].Trim()
                == $"component: () => {importNeedle}"
            && lines[openingLine + 4].Trim() == "},";
    }

    private static ClientRouteIntegrationEditResult Failure(
        string source) =>
        ClientRouteIntegrationEditResult.Failure(
            source,
            "Vue 路由必须包含唯一标准 routes 数组和 /403 状态路由锚点。");
}

/// <summary>
/// 只向当前标准 Layui controller Map 增加显式本地 controller 映射。
/// </summary>
internal static class LayuiRouteIntegrationEditor
{
    public static ClientRouteIntegrationEditResult Edit(
        string source,
        string routerPath,
        ModuleClientRouteTarget route)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(routerPath);
        ArgumentNullException.ThrowIfNull(route);

        if (source.Contains('`'))
        {
            return Failure(source);
        }

        var lines = ClientRouteText.SplitLines(source);
        var sanitized = ClientRouteText.RemoveComments(lines);
        var mapStarts = sanitized
            .Select((line, index) => new { line, index })
            .Where(item =>
                item.line.Trim() == "return new Map([")
            .Select(item => item.index)
            .ToArray();
        if (mapStarts.Length != 1)
        {
            return Failure(source);
        }

        var closing = Enumerable
            .Range(
                mapStarts[0] + 1,
                sanitized.Count - mapStarts[0] - 1)
            .FirstOrDefault(
                index => sanitized[index].Trim() == "]);",
                -1);
        if (closing < 0)
        {
            return Failure(source);
        }

        var relativeImport = ClientRouteText.RelativeImport(
            routerPath,
            route.LayuiControllerPath!);
        var routeNeedle =
            $"['{route.RoutePath}', defineController(";
        var routeLines = sanitized
            .Select((line, index) => new { line, index })
            .Where(item => item.index > mapStarts[0]
                && item.index < closing
                && item.line.Trim() == routeNeedle)
            .Select(item => item.index)
            .ToArray();
        if (routeLines.Length > 1)
        {
            return ClientRouteIntegrationEditResult.Failure(
                source,
                "Layui 路由包含重复的目标路径。");
        }

        if (routeLines.Length == 1)
        {
            if (IsGeneratedRouteBlock(
                    sanitized,
                    routeLines[0],
                    routeNeedle,
                    relativeImport,
                    route.LayuiControllerExport!))
            {
                return ClientRouteIntegrationEditResult.Success(
                    source,
                    source);
            }

            return ClientRouteIntegrationEditResult.Failure(
                source,
                "Layui 路由路径已被其他本地 controller 映射占用。");
        }

        var lastSignificant = closing - 1;
        while (lastSignificant > mapStarts[0]
               && string.IsNullOrWhiteSpace(
                   sanitized[lastSignificant]))
        {
            lastSignificant--;
        }

        var hasExistingEntry = lastSignificant > mapStarts[0];
        if (hasExistingEntry
            && sanitized[lastSignificant].Trim() is not (")]" or ")],"))
        {
            return Failure(source);
        }

        var newline = ClientRouteText.DetectNewline(source);
        var closingIndent = ClientRouteText.LeadingWhitespace(
            lines[closing].Content);
        var entryIndent = $"{closingIndent}  ";
        var blockText =
            $"{entryIndent}['{route.RoutePath}', defineController({newline}"
            + $"{entryIndent}  () => import('{relativeImport}'),{newline}"
            + $"{entryIndent}  '{route.LayuiControllerExport!}',{newline}"
            + $"{entryIndent}  root,{newline}"
            + $"{entryIndent}  sharedOptions{newline}"
            + $"{entryIndent})]{newline}";
        var desired = source.Insert(
            lines[closing].Start,
            blockText);
        if (hasExistingEntry
            && sanitized[lastSignificant].Trim() == ")]")
        {
            desired = desired.Insert(
                lines[lastSignificant].ContentEnd,
                ",");
        }

        return ClientRouteIntegrationEditResult.Success(
            source,
            desired);
    }

    private static bool IsGeneratedRouteBlock(
        IReadOnlyList<string> lines,
        int routeLine,
        string routeNeedle,
        string relativeImport,
        string controllerExport)
    {
        if (routeLine + 5 >= lines.Count
            || lines[routeLine].Trim() != routeNeedle
            || lines[routeLine + 1].Trim()
                != $"() => import('{relativeImport}'),"
            || lines[routeLine + 2].Trim()
                != $"'{controllerExport}',"
            || lines[routeLine + 3].Trim() != "root,"
            || lines[routeLine + 4].Trim() != "sharedOptions")
        {
            return false;
        }

        return lines[routeLine + 5].Trim() is ")]" or ")],";
    }

    private static ClientRouteIntegrationEditResult Failure(
        string source) =>
        ClientRouteIntegrationEditResult.Failure(
            source,
            "Layui 路由必须包含唯一标准 return new Map([ ... ]);。");
}

/// <summary>
/// 提供双端编辑器共享的注释清理、行定位与相对导入计算。
/// </summary>
internal static class ClientRouteText
{
    public static IReadOnlyList<string> RemoveComments(
        IReadOnlyList<ClientRouteSourceLine> lines)
    {
        var result = new List<string>(lines.Count);
        var inBlockComment = false;
        foreach (var line in lines)
        {
            var builder = new char[line.Content.Length];
            Array.Fill(builder, ' ');
            var quote = '\0';
            for (var index = 0; index < line.Content.Length; index++)
            {
                var current = line.Content[index];
                if (inBlockComment)
                {
                    if (current == '*'
                        && index + 1 < line.Content.Length
                        && line.Content[index + 1] == '/')
                    {
                        inBlockComment = false;
                        index++;
                    }

                    continue;
                }

                if (quote != '\0')
                {
                    builder[index] = current;
                    if (current == '\\'
                        && index + 1 < line.Content.Length)
                    {
                        index++;
                        builder[index] = line.Content[index];
                    }
                    else if (current == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (current is '\'' or '"')
                {
                    quote = current;
                    builder[index] = current;
                    continue;
                }

                if (current == '/'
                    && index + 1 < line.Content.Length
                    && line.Content[index + 1] == '/')
                {
                    break;
                }

                if (current == '/'
                    && index + 1 < line.Content.Length
                    && line.Content[index + 1] == '*')
                {
                    inBlockComment = true;
                    index++;
                    continue;
                }

                builder[index] = current;
            }

            result.Add(new string(builder));
        }

        return result;
    }

    public static IReadOnlyList<ClientRouteSourceLine> SplitLines(
        string source)
    {
        var lines = new List<ClientRouteSourceLine>();
        var start = 0;
        while (start < source.Length)
        {
            var end = source.IndexOf('\n', start);
            if (end < 0)
            {
                lines.Add(new ClientRouteSourceLine(
                    start,
                    source.Length,
                    source[start..].TrimEnd('\r')));
                return lines;
            }

            var contentEnd = end > start
                && source[end - 1] == '\r'
                    ? end - 1
                    : end;
            lines.Add(new ClientRouteSourceLine(
                start,
                contentEnd,
                source[start..end].TrimEnd('\r')));
            start = end + 1;
        }

        return lines;
    }

    public static string RelativeImport(
        string fromFile,
        string toFile)
    {
        var from = DirectoryPart(fromFile)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var to = toFile
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < from.Length
               && common < to.Length
               && StringComparer.OrdinalIgnoreCase.Equals(
                   from[common],
                   to[common]))
        {
            common++;
        }

        var relative = string.Join(
            '/',
            Enumerable.Repeat("..", from.Length - common)
                .Concat(to.Skip(common)));
        return relative.StartsWith(".", StringComparison.Ordinal)
            ? relative
            : $"./{relative}";
    }

    public static string LeadingWhitespace(string value) =>
        value[..value.TakeWhile(character =>
            character is ' ' or '\t').Count()];

    public static string DetectNewline(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

    private static string DirectoryPart(string value)
    {
        var normalized = value.Replace('\\', '/');
        var separator = normalized.LastIndexOf('/');
        return separator < 0 ? string.Empty : normalized[..separator];
    }
}

/// <summary>
/// 保存源文件一行的绝对字符位置，供最小文本插入使用。
/// </summary>
internal sealed record ClientRouteSourceLine(
    int Start,
    int ContentEnd,
    string Content);
