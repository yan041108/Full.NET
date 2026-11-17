using System.Security.Cryptography;
using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    // 核对已启用 OIDC 的地址和加密配置前提；关闭或布尔绑定失败交由签名诊断处理。
    private static void CheckOidcIssuerAndEncryption(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        List<DiagnoseFinding> findings)
    {
        string? Read(string suffix)
        {
            _ = TryReadIdentitySigningValue(root, profileSettings, workspacePath, profile,
                "Identity:Oidc:" + suffix, out var value);
            return value;
        }

        if (!bool.TryParse(Read("Enable"), out var enabled) || !enabled) return;

        var production = string.Equals(profile, "production", StringComparison.OrdinalIgnoreCase);
        var issuer = Read("Issuer");
        if (string.IsNullOrWhiteSpace(issuer) || !Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || (production && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            findings.Add(DiagnoseFinding.Error("DIAG_OIDC_ISSUER_INVALID",
                "已启用 OIDC 的 Issuer 缺失或不符合当前环境的地址规则。",
                "核对 Identity:Oidc:Issuer，使用无凭据、查询或片段的绝对地址，Production 使用 HTTPS；诊断不访问或回显地址。"));
        }
        else
        {
            findings.Add(DiagnoseFinding.Ok("DIAG_OIDC_ISSUER_CONFIGURED",
                "OIDC Issuer 已通过地址格式检查；未验证可达性或完整协议配置。"));
        }

        var encryption = Read("EncryptionKeyBase64");
        if (string.IsNullOrWhiteSpace(encryption))
        {
            if (production)
                findings.Add(DiagnoseFinding.Error("DIAG_OIDC_ENCRYPTION_INVALID",
                    "Production 已启用的 OIDC 缺少加密密钥配置。",
                    "配置 Identity:Oidc:EncryptionKeyBase64 为有效的 256 位 Base64 密钥；诊断不生成或回显密钥。"));
            return;
        }

        Span<byte> decoded = stackalloc byte[32];
        try
        {
            if (Convert.TryFromBase64String(encryption, decoded, out var bytes) && bytes == 32)
            {
                findings.Add(DiagnoseFinding.Ok("DIAG_OIDC_ENCRYPTION_CONFIGURED",
                    "OIDC 加密配置已通过 Base64 格式与 256 位长度检查；未验证密钥强度或多实例一致性。"));
            }
            else
            {
                findings.Add(DiagnoseFinding.Error("DIAG_OIDC_ENCRYPTION_INVALID",
                    "已提供的 OIDC 加密密钥不是有效的 256 位 Base64 配置。",
                    "核对 Identity:Oidc:EncryptionKeyBase64 的格式和解码后长度；诊断不生成或回显密钥。"));
            }
        }
        finally
        {
            // 格式检查只使用固定大小缓冲区，离开检查后清除解码后的秘密。
            CryptographicOperations.ZeroMemory(decoded);
        }
    }
}
