using System.Collections.ObjectModel;
using System.Xml;
using System.Xml.Linq;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存 Composition 手写文件的纯内存改写结果。
/// </summary>
public sealed class CompositionIntegrationEditResult
{
    private CompositionIntegrationEditResult(
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

    public static CompositionIntegrationEditResult Success(
        string originalContent,
        string desiredContent) =>
        new(
            succeeded: true,
            changed: !StringComparer.Ordinal.Equals(
                originalContent,
                desiredContent),
            desiredContent,
            diagnostics: []);

    public static CompositionIntegrationEditResult Failure(
        string originalContent,
        string diagnostic) =>
        new(
            succeeded: false,
            changed: false,
            originalContent,
            [diagnostic]);
}

/// <summary>
/// 只向结构可验证的 Composition 项目增加一个精确模块项目引用。
/// </summary>
public static class CompositionProjectEditor
{
    public static CompositionIntegrationEditResult Edit(
        string source,
        string compositionProjectPath,
        string moduleProjectPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            compositionProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleProjectPath);

        XDocument document;
        try
        {
            document = XDocument.Parse(
                source,
                LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return CompositionIntegrationEditResult.Failure(
                source,
                "Composition 项目 XML 无法解析。");
        }

        if (document.Root?.Name.LocalName != "Project")
        {
            return CompositionIntegrationEditResult.Failure(
                source,
                "Composition 项目缺少唯一 Project 根元素。");
        }

        var projectReferences = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "ProjectReference")
            .ToArray();
        var referenceGroups = document
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "ItemGroup"
                && element.Elements().Any(child =>
                    child.Name.LocalName == "ProjectReference"))
            .ToArray();
        if (referenceGroups.Length > 1)
        {
            return CompositionIntegrationEditResult.Failure(
                source,
                "Composition 项目必须且只能存在一个可验证的 ProjectReference ItemGroup。");
        }

        var matchingReferences = projectReferences.Count(reference =>
            ReferenceMatches(
                compositionProjectPath,
                reference.Attribute("Include")?.Value,
                moduleProjectPath));
        if (matchingReferences > 1)
        {
            return CompositionIntegrationEditResult.Failure(
                source,
                "Composition 项目包含重复的目标模块 ProjectReference。");
        }

        if (matchingReferences == 1)
        {
            return CompositionIntegrationEditResult.Success(
                source,
                source);
        }

        var newline = DetectNewline(source);
        var lines = SplitLines(source);
        var relativeReference = CreateRelativeReference(
            compositionProjectPath,
            moduleProjectPath);
        var includeAttribute = new XAttribute(
            "Include",
            relativeReference).ToString();
        if (referenceGroups.Length == 1)
        {
            var lineInfo = (IXmlLineInfo)referenceGroups[0];
            if (!lineInfo.HasLineInfo())
            {
                return CompositionIntegrationEditResult.Failure(
                    source,
                    "Composition 项目无法定位 ProjectReference ItemGroup。");
            }

            var closingLine = FindClosingLine(
                lines,
                lineInfo.LineNumber - 1,
                "</ItemGroup>");
            if (closingLine < 0)
            {
                return CompositionIntegrationEditResult.Failure(
                    source,
                    "Composition 项目无法定位 ProjectReference ItemGroup 结束位置。");
            }

            var closingIndent = LeadingWhitespace(
                lines[closingLine].Content);
            var insertion =
                $"{closingIndent}  <ProjectReference "
                + $"{includeAttribute} />{newline}";
            var desired = source.Insert(
                lines[closingLine].Start,
                insertion);
            return ValidatedSuccess(source, desired);
        }

        var projectClosingLines = lines
            .Select((line, index) => new { line, index })
            .Where(item =>
                item.line.Content.Trim() == "</Project>")
            .Select(item => item.index)
            .ToArray();
        if (projectClosingLines.Length != 1)
        {
            return CompositionIntegrationEditResult.Failure(
                source,
                "Composition 项目无法定位唯一 Project 结束位置。");
        }

        var projectIndent = LeadingWhitespace(
            lines[projectClosingLines[0]].Content);
        var group =
            $"{projectIndent}  <ItemGroup>{newline}"
            + $"{projectIndent}    <ProjectReference "
            + $"{includeAttribute} />{newline}"
            + $"{projectIndent}  </ItemGroup>{newline}";
        var result = source.Insert(
            lines[projectClosingLines[0]].Start,
            group);
        return ValidatedSuccess(source, result);
    }

    private static CompositionIntegrationEditResult ValidatedSuccess(
        string originalContent,
        string desiredContent)
    {
        try
        {
            _ = XDocument.Parse(desiredContent, LoadOptions.None);
            return CompositionIntegrationEditResult.Success(
                originalContent,
                desiredContent);
        }
        catch (XmlException)
        {
            return CompositionIntegrationEditResult.Failure(
                originalContent,
                "Composition 项目候选 XML 无法解析。");
        }
    }

    private static bool ReferenceMatches(
        string compositionProjectPath,
        string? include,
        string moduleProjectPath) =>
        !string.IsNullOrWhiteSpace(include)
        && StringComparer.OrdinalIgnoreCase.Equals(
            ResolveReference(compositionProjectPath, include),
            Normalize(moduleProjectPath));

    private static string CreateRelativeReference(
        string compositionProjectPath,
        string moduleProjectPath)
    {
        var from = RepositoryDirectory(
                Normalize(compositionProjectPath))
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);
        var to = Normalize(moduleProjectPath)
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < from.Length
               && common < to.Length
               && StringComparer.OrdinalIgnoreCase.Equals(
                   from[common],
                   to[common]))
        {
            common++;
        }

        return string.Join(
            '\\',
            Enumerable.Repeat("..", from.Length - common)
                .Concat(to.Skip(common)));
    }

    private static string ResolveReference(
        string projectPath,
        string reference)
    {
        var segments = RepositoryDirectory(Normalize(projectPath))
            .Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        foreach (var segment in Normalize(reference)
                     .Split(
                         '/',
                         StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return "";
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static int FindClosingLine(
        IReadOnlyList<SourceLine> lines,
        int start,
        string closingTag)
    {
        for (var index = start + 1; index < lines.Count; index++)
        {
            if (lines[index].Content.Trim() == closingTag)
            {
                return index;
            }
        }

        return -1;
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
                    source[start..].TrimEnd('\r')));
                return lines;
            }

            lines.Add(new SourceLine(
                start,
                source[start..end].TrimEnd('\r')));
            start = end + 1;
        }

        return lines;
    }

    private static string LeadingWhitespace(string line) =>
        line[..line.TakeWhile(character =>
            character is ' ' or '\t').Count()];

    private static string RepositoryDirectory(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? "" : path[..separator];
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/');

    private static string DetectNewline(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

    private sealed record SourceLine(
        int Start,
        string Content);
}
