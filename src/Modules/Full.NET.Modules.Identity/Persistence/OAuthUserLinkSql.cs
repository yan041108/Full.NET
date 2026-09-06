using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>OAuth 用户绑定 SQL。</summary>
internal static class OAuthUserLinkSql
{
    private const string SelectColumns = """
        link.Id,
               link.UserId,
               link.ProviderKey,
               link.Subject,
               link.Email,
               link.EmailVerified,
               link.DisplayName,
               link.LinkedAtUtc,
               link.LastUsedAtUtc,
               link.Version
        """;

    public static readonly SqlStatement Insert = new(
        "identity.insert_oauth_user_link",
        """
        INSERT INTO fn_identity_oauth_user_link
            (Id, UserId, ProviderKey, Subject, Email, EmailVerified, DisplayName,
             LinkedAtUtc, LastUsedAtUtc, Version)
        VALUES
            (@Id, @UserId, @ProviderKey, @Subject, @Email, @EmailVerified, @DisplayName,
             @LinkedAtUtc, @LastUsedAtUtc, @Version)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByProviderKeyAndSubject = new(
        "identity.find_oauth_user_link_by_provider_key_and_subject",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_oauth_user_link AS link
        WHERE link.ProviderKey = @ProviderKey
          AND link.Subject = @Subject
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByUserIdAndProviderKey = new(
        "identity.find_oauth_user_link_by_user_id_and_provider_key",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_oauth_user_link AS link
        WHERE link.UserId = @UserId
          AND link.ProviderKey = @ProviderKey
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListByUserId = new(
        "identity.list_oauth_user_links_by_user_id",
        $"""
        SELECT {SelectColumns},
               provider.DisplayName AS ProviderDisplayName
        FROM fn_identity_oauth_user_link AS link
        INNER JOIN fn_identity_oauth_provider AS provider
            ON provider.ProviderKey = link.ProviderKey
        WHERE link.UserId = @UserId
        ORDER BY link.LinkedAtUtc DESC, link.Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindByIdAndUserId = new(
        "identity.find_oauth_user_link_by_id_and_user_id",
        $"""
        SELECT {SelectColumns}
        FROM fn_identity_oauth_user_link AS link
        WHERE link.Id = @LinkId
          AND link.UserId = @UserId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteByIdAndUserId = new(
        "identity.delete_oauth_user_link_by_id_and_user_id",
        """
        DELETE FROM fn_identity_oauth_user_link
        WHERE Id = @LinkId
          AND UserId = @UserId
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateLastUsed = new(
        "identity.update_oauth_user_link_last_used",
        """
        UPDATE fn_identity_oauth_user_link
        SET LastUsedAtUtc = @LastUsedAtUtc,
            Version = Version + 1
        WHERE Id = @LinkId
          AND Version = @Version
        """,
        SqlDataScope.HostOnly);
}

/// <summary>带提供程序显示名称的 OAuth 用户绑定查询行。</summary>
internal sealed class OAuthUserLinkWithProviderRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string ProviderKey { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string? Email { get; set; }

    public bool EmailVerified { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset LinkedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public int Version { get; set; }

    public string ProviderDisplayName { get; set; } = string.Empty;
}
