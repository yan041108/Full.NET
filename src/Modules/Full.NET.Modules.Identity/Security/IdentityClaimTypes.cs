// Identity 声明类型常量；与 JWT、Cookie 和 OIDC 映射保持一致。
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Security;

/// <summary>Identity 声明类型常量；与 JWT、Cookie 和 OIDC 映射保持一致。</summary>
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
