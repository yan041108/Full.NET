using System.Net;
using System.Text;
using Full.NET.Localization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>中心服务端页面的规范语言与输出编码；ui_locales 优先，未知语言回退请求协商结果。</summary>
internal sealed class IdentityOidcPages(string locale)
{
    public string Locale { get; } = locale;
    public bool ConsentOnLogin { get; set; }
    private bool Chinese => Locale == LocaleCatalog.Chinese;
    public string Text(string key) => (Chinese ? ChineseText : EnglishText)[key];
    public static IdentityOidcPages Resolve(string? preferred, ILocaleNormalizer normalizer, ILocaleContext current)
    {
        foreach (var candidate in (preferred ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (normalizer.IsSupported(candidate)) return new(normalizer.Normalize(candidate));
        return new(normalizer.Normalize(current.CurrentLocale));
    }
    public static void Secure(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
    }
    public string Login(OpenIddictRequest request, AntiforgeryTokenSet token, string? message = null, bool change = false)
    {
        var b = Start(Text("center"));
        Message(b, message);
        b.Append("<form method=\"post\" action=\"/connect/authorize\">");
        Protocol(b, request); Anti(b, token);
        Field(b, "username", "username", "text", "username");
        Field(b, "password", "password", "password", "current-password");
        Field(b, "second_factor", "mfa", "text", "one-time-code", false);
        if (change) Field(b, "new_password", "newPassword", "password", "new-password");
        if (ConsentOnLogin && !change)
        {
            b.Append("<p>").Append(E(Text("consent"))).Append(" ").Append(E(request.ClientId)).Append("</p><ul>");
            foreach (var scope in request.GetScopes()) b.Append("<li translate=\"no\">").Append(E(scope)).Append("</li>");
            b.Append("</ul>");
        }
        b.Append(ConsentOnLogin && !change ? "<button type=\"submit\" name=\"consent\" value=\"accept\">" : "<button type=\"submit\">")
            .Append(E(Text(change ? "change" : ConsentOnLogin ? "allow" : "signin"))).Append("</button></form>")
            .Append("<a href=\"/connect/recover?ui_locales=").Append(Uri.EscapeDataString(Locale)).Append("\">")
            .Append(E(Text("recover"))).Append("</a>");
        return End(b);
    }
    public string Consent(OpenIddictRequest request, AntiforgeryTokenSet token, string clientName,
        string? tenantName = null, string? tenantProof = null, string? tenantConsentId = null)
    {
        var b = Start(Text("consent"));
        b.Append("<p>").Append(E(clientName)).Append("</p><ul>");
        foreach (var scope in request.GetScopes()) b.Append("<li translate=\"no\">").Append(E(scope)).Append("</li>");
        b.Append("</ul><form method=\"post\" action=\"/connect/authorize\">");
        Protocol(b, request); Anti(b, token);
        if (tenantName is not null)
        {
            b.Append("<p>").Append(E(Text("tenant"))).Append(": ").Append(E(tenantName)).Append("</p>");
            foreach (var resource in request.GetResources())
                b.Append("<p>").Append(E(Text("resource"))).Append(": ").Append(E(resource)).Append("</p>");
            Hidden(b, "fullnet_tenant_consent", tenantProof);
            Hidden(b, "fullnet_tenant_consent_id", tenantConsentId);
        }
        b.Append("<button name=\"consent\" value=\"accept\">").Append(E(Text("allow")))
            .Append("</button><button name=\"consent\" value=\"deny\">").Append(E(Text("deny"))).Append("</button></form>");
        return End(b);
    }
    public string Recovery(AntiforgeryTokenSet token, string? message)
    {
        var b = Start(Text("recover")); Message(b, message);
        b.Append("<p>").Append(E(Text("recoveryHelp"))).Append("</p><form method=\"post\" action=\"/connect/recover\">");
        Anti(b, token); Hidden(b, "ui_locales", Locale);
        Field(b, "username", "username", "text", "username");
        Field(b, "recovery_code", "recoveryCode", "password", "off");
        Field(b, "new_password", "newPassword", "password", "new-password");
        b.Append("<button>").Append(E(Text("change"))).Append("</button></form><a href=\"/\">")
            .Append(E(Text("return"))).Append("</a>");
        return End(b);
    }
    public string Logout(OpenIddictRequest request, AntiforgeryTokenSet token)
    {
        var b = Start(Text("signoutQuestion"));
        b.Append("<form method=\"post\" action=\"/connect/logout\">"); Anti(b, token);
        Hidden(b, "client_id", request.ClientId); Hidden(b, "id_token_hint", request.IdTokenHint);
        Hidden(b, "post_logout_redirect_uri", request.PostLogoutRedirectUri); Hidden(b, "state", request.State);
        Hidden(b, "ui_locales", Locale);
        b.Append("<button name=\"confirm\" value=\"yes\">").Append(E(Text("signout"))).Append("</button></form>");
        return End(b);
    }
    private StringBuilder Start(string title) => new StringBuilder("<!DOCTYPE html><html lang=\"").Append(E(Locale))
        .Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>")
        .Append(E(title)).Append("</title></head><body><main><h1>").Append(E(title)).Append("</h1>");
    private static string End(StringBuilder b) => b.Append("</main></body></html>").ToString();
    private static void Message(StringBuilder b, string? message)
    { if (message is not null) b.Append("<p role=\"status\">").Append(E(message)).Append("</p>"); }
    private void Field(StringBuilder b, string name, string label, string type, string autocomplete, bool required = true) =>
        b.Append("<p><label>").Append(E(Text(label))).Append(" <input name=\"").Append(name).Append("\" type=\"")
        .Append(type).Append("\" autocomplete=\"").Append(autocomplete).Append(required ? "\" required></label></p>" : "\"></label></p>");
    private static void Anti(StringBuilder b, AntiforgeryTokenSet token) => Hidden(b, token.FormFieldName, token.RequestToken);
    private static void Hidden(StringBuilder b, string name, string? value)
    {
        if (value is null) return;
        b.Append("<input type=\"hidden\" name=\"").Append(E(name)).Append("\" value=\"").Append(E(value)).Append("\">");
    }
    private void Protocol(StringBuilder b, OpenIddictRequest r)
    {
        // 交互登录和同意表单必须原样带回经协议层校验的资源指示符。
        foreach (var resource in r.GetResources()) Hidden(b, "resource", resource);
        foreach (var (name, value) in new (string, string?)[] { ("client_id", r.ClientId), ("redirect_uri", r.RedirectUri),
            ("response_type", r.ResponseType), ("response_mode", r.ResponseMode), ("scope", r.Scope), ("state", r.State), ("nonce", r.Nonce),
            ("code_challenge", r.CodeChallenge), ("code_challenge_method", r.CodeChallengeMethod),
            ("prompt", r.Prompt), ("max_age", r.MaxAge?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("acr_values", r.AcrValues), ("login_hint", r.LoginHint), ("ui_locales", Locale),
            ("fullnet_tenant_id", r.GetParameter("fullnet_tenant_id")?.ToString()) }) Hidden(b, name, value);
    }
    private static string E(string? text) => WebUtility.HtmlEncode(text) ?? string.Empty;
    private static readonly Dictionary<string, string> ChineseText = new()
    {
        ["resource"] = "目标资源", ["tenant"] = "目标租户", ["center"] = "身份认证中心", ["username"] = "用户名", ["password"] = "当前密码", ["newPassword"] = "新密码",
        ["mfa"] = "验证码或备用恢复码（已启用多因素认证时必填）", ["signin"] = "登录", ["signout"] = "退出登录",
        ["signoutQuestion"] = "退出当前浏览器的认证中心会话？", ["consent"] = "允许此应用访问以下信息？",
        ["allow"] = "允许", ["deny"] = "拒绝", ["invalid"] = "凭据或验证码无效，请重试。",
        ["passwordRequired"] = "请先修改密码，然后重新登录。", ["passwordChanged"] = "密码已更新，请重新登录；多因素认证仍然有效。",
        ["change"] = "更新密码", ["recover"] = "恢复账号密码", ["recoveryCode"] = "备用恢复码", ["return"] = "返回应用登录",
        ["recoveryHelp"] = "使用预先生成的备用恢复码重置密码；没有恢复码请联系管理员。重置不会关闭多因素认证。",
        ["mfaRequired"] = "此应用要求多因素认证，请使用已启用验证器或备用恢复码的账号重新登录。"
    };
    private static readonly Dictionary<string, string> EnglishText = new()
    {
        ["resource"] = "Target resource", ["tenant"] = "Target tenant", ["center"] = "Identity Center", ["username"] = "Username", ["password"] = "Current password", ["newPassword"] = "New password",
        ["mfa"] = "Authenticator or recovery code (required when MFA is enabled)", ["signin"] = "Sign in", ["signout"] = "Sign out",
        ["signoutQuestion"] = "Sign out of Identity Center in this browser?", ["consent"] = "Allow this application to access this information?",
        ["allow"] = "Allow", ["deny"] = "Deny", ["invalid"] = "The credentials or verification code are invalid.",
        ["passwordRequired"] = "Change your password before signing in again.", ["passwordChanged"] = "Password updated. Sign in again; MFA remains enabled.",
        ["change"] = "Update password", ["recover"] = "Recover account password", ["recoveryCode"] = "Recovery code", ["return"] = "Return to application sign-in",
        ["recoveryHelp"] = "Reset with a previously generated recovery code, or contact an administrator. Resetting does not disable MFA.",
        ["mfaRequired"] = "This application requires MFA. Sign in again using an enrolled authenticator or a recovery code."
    };
}
