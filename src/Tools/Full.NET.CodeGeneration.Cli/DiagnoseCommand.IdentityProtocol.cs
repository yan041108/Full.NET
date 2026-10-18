using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static void CheckIdentityProtocolOptions(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        // JWT 标识只遵循宿主的非空规则，不能套用 OIDC Issuer 的 URL 要求；关闭端点也须校验。
        var invalidIdentifiers = new[] { "Issuer", "Audience", "ClientId" }.Any(key =>
            TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile, "Identity:" + key, out var value)
                && string.IsNullOrWhiteSpace(value));
        findings.Add(invalidIdentifiers
            ? DiagnoseFinding.Error("DIAG_IDENTITY_IDENTIFIERS_INVALID",
                "Identity 标识配置包含空值，不能满足宿主要求。",
                "将最终生效的 Issuer、Audience、ClientId 配置为非空、非纯空白字符串；省略配置时保留宿主默认值。诊断不会输出标识值。")
            : DiagnoseFinding.Ok("DIAG_IDENTITY_IDENTIFIERS_CONFIGURED",
                "Identity 的 Issuer、Audience、ClientId 符合宿主非空要求；未认证完整 Identity Options 或宿主启动。"));

        _ = TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile,
            "Identity:SessionLoginPolicy", out var policy);
        // 配置绑定允许名称、数字和名称组合；仅最终的已定义枚举值有效，不另建更窄的字符串白名单。
        var validPolicy = policy is null || (Enum.TryParse<DiagnosticSessionLoginPolicy>(policy, true, out var parsed)
            && Enum.IsDefined(parsed));
        findings.Add(validPolicy
            ? DiagnoseFinding.Ok("DIAG_IDENTITY_SESSION_POLICY_CONFIGURED",
                "Identity 会话策略配置可绑定为已定义值；未认证实际登录、会话撤销或宿主启动。")
            : DiagnoseFinding.Error("DIAG_IDENTITY_SESSION_POLICY_INVALID",
                "Identity:SessionLoginPolicy 不能绑定为已定义的会话策略。",
                "使用 AllowMultiple、SingleSession 或 SingleSessionPerClient（数值 0、1、2）；诊断不会输出配置值。"));
    }

    // 工具不引用 Identity 运行时模块；真实 Contract 枚举和 Options 绑定回归约束名称与已发布数值。
    private enum DiagnosticSessionLoginPolicy
    {
        AllowMultiple = 0,
        SingleSession = 1,
        SingleSessionPerClient = 2,
    }
}
