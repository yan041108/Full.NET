namespace Full.NET.Modules.Identity.Oidc;

/// <summary>OIDC 中心登录 Cookie 认证方案与声明类型常量。</summary>
internal static class IdentityOidcCenterAuthenticationDefaults
{
    public const string AuthenticationScheme = "Identity.Oidc.Center";

    public const string CenterSessionIdClaim = "fn:oidc_center_session_id";

    public const string UserIdClaim = "fn:oidc_center_user_id";

    public const string SecurityStampClaim = "fn:oidc_center_security_stamp";

    public const string UsernameClaim = "fn:oidc_center_username";

    public const string DisplayNameClaim = "fn:oidc_center_display_name";

    public const string CookieName = "fullnet-oidc-center";
}
