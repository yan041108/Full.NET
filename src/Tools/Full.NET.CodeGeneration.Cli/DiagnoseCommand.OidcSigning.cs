using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    // OIDC 的密钥环和启用边界独立于 JWT；只检查其签名前提，不认证完整协议配置或导入 PEM。
    private static void CheckOidcSigning(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        string? Read(string suffix)
        {
            _ = TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile,
                "Identity:Oidc:" + suffix, out var value);
            return value;
        }

        var enabled = Read("Enable");
        var ephemeral = Read("AllowDevelopmentEphemeralSigningKey");
        if ((enabled is not null && !bool.TryParse(enabled, out _))
            || (ephemeral is not null && !bool.TryParse(ephemeral, out _)))
        {
            findings.Add(DiagnoseFinding.Error("DIAG_OIDC_SIGNING_OPTIONS_INVALID",
                "OIDC 签名相关开关不能绑定为布尔值。",
                "将 Identity:Oidc 的 Enable 与 AllowDevelopmentEphemeralSigningKey 配置为 true 或 false；诊断不会输出配置值。"));
            return;
        }

        // 默认不启用；关闭 OIDC 后其 Validator 不要求签名配置，也不拒绝未生效的临时密钥开关。
        if (enabled is null || !bool.Parse(enabled))
        {
            findings.Add(DiagnoseFinding.Ok("DIAG_OIDC_DISABLED", "OIDC 未启用；未要求其独立活动签名配置。"));
            return;
        }

        var production = string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase);
        if (ephemeral is not null && bool.Parse(ephemeral))
        {
            var configuredKeys = ReadIdentitySigningKeyNames(root, profileSettings, workspacePath, profile, "Identity:Oidc").ToArray();
            if (!production && configuredKeys.Length > 0)
            {
                CheckDevelopmentConfiguredSigningKeys(root, profileSettings, workspacePath, profile,
                    true, configuredKeys, findings);
                return;
            }

            findings.Add(production
                ? DiagnoseFinding.Error("DIAG_OIDC_EPHEMERAL_SIGNING",
                    "Production 已启用的 OIDC 禁止使用开发临时签名密钥。",
                    "关闭 Identity:Oidc:AllowDevelopmentEphemeralSigningKey，配置 OIDC 自有的持久活动签名密钥。")
                : DiagnoseFinding.Warn("DIAG_OIDC_EPHEMERAL_SIGNING",
                    "Development 已显式启用 OIDC 临时签名密钥；重启后令牌失效，不能用于生产。",
                    "共享实例或持久会话请配置 OIDC 自有的活动签名密钥；诊断不会生成或输出密钥。"));
            return;
        }

        var active = Read("ActiveSigningKeyId");
        var names = ReadIdentitySigningKeyNames(root, profileSettings, workspacePath, profile, "Identity:Oidc");
        if (!string.IsNullOrWhiteSpace(active) && !IsPlaceholder(active)
            && names.Contains(active, StringComparer.Ordinal)
            && IsUsableSigningValue(Read("SigningKeys:" + active + ":PublicKeyPem"))
            && IsUsableSigningValue(Read("SigningKeys:" + active + ":PrivateKeyPem")))
        {
            findings.Add(DiagnoseFinding.Ok("DIAG_OIDC_SIGNING_CONFIGURED",
                "OIDC 活动签名配置项齐全；未验证 PEM 或 OIDC 的其他配置。"));
            return;
        }

        const string message = "已启用的 OIDC 缺少匹配且完整的独立活动签名密钥配置。";
        const string hint = "配置 Identity:Oidc 的 ActiveSigningKeyId 及同名 SigningKeys 公私钥；JWT 密钥不能替代。仅 Development 可显式启用 OIDC 临时签名，诊断不回显 KeyId 或密钥。";
        findings.Add(production
            ? DiagnoseFinding.Error("DIAG_OIDC_SIGNING_REQUIRED", message, hint)
            : DiagnoseFinding.Warn("DIAG_OIDC_SIGNING_REQUIRED", message, hint));
    }
}
