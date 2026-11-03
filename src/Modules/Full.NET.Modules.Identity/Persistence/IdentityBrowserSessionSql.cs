using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>BFF 票据共享存储；秘密只有保护后载荷，随机 Cookie 只保存不可逆摘要。</summary>
internal static class IdentityBrowserSessionSql
{
    public static readonly SqlStatement Find = new("identity.find_browser_session", """
        SELECT Id, HandleHash, Ciphertext, ExpiresAtUtc, Version, RefreshLeaseId, RefreshLeaseUntilUtc
        FROM fn_identity_browser_session WHERE HandleHash = @HandleHash AND ExpiresAtUtc > @Now
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Insert = new("identity.insert_browser_session", """
        INSERT INTO fn_identity_browser_session
            (Id, HandleHash, Ciphertext, ExpiresAtUtc, Version, RefreshLeaseId, RefreshLeaseUntilUtc)
        VALUES (@Id, @HandleHash, @Ciphertext, @ExpiresAtUtc, 1, NULL, NULL)
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Acquire = new("identity.acquire_browser_refresh", """
        UPDATE fn_identity_browser_session SET RefreshLeaseId = @LeaseId, RefreshLeaseUntilUtc = @LeaseUntil
        WHERE Id = @Id AND Version = @Version AND RefreshLeaseId IS NULL AND ExpiresAtUtc > @Now
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Replace = new("identity.replace_browser_credentials", """
        UPDATE fn_identity_browser_session
        SET Ciphertext = @Ciphertext, Version = Version + 1, RefreshLeaseId = NULL, RefreshLeaseUntilUtc = NULL
        WHERE Id = @Id AND Version = @Version AND RefreshLeaseId = @LeaseId AND ExpiresAtUtc > @Now
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Release = new("identity.release_browser_refresh", """
        UPDATE fn_identity_browser_session SET RefreshLeaseId = NULL, RefreshLeaseUntilUtc = NULL
        WHERE Id = @Id AND RefreshLeaseId = @LeaseId
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement Delete = new("identity.delete_browser_session",
        "DELETE FROM fn_identity_browser_session WHERE Id = @Id", SqlDataScope.HostOnly);
    public static readonly SqlStatement PruneSqlServer = new("identity.prune_browser_session", """
        DELETE TOP (500) FROM fn_identity_browser_session WHERE ExpiresAtUtc <= @Now
        """, SqlDataScope.HostOnly);
    public static readonly SqlStatement PruneMySql = new("identity.prune_browser_session_mysql", """
        DELETE FROM fn_identity_browser_session WHERE ExpiresAtUtc <= @Now LIMIT 500
        """, SqlDataScope.HostOnly);
}

internal sealed record IdentityBrowserSessionRow(Guid Id, string HandleHash, string Ciphertext,
    DateTimeOffset ExpiresAtUtc, long Version, Guid? RefreshLeaseId, DateTimeOffset? RefreshLeaseUntilUtc);
