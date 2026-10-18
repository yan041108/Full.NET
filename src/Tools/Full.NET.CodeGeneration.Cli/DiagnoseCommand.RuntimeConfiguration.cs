using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static IConfigurationRoot CreateDiagnosticConfiguration(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        IEnumerable<string> paths, params string[] sections)
    {
        // 复用既有逐叶合并；先映射特殊连接环境前缀，不扩大到未选择的配置节。
        var effective = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPath in paths)
        {
            var path = rawPath;
            foreach (var prefix in ConnectionEnvironmentPrefixes)
                if (path.StartsWith(prefix.Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    path = "ConnectionStrings:" + path[prefix.Prefix.Length..];
                    break;
                }
            if (!sections.Any(section => string.Equals(path, section, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile, path, out var value))
                effective.TryAdd(path, value);
        }
        return new ConfigurationBuilder().AddInMemoryCollection(effective).Build();
    }
}
