namespace Full.NET.Modules.Cryptography.Contracts;

/// <summary>国密控制面的稳定权限码。</summary>
public static class CryptographyPermissions
{
    /// <summary>允许查询国密密钥目录与部署状态。</summary>
    public const string KeysRead = "cryptography.keys.read";

    /// <summary>允许在授权密钥上执行 SM2 签名。</summary>
    public const string Sm2Sign = "cryptography.sm2.sign";

    /// <summary>允许执行 SM2 验签。</summary>
    public const string Sm2Verify = "cryptography.sm2.verify";
}

/// <summary>国密密钥状态机值。</summary>
public static class CryptographyKeyStatuses
{
    /// <summary>可用于签名与验签。</summary>
    public const string Active = "active";

    /// <summary>已退役，禁止新签名。</summary>
    public const string Retired = "retired";
}

/// <summary>国密模块稳定错误码。</summary>
public static class CryptographyErrorCodes
{
    public const string Prefix = "cryptography.";

    public const string KeyNotFound = "cryptography.key.not_found";

    public const string KeyRetired = "cryptography.key.retired";

    public const string PrivateKeyNotConfigured = "cryptography.key.private_key_not_configured";

    public const string SignValidationFailed = "cryptography.sm2.sign_validation_failed";

    public const string VerifyValidationFailed = "cryptography.sm2.verify_validation_failed";

    public const string SignatureInvalid = "cryptography.sm2.signature_invalid";
}

/// <summary>国密部署状态响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Algorithm">稳定算法键（如 SM2）。</param>
/// <param name="DefaultUserId">SM2 默认用户 Id，用于签名与验签；不携带私钥。</param>
/// <param name="SigningPurpose">签名用途说明，描述当前部署的签名语义。</param>
/// <param name="DeploymentNotice">部署提示文本，说明运行约束或风险。</param>
public sealed record CryptographyStatusResponse(
    string Algorithm,
    string DefaultUserId,
    string SigningPurpose,
    string DeploymentNotice);

/// <summary>国密密钥目录项。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。KeyKey、Algorithm、Purpose、Status 等稳定键发布后不得改名或删除。</remarks>
/// <param name="Id">密钥记录唯一标识。</param>
/// <param name="KeyKey">稳定密钥键，业务调用方据此选择密钥。</param>
/// <param name="DisplayName">展示名称，用于人读。</param>
/// <param name="Description">补充说明；可为 <see langword="null"/>。</param>
/// <param name="Algorithm">稳定算法键（如 SM2）。</param>
/// <param name="Purpose">密钥用途键（如 sign、verify）。</param>
/// <param name="PublicKeyHex">公钥十六进制表示。</param>
/// <param name="PublicKeyFingerprint">公钥指纹，用于跨系统比对与人工核对。</param>
/// <param name="Status">稳定密钥状态键（见 <see cref="CryptographyKeyStatuses"/>）。</param>
/// <param name="PrivateKeyConfigured">是否已配置私钥；<see langword="false"/> 时禁止签名。</param>
/// <param name="SortOrder">目录排序序号，用于稳定展示。</param>
/// <param name="CreatedAtUtc">密钥记录创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">密钥记录最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
public sealed record CryptographyKeyResponse(
    Guid Id,
    string KeyKey,
    string DisplayName,
    string? Description,
    string Algorithm,
    string Purpose,
    string PublicKeyHex,
    string PublicKeyFingerprint,
    string Status,
    bool PrivateKeyConfigured,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>SM2 签名请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="KeyKey">稳定密钥键，必须对应已配置私钥且 <see cref="CryptographyKeyStatuses.Active"/> 的密钥。</param>
/// <param name="Message">待签名原文；调用方需保证原文边界与编码一致，避免摘要差异。</param>
/// <param name="UserId">SM2 用户 Id；为 <see langword="null"/> 时使用部署默认值，不可携带私钥。</param>
public sealed record Sm2SignRequest(
    string KeyKey,
    string Message,
    string? UserId);

/// <summary>SM2 签名响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="KeyId">签名所用密钥记录 Id。</param>
/// <param name="KeyKey">稳定密钥键，便于调用方关联请求。</param>
/// <param name="Algorithm">稳定算法键（如 SM2）。</param>
/// <param name="SignatureHex">签名十六进制表示；用于跨系统核对。</param>
/// <param name="PublicKeyFingerprint">签名所用公钥指纹，便于验签方选择对应公钥。</param>
/// <param name="SignedAtUtc">签名完成时间（UTC）。</param>
public sealed record Sm2SignResponse(
    Guid KeyId,
    string KeyKey,
    string Algorithm,
    string SignatureHex,
    string PublicKeyFingerprint,
    DateTimeOffset SignedAtUtc);

/// <summary>SM2 验签请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="KeyKey">稳定密钥键，用于定位验签所用公钥。</param>
/// <param name="Message">验签原文；必须与签名时边界与编码一致，否则必然失败。</param>
/// <param name="SignatureHex">待校验签名十六进制表示。</param>
/// <param name="UserId">SM2 用户 Id；为 <see langword="null"/> 时使用部署默认值，须与签名时一致。</param>
public sealed record Sm2VerifyRequest(
    string KeyKey,
    string Message,
    string SignatureHex,
    string? UserId);

/// <summary>SM2 验签响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="KeyId">验签所用密钥记录 Id。</param>
/// <param name="KeyKey">稳定密钥键，便于调用方关联请求。</param>
/// <param name="IsValid">验签结果；<see langword="true"/> 表示签名有效，<see langword="false"/> 不得回退为成功。</param>
/// <param name="PublicKeyFingerprint">验签所用公钥指纹，便于人工核对。</param>
public sealed record Sm2VerifyResponse(
    Guid KeyId,
    string KeyKey,
    bool IsValid,
    string PublicKeyFingerprint);
