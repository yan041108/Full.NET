using System.Globalization;
using System.Text.RegularExpressions;

namespace Full.NET.Hosting.Observability;

/// <summary>Local 写入与受权读取共用的应用目录和自有 txt 文件名契约，不创建目录。</summary>
public static partial class LocalLogFileLayout
{
    /// <summary>配置目录相对 ContentRoot 解析，再追加经过规范化的应用名。</summary>
    public static string ResolveDirectory(string directory, string contentRoot, string application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        var scope = string.Concat(application.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        if (scope.Length is < 1 or > 120 || scope is "." or "..") throw new ArgumentException("Local log application scope is invalid.");
        return Path.Combine(Path.GetFullPath(directory, contentRoot), scope);
    }

    /// <summary>只接收本出口的通道、UTC 日期及文件标识格式；不接受任意 txt。</summary>
    public static bool IsOwnedFileName(string name)
    {
        var match = OwnedName().Match(name);
        return match.Success && DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    [GeneratedRegex("^fullnet-(?:general|priority)-([0-9]{8})-[0-9a-f]{32}\\.txt$", RegexOptions.CultureInvariant)]
    internal static partial Regex OwnedName();
}
