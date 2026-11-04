using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static void CheckDevelopmentConfiguredSigningKeys(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        bool oidc, IReadOnlyCollection<string> keyNames, List<DiagnoseFinding> findings)
    {
        // 开发临时密钥仅在字典为空时回退；有条目时按真实密钥环选择字段，不导入 PEM。
        var section = oidc ? "Identity:Oidc" : "Identity";
        string? Read(string suffix)
        {
            _ = TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile,
                section + ":" + suffix, out var value);
            return value;
        }

        var active = Read(oidc ? "ActiveSigningKeyId" : "ActiveKeyId");
        var configured = IsUsableSigningValue(active)
            && keyNames.Contains(active, StringComparer.Ordinal)
            && keyNames.All(name =>
            {
                var privateKey = Read("SigningKeys:" + name + ":PrivateKeyPem");
                if (string.Equals(name, active, StringComparison.Ordinal))
                    return IsUsableSigningValue(privateKey);

                // JWT 的非活动项只用公钥；OIDC 非活动项优先用非空私钥，否则用公钥。
                if (oidc && !string.IsNullOrWhiteSpace(privateKey))
                    return IsUsableSigningValue(privateKey);
                return IsUsableSigningValue(Read("SigningKeys:" + name + ":PublicKeyPem"));
            });
        var code = oidc ? "DIAG_OIDC_SIGNING_" : "DIAG_IDENTITY_SIGNING_";
        findings.Add(configured
            ? DiagnoseFinding.Ok(code + "CONFIGURED",
                "已配置开发签名环的活动私钥与验证字段齐全；不会使用临时回退，未验证 PEM 或密码学有效性。")
            : DiagnoseFinding.Error(code + "REQUIRED",
                "已配置的开发签名环缺少匹配的活动私钥或验证字段，不能回退到临时密钥。",
                "修正精确匹配的活动 KeyId 及密钥环所需字段；仅无任何密钥条目时才能使用开发临时回退，诊断不会输出 KeyId 或密钥。"));
    }
}
