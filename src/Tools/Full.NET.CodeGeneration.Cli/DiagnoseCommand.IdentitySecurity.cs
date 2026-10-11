using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static void CheckIdentitySecurityOptions(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        string? Read(string key)
        {
            _ = TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile, "Identity:" + key, out var value);
            return value;
        }

        var secureCookies = Read("RequireSecureCookies");
        var remoteAdmin = Read("EnableRemoteSuperAdministratorManagement");
        var totp = Read("EnableTotpStrongReauthentication");
        // 关闭令牌端点也会绑定这些属性；只检查布尔绑定，不额外要求 Cookie 开关为 true。
        if (new[] { secureCookies, remoteAdmin, totp }.Any(value => value is not null && !bool.TryParse(value, out _)))
        {
            findings.Add(DiagnoseFinding.Error("DIAG_IDENTITY_SECURITY_OPTIONS_INVALID",
                "Identity 安全相关开关不能绑定为布尔值。",
                "将 RequireSecureCookies、EnableRemoteSuperAdministratorManagement、EnableTotpStrongReauthentication 配置为 true 或 false；诊断不会输出配置值。"));
            return;
        }

        // 两个管理开关缺键或显式 null 均为 false；Production 约束与真实 Options 校验一致。
        if (string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase)
            && remoteAdmin is not null && bool.Parse(remoteAdmin)
            && (totp is null || !bool.Parse(totp)))
        {
            findings.Add(DiagnoseFinding.Error("DIAG_IDENTITY_REMOTE_ADMIN_REAUTH_REQUIRED",
                "Production 远程超管管理缺少 TOTP 强认证开关。",
                "关闭 EnableRemoteSuperAdministratorManagement，或配置 EnableTotpStrongReauthentication=true 并另行验证强认证 Provider；诊断不会注册或执行 Provider。"));
            return;
        }

        findings.Add(DiagnoseFinding.Ok("DIAG_IDENTITY_SECURITY_OPTIONS_CONFIGURED",
            "Identity 三项安全开关绑定及 Production 远程超管配置约束符合宿主规则；未认证实际 Cookie、TOTP、授权或宿主启动。"));
    }
}
