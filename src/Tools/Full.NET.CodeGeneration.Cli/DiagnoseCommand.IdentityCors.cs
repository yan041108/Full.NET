using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    // 宿主固定启用 CORS 凭据；只核对可绑定来源项中的单独星号，不替代来源语义或完整策略校验。
    private static void CheckIdentityCorsCredentials(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        const string prefix = "Identity:AllowedOrigins:";
        // 字符串数组按直接子键绑定，忽略不能构造字符串的对象项；空父节点不删除低层子项。
        var items = ReadRuntimeConfigurationPaths(root, profileSettings, workspacePath, profile)
            .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(path => prefix + path[prefix.Length..].Split(':')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var hasWildcard = items.Any(path => TryReadIdentitySigningValue(
            root, profileSettings, workspacePath, profile, path, out var value) && value == "*");
        findings.Add(hasWildcard
            ? DiagnoseFinding.Error("DIAG_IDENTITY_CORS_CREDENTIALS_INVALID",
                "Identity CORS 来源列表中的单独星号与宿主启用凭据的策略冲突。",
                "替换或移除对应的 AllowedOrigins 子项；空父节点不会删除低层子项。诊断不会输出来源值。")
            : DiagnoseFinding.Ok("DIAG_IDENTITY_CORS_CREDENTIALS_CONFIGURED",
                "Identity 来源配置未发现单独星号与凭据策略冲突；未认证来源语义、实际 CORS 或宿主启动。"));
    }
}
