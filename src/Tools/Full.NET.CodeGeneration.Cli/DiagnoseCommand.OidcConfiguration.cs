using System.Security.Cryptography;
using System.Text.Json;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    // 只核对已启用 OIDC 的地址与可选加密配置前提；关闭或布尔绑定失败交由签名诊断处理。
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

        var issuer = Read("Issuer");
        if (string.IsNullOrWhiteSpace(issuer) || !Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            findings.Add(DiagnoseFinding.Error("DIAG_OIDC_ISSUER_INVALID",
                "已启用 OIDC 的 Issuer 缺失或不是合法的无凭据 HTTP(S) 绝对地址。",
                "核对 Identity:Oidc:Issuer；诊断不访问该地址，也不回显地址或凭据。"));
        }
        else
        {
            findings.Add(DiagnoseFinding.Ok("DIAG_OIDC_ISSUER_CONFIGURED",
                "OIDC Issuer 已通过地址格式检查；未验证可达性或完整协议配置。"));
        }

        var encryption = Read("EncryptionKeyBase64");
        // 与现有 Validator 一致，空值仍是可选配置；不擅自增加生产必填策略。
        if (string.IsNullOrWhiteSpace(encryption)) return;

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
