using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Security;

internal static class IdentityClaimTypes
{
    public const string SessionId = FullNetIdentityClaimTypes.SessionId;

    public const string ActorScope = FullNetIdentityClaimTypes.ActorScope;

    public const string Scope = FullNetIdentityClaimTypes.Scope;

    public const string SecurityStamp = FullNetIdentityClaimTypes.SecurityStamp;

    public const string TenantId = FullNetIdentityClaimTypes.TenantId;

    public const string Permission = FullNetIdentityClaimTypes.Permission;

    public const string SuperAdministrator =
        FullNetIdentityClaimTypes.SuperAdministrator;

    public const string ApiKeyId = FullNetIdentityClaimTypes.ApiKeyId;
}
