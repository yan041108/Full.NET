namespace Full.NET.Modules.Identity.Contracts;

/// <summary>OIDC 签名密钥管理 API 契约；仅暴露公钥元数据，永不返回私钥材料。</summary>
public static class IdentityOidcSigningKeyPermissions
{
    public const string Read = "identity.oidc_signing_keys.read";
}

public sealed record OidcSigningKeyResponse(
    string KeyId,
    bool IsActive,
    bool HasPrivateKey,
    string Algorithm,
    string PublicKeyPem);

public sealed record OidcSigningKeyListResponse(
    string ActiveSigningKeyId,
    bool UsesEphemeralDevelopmentKey,
    IReadOnlyList<OidcSigningKeyResponse> Keys);