namespace Full.NET.Modules.Cryptography.Configuration;

/// <summary>国密 SM2 部署配置；私钥仅允许来自受信配置源，不得通过匿名 API 回显。</summary>
public sealed class CryptographyOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "FullNet:Cryptography";

    /// <summary>默认 SM2 用户标识，用于 SM3withSM2 签名。</summary>
    public string DefaultUserId { get; init; } = "1234567812345678";

    /// <summary>首个受控签名场景说明。</summary>
    public string SigningPurpose { get; init; } =
        "integration-payload-signature";

    /// <summary>部署边界说明。</summary>
    public string DeploymentNotice { get; init; } =
        "国密控制面仅提供受授权 SM2 签名/验签与密钥状态目录；私钥由部署期配置注入，不提供匿名通用加解密接口。";

    /// <summary>按 KeyKey 映射的 SM2 私钥十六进制（不含 0x 前缀）。</summary>
    public IReadOnlyDictionary<string, string> Sm2PrivateKeys { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
