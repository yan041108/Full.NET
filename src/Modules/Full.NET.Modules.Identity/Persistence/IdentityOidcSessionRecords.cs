namespace Full.NET.Modules.Identity.Persistence;

internal sealed class IdentityOidcCenterSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class IdentityOidcApplicationSession
{
    public Guid Id { get; set; }
    public Guid CenterSessionId { get; set; }
    public Guid OidcApplicationId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string ActorScope { get; set; } = string.Empty;
    public string EffectiveScope { get; set; } = string.Empty;
    public Guid? ActiveTenantId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed record IdentityOidcCenterSessionRow(
    Guid Id,
    Guid UserId,
    string SecurityStamp,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    long Version,
    DateTimeOffset UpdatedAtUtc);

internal sealed record IdentityOidcApplicationSessionRow(
    Guid Id,
    Guid CenterSessionId,
    Guid OidcApplicationId,
    string ClientId,
    Guid UserId,
    string ActorScope,
    string EffectiveScope,
    Guid? ActiveTenantId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    long Version,
    DateTimeOffset UpdatedAtUtc);

internal sealed class IdentityOidcActiveApplicationSessionOwnershipRow
{
    public Guid SessionId { get; set; }

    public Guid UserId { get; set; }
}

internal sealed class IdentityOidcApplicationSessionValidationRecord
{
    public Guid ApplicationSessionId { get; set; }
    public Guid CenterSessionId { get; set; }
    public Guid UserId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ActorScope { get; set; } = string.Empty;
    public string EffectiveScope { get; set; } = string.Empty;
    public Guid? ActiveTenantId { get; set; }
    public DateTimeOffset ApplicationExpiresAtUtc { get; set; }
    public DateTimeOffset? ApplicationRevokedAtUtc { get; set; }
    public string CenterSecurityStamp { get; set; } = string.Empty;
    public DateTimeOffset CenterExpiresAtUtc { get; set; }
    public DateTimeOffset? CenterRevokedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? LockoutEndUtc { get; set; }
    public string UserSecurityStamp { get; set; } = string.Empty;
}

internal static class IdentityOidcSessionRecordMapper
{
    internal static IdentityOidcCenterSession ToCenterSession(IdentityOidcCenterSessionRow row) => new()
    {
        Id = row.Id,
        UserId = row.UserId,
        SecurityStamp = row.SecurityStamp,
        CreatedAtUtc = row.CreatedAtUtc,
        ExpiresAtUtc = row.ExpiresAtUtc,
        RevokedAtUtc = row.RevokedAtUtc,
        Version = row.Version,
        UpdatedAtUtc = row.UpdatedAtUtc,
    };

    internal static IdentityOidcApplicationSession ToApplicationSession(
        IdentityOidcApplicationSessionRow row) => new()
    {
        Id = row.Id,
        CenterSessionId = row.CenterSessionId,
        OidcApplicationId = row.OidcApplicationId,
        ClientId = row.ClientId,
        UserId = row.UserId,
        ActorScope = row.ActorScope,
        EffectiveScope = row.EffectiveScope,
        ActiveTenantId = row.ActiveTenantId,
        CreatedAtUtc = row.CreatedAtUtc,
        ExpiresAtUtc = row.ExpiresAtUtc,
        RevokedAtUtc = row.RevokedAtUtc,
        Version = row.Version,
        UpdatedAtUtc = row.UpdatedAtUtc,
    };
}