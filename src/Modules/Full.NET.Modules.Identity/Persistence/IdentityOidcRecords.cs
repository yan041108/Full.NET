using Full.NET.Modules.Identity.Oidc;

namespace Full.NET.Modules.Identity.Persistence;

public sealed class IdentityOidcApplication
{
    public Guid Id { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ConsentType { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayNamesJson { get; set; }
    public string? PermissionsJson { get; set; }
    public string? PostLogoutRedirectUrisJson { get; set; }
    public string? PropertiesJson { get; set; }
    public string? RedirectUrisJson { get; set; }
    public string? RequirementsJson { get; set; }
    public string? ApplicationType { get; set; }
    public string? JsonWebKeySetJson { get; set; }
    public string? SettingsJson { get; set; }
    public string? ClientType { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class IdentityOidcAuthorization
{
    public Guid Id { get; set; }
    public Guid? ApplicationId { get; set; }
    public DateTimeOffset? CreationDateUtc { get; set; }
    public string? PropertiesJson { get; set; }
    public string? ScopesJson { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class IdentityOidcScope
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? DescriptionsJson { get; set; }
    public string? DisplayName { get; set; }
    public string? DisplayNamesJson { get; set; }
    public string? PropertiesJson { get; set; }
    public string? ResourcesJson { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class IdentityOidcToken
{
    public Guid Id { get; set; }
    public Guid? ApplicationId { get; set; }
    public Guid? AuthorizationId { get; set; }
    public DateTimeOffset? CreationDateUtc { get; set; }
    public DateTimeOffset? ExpirationDateUtc { get; set; }
    public string? Payload { get; set; }
    public string? PropertiesJson { get; set; }
    public DateTimeOffset? RedemptionDateUtc { get; set; }
    public string? ReferenceId { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    internal string? SessionId { get; set; }
}

internal sealed record IdentityOidcApplicationRow(
    Guid Id, string? ClientId, string? ClientSecret, string? ConsentType, string? DisplayName,
    string? DisplayNamesJson, string? PermissionsJson, string? PostLogoutRedirectUrisJson,
    string? PropertiesJson, string? RedirectUrisJson, string? RequirementsJson, string? ApplicationType,
    string? JsonWebKeySetJson, string? SettingsJson, string? ClientType, long Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

internal sealed record IdentityOidcAuthorizationRow(
    Guid Id, Guid? ApplicationId, DateTimeOffset? CreationDateUtc, string? PropertiesJson,
    string? ScopesJson, string? Status, string? Subject, string? Type, long Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

internal sealed record IdentityOidcScopeRow(
    Guid Id, string? Name, string? Description, string? DescriptionsJson, string? DisplayName,
    string? DisplayNamesJson, string? PropertiesJson, string? ResourcesJson, long Version,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

internal sealed record IdentityOidcTokenRow(
    Guid Id, Guid? ApplicationId, Guid? AuthorizationId, DateTimeOffset? CreationDateUtc,
    DateTimeOffset? ExpirationDateUtc, string? Payload, string? PropertiesJson,
    DateTimeOffset? RedemptionDateUtc, string? ReferenceId, string? Status, string? Subject,
    string? Type, long Version, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

internal sealed record IdentityOidcCountRow(long Count);

internal static class IdentityOidcRecordMapper
{
    internal static IdentityOidcApplication ToApplication(IdentityOidcApplicationRow row) => new()
    {
        Id = row.Id, ClientId = row.ClientId, ClientSecret = row.ClientSecret, ConsentType = row.ConsentType,
        DisplayName = row.DisplayName, DisplayNamesJson = row.DisplayNamesJson, PermissionsJson = row.PermissionsJson,
        PostLogoutRedirectUrisJson = row.PostLogoutRedirectUrisJson, PropertiesJson = row.PropertiesJson,
        RedirectUrisJson = row.RedirectUrisJson, RequirementsJson = row.RequirementsJson,
        ApplicationType = row.ApplicationType, JsonWebKeySetJson = row.JsonWebKeySetJson,
        SettingsJson = row.SettingsJson, ClientType = row.ClientType, Version = row.Version,
        CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
    };

    internal static IdentityOidcAuthorization ToAuthorization(IdentityOidcAuthorizationRow row) => new()
    {
        Id = row.Id, ApplicationId = row.ApplicationId, CreationDateUtc = row.CreationDateUtc,
        PropertiesJson = row.PropertiesJson, ScopesJson = row.ScopesJson, Status = row.Status,
        Subject = row.Subject, Type = row.Type, Version = row.Version,
        CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
    };

    internal static IdentityOidcScope ToScope(IdentityOidcScopeRow row) => new()
    {
        Id = row.Id, Name = row.Name, Description = row.Description, DescriptionsJson = row.DescriptionsJson,
        DisplayName = row.DisplayName, DisplayNamesJson = row.DisplayNamesJson, PropertiesJson = row.PropertiesJson,
        ResourcesJson = row.ResourcesJson, Version = row.Version,
        CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
    };

    internal static IdentityOidcToken ToToken(IdentityOidcTokenRow row)
    {
        var token = new IdentityOidcToken
        {
            Id = row.Id, ApplicationId = row.ApplicationId, AuthorizationId = row.AuthorizationId,
            CreationDateUtc = row.CreationDateUtc, ExpirationDateUtc = row.ExpirationDateUtc,
            Payload = row.Payload, PropertiesJson = row.PropertiesJson, RedemptionDateUtc = row.RedemptionDateUtc,
            ReferenceId = row.ReferenceId, Status = row.Status, Subject = row.Subject, Type = row.Type,
            Version = row.Version, CreatedAtUtc = row.CreatedAtUtc, UpdatedAtUtc = row.UpdatedAtUtc,
        };
        token.SessionId = IdentityOidcStoreSupport.ReadSessionId(token.PropertiesJson);
        return token;
    }
}