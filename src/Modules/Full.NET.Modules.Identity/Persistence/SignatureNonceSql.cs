using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>签名 Nonce 防重放持久化 SQL。</summary>
internal static class SignatureNonceSql
{
    public static readonly SqlStatement TryInsert = new(
        "identity.insert_signature_nonce",
        """
        INSERT INTO fn_identity_signature_nonce
            (Id, AccessKeyId, NonceDigest, CreatedAtUtc, ExpiresAtUtc)
        VALUES
            (@Id, @AccessKeyId, @NonceDigest, @CreatedAtUtc, @ExpiresAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Exists = new(
        "identity.exists_signature_nonce",
        """
        SELECT TOP (1) 1
        FROM fn_identity_signature_nonce
        WHERE AccessKeyId = @AccessKeyId
          AND NonceDigest = @NonceDigest
          AND ExpiresAtUtc > @NowUtc
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ExistsMySql = new(
        "identity.exists_signature_nonce.mysql",
        """
        SELECT 1
        FROM fn_identity_signature_nonce
        WHERE AccessKeyId = @AccessKeyId
          AND NonceDigest = @NonceDigest
          AND ExpiresAtUtc > @NowUtc
        LIMIT 1
        """,
        SqlDataScope.Global);
}
