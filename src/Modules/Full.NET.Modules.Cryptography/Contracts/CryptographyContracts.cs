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
public sealed record CryptographyStatusResponse(
    string Algorithm,
    string DefaultUserId,
    string SigningPurpose,
    string DeploymentNotice);

/// <summary>国密密钥目录项。</summary>
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
public sealed record Sm2SignRequest(
    string KeyKey,
    string Message,
    string? UserId);

/// <summary>SM2 签名响应。</summary>
public sealed record Sm2SignResponse(
    Guid KeyId,
    string KeyKey,
    string Algorithm,
    string SignatureHex,
    string PublicKeyFingerprint,
    DateTimeOffset SignedAtUtc);

/// <summary>SM2 验签请求。</summary>
public sealed record Sm2VerifyRequest(
    string KeyKey,
    string Message,
    string SignatureHex,
    string? UserId);

/// <summary>SM2 验签响应。</summary>
public sealed record Sm2VerifyResponse(
    Guid KeyId,
    string KeyKey,
    bool IsValid,
    string PublicKeyFingerprint);
