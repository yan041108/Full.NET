using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>OIDC 治理写路径审计；资源指纹写入 UsernameFingerprint 字段供检索。</summary>
internal sealed class OidcManagementAuditWriter(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    IClock clock)
{
    internal const string ClientCreatedEventType = "identity.oidc_client.created";
    internal const string ClientUpdatedEventType = "identity.oidc_client.updated";
    internal const string ClientDisabledEventType = "identity.oidc_client.disabled";
    internal const string ClientRotatedEventType = "identity.oidc_client.rotated";
    internal const string AuthorizationRevokedEventType = "identity.oidc_authorization.revoked";
    internal const string SigningKeyActivatedEventType = "identity.oidc_signing_key.activated";

    public async Task WriteAsync(
        Guid actorUserId,
        string eventType,
        string resourceFingerprint,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            actorUserId,
            null,
            Truncate(resourceFingerprint, 128),
            eventType,
            resourceFingerprint,
            true,
            Truncate(ipAddress, 64),
            Truncate(userAgent, 512),
            null,
            clock.UtcNow);
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertAuthAudit,
                audit,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"OIDC management audit insert affected {affectedRows} rows instead of one.");
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

internal sealed record OidcManagementActorContext(
    Guid ActorUserId,
    string? IpAddress,
    string? UserAgent);