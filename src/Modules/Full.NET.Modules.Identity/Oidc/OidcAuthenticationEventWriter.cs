using Full.NET.Abstractions.Ids;
using System.Diagnostics;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>OIDC 中心与应用会话的认证事件；只写入已有 Identity 审计表。</summary>
internal sealed class OidcAuthenticationEventWriter(
    ICommandExecutor commandExecutor,
    IIdGenerator idGenerator,
    IClock clock)
{
    internal const string CenterLogin = "oidc.center_login";
    internal const string ApplicationSessionCreated = "oidc.application_session_created";
    internal const string CenterLogout = "oidc.center_logout";
    internal const string ApplicationLogout = "oidc.application_logout";

    public async Task WriteAsync(
        Guid? userId,
        string? normalizedUsername,
        string eventType,
        string resultCode,
        bool succeeded,
        CancellationToken cancellationToken,
        Guid? centerSessionId = null,
        string? clientId = null,
        Guid? applicationSessionId = null)
    {
        var audit = new AuthAuditEvent(
            idGenerator.NewId(),
            userId,
            null,
            string.IsNullOrWhiteSpace(normalizedUsername)
                ? string.Empty : TokenHash.Compute(normalizedUsername),
            eventType,
            resultCode,
            succeeded,
            null,
            null,
            null,
            clock.UtcNow,
            succeeded ? userId : null,
            Activity.Current?.TraceId.ToString(),
            eventType == CenterLogin ? "password"
                : eventType == ApplicationSessionCreated ? "sso" : "session",
            clientId,
            centerSessionId,
            applicationSessionId);
        var rows = await commandExecutor.ExecuteAsync(
            IdentitySql.InsertAuthAudit, audit, cancellationToken).ConfigureAwait(false);
        if (rows != 1)
        {
            throw new InvalidOperationException(
                $"OIDC authentication audit insert affected {rows} rows instead of one.");
        }
    }
}
