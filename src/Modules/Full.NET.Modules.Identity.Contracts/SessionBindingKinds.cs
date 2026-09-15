namespace Full.NET.Modules.Identity.Contracts;

/// <summary>后台会话绑定的权威来源；调用方不得自报未知种类。</summary>
public static class SessionBindingKinds
{
    /// <summary>旧刷新会话表 <c>fn_identity_refresh_session</c>。</summary>
    public const string Refresh = "refresh";

    /// <summary>OIDC 应用会话表 <c>fn_identity_oidc_application_session</c>。</summary>
    public const string OidcApplication = "oidc-application";
}