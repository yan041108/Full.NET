using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Cryptography.Persistence;

internal static class CryptographySql
{
    private const string KeyColumns =
        """
        Id, KeyKey, DisplayName, Description, Algorithm, Purpose,
        PublicKeyHex, PublicKeyFingerprint, Status, SortOrder,
        CreatedAtUtc, UpdatedAtUtc
        """;

    public static readonly SqlStatement ListKeys =
        new(
            "cryptography.list_keys",
            $"""
            SELECT {KeyColumns}
            FROM fn_cryptography_key
            ORDER BY SortOrder, KeyKey
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindKeyById =
        new(
            "cryptography.find_key_by_id",
            $"""
            SELECT {KeyColumns}
            FROM fn_cryptography_key
            WHERE Id = @Id
            """,
            SqlDataScope.HostOnly);

    public static readonly SqlStatement FindKeyByKeyKey =
        new(
            "cryptography.find_key_by_key_key",
            $"""
            SELECT {KeyColumns}
            FROM fn_cryptography_key
            WHERE KeyKey = @KeyKey
            """,
            SqlDataScope.HostOnly);
}
