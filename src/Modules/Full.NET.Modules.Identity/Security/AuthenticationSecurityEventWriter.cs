using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Security;

/// <summary>写入凭据恢复和 MFA 安全事件；调用方决定状态与审计的事务边界。</summary>
internal sealed class AuthenticationSecurityEventWriter(
    ICommandExecutor commands,
    IIdGenerator ids,
    IClock clock)
{
    public async Task WriteAsync(
        Guid? userId,
        Guid? actorUserId,
        string eventType,
        string resultCode,
        bool succeeded,
        string authenticationMethod,
        CancellationToken cancellationToken)
    {
        var audit = new AuthAuditEvent(
            ids.NewId(), userId, null, string.Empty,
            eventType, resultCode, succeeded,
            null, null, null, clock.UtcNow,
            actorUserId, Activity.Current?.TraceId.ToString(),
            authenticationMethod);
        var rows = await commands.ExecuteAsync(
            IdentitySql.InsertAuthAudit, audit, cancellationToken).ConfigureAwait(false);
        if (rows != 1)
        {
            throw new InvalidOperationException(
                $"Authentication security audit insert affected {rows} rows instead of one.");
        }
    }
}
