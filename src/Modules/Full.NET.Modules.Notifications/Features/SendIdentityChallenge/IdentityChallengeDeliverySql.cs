using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Features.SendIdentityChallenge;

internal static class IdentityChallengeDeliverySql
{
    public static readonly SqlStatement FindFirstHostSmtpProfileVersionSqlServer = new(
        "notifications.identity_challenge.find_first_host_smtp_profile_version.sql_server",
        """
        SELECT TOP (1) pv.Id, pv.ProfileId, pv.VersionNumber, pv.ProviderTypeKey, pv.AdapterVersion,
               pv.NonSecretConfigJson, pv.SecretReference, pv.ContentHash, pv.PublishedById, pv.PublishedAtUtc
        FROM fn_notifications_provider_profile AS profile
        INNER JOIN fn_notifications_provider_profile_version AS pv
            ON pv.Id = profile.LatestPublishedVersionId
        WHERE profile.TenantScopeKey = 'host'
          AND profile.ProviderTypeKey = 'email.smtp'
          AND profile.IsEnabled = 1
        ORDER BY profile.CreatedAtUtc, profile.Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindFirstHostSmtpProfileVersionMySql = new(
        "notifications.identity_challenge.find_first_host_smtp_profile_version.mysql",
        """
        SELECT pv.Id, pv.ProfileId, pv.VersionNumber, pv.ProviderTypeKey, pv.AdapterVersion,
               pv.NonSecretConfigJson, pv.SecretReference, pv.ContentHash, pv.PublishedById, pv.PublishedAtUtc
        FROM fn_notifications_provider_profile AS profile
        INNER JOIN fn_notifications_provider_profile_version AS pv
            ON pv.Id = profile.LatestPublishedVersionId
        WHERE profile.TenantScopeKey = 'host'
          AND profile.ProviderTypeKey = 'email.smtp'
          AND profile.IsEnabled = 1
        ORDER BY profile.CreatedAtUtc, profile.Id
        LIMIT 1
        """,
        SqlDataScope.Global);
}
