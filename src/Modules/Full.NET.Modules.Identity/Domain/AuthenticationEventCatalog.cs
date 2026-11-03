using System.Collections.Frozen;

namespace Full.NET.Modules.Identity.Domain;

/// <summary>认证事件的静态目录；分类不依赖译文，拒绝结果不得包含协议描述或凭据。</summary>
internal static class AuthenticationEventCatalog
{
    internal const string ProtocolRejected = "oidc.protocol_rejected";
    internal const string RecoveryCodesRegenerated = "mfa.recovery_codes_regenerated";
    internal const string RecoveryCodeConsumed = "mfa.recovery_code_consumed";
    internal const string OAuthCallbackRejected = "oauth.callback_rejected";

    internal static readonly FrozenDictionary<string, string> Categories = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["login"] = "credential", ["signature_authentication"] = "credential",
        ["open_access_api_key_authentication"] = "credential", [OAuthCallbackRejected] = "protocol",
        ["refresh"] = "session", ["logout"] = "session", ["context-switch"] = "session",
        ["oidc.center_login"] = "credential", ["oidc.application_session_created"] = "sso",
        ["oidc.center_session_reused"] = "sso",
        ["oidc.center_logout"] = "session", ["oidc.application_logout"] = "session",
        ["oidc.consent"] = "authorization", [ProtocolRejected] = "protocol",
        ["mfa.totp_enrollment_started"] = "mfa", ["mfa.totp_enrollment_confirmed"] = "mfa",
        ["mfa.strong_reauthentication"] = "mfa", [RecoveryCodesRegenerated] = "mfa", [RecoveryCodeConsumed] = "mfa",
        ["password_change"] = "credential_management", ["password.self_change"] = "credential_management",
        ["password.recovery"] = "credential_management", ["password_recovery.completed"] = "credential_management",
        ["password.admin_reset"] = "credential_management",
        ["host_online_session.revoked"] = "administration", ["host_online_session.revoked_all"] = "administration",
        ["host_user.login_lockout_unlocked"] = "administration", ["host_user.profile_sensitive_field_revealed"] = "administration",
        ["identity.oidc_client.created"] = "administration", ["identity.oidc_client.updated"] = "administration",
        ["identity.oidc_client.disabled"] = "administration", ["identity.oidc_client.rotated"] = "administration",
        ["identity.oidc_authorization.revoked"] = "administration", ["identity.oidc_signing_key.activated"] = "administration",
        ["identity.super_administrator.granted"] = "administration", ["identity.super_administrator.revoked"] = "administration",
        ["authentication_events.export"] = "administration",
        ["oidc.signing_key_published"] = "administration", ["oidc.signing_key_retired"] = "administration",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>只保留白名单机器码；未知渠道描述统一归类，禁止把异常、令牌或请求原文写入结果。</summary>
    internal static string ProtocolResult(string? error) => error switch
    {
        "invalid_request" or "invalid_client" or "invalid_grant" or "invalid_scope" or "invalid_token"
            or "unauthorized_client" or "unsupported_grant_type" or "unsupported_response_type"
            or "access_denied" or "login_required" or "consent_required" or "interaction_required"
            or "temporarily_unavailable" or "server_error" => "identity.oidc_protocol." + error,
        _ => "identity.oidc_protocol.rejected"
    };
}
