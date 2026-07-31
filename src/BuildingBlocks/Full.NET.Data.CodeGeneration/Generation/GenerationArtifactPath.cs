using System.Text;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 集中校验生成产物的工作区相对路径，避免后续写盘适配层出现路径穿越。
/// </summary>
internal static class GenerationArtifactPath
{
    private static readonly HashSet<string> WindowsReservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "CONIN$",
            "CONOUT$",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "COM¹",
            "COM²",
            "COM³",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
            "LPT¹",
            "LPT²",
            "LPT³",
        };

    public static string Validate(string? relativePath, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("生成产物相对路径不能为空。", parameterName);
        }

        if (relativePath[0] == '/'
            || relativePath.Contains('\\', StringComparison.Ordinal)
            || relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "生成产物路径必须是仅使用正斜杠的工作区相对路径。",
                parameterName);
        }

        if (!relativePath.IsNormalized(NormalizationForm.FormC))
        {
            throw new ArgumentException(
                "生成产物路径必须使用 Unicode NFC 规范形式。",
                parameterName);
        }

        var segments = relativePath.Split('/');
        if (segments.Any(segment =>
                segment.Length == 0
                || segment == "."
                || segment == ".."
                || segment.EndsWith('.')
                || segment.EndsWith(' ')
                || segment.Any(IsInvalidPortableCharacter)
                || WindowsReservedNames.Contains(
                    segment.Split('.', 2)[0])))
        {
            throw new ArgumentException(
                "生成产物路径包含不安全或不可移植的路径段。",
                parameterName);
        }

        return relativePath;
    }

    private static bool IsInvalidPortableCharacter(char character)
    {
        return character < ' '
            || character is '"' or '<' or '>' or '|' or '?' or '*';
    }
}
