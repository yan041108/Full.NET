using System.Diagnostics;
using System.Globalization;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.QueryAuthenticationEvents;

/// <summary>只导出安全投影，超过行数上限时要求缩小范围，不静默截断。</summary>
internal sealed class AuthenticationEventExportService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator ids,
    IOptions<DatabaseOptions> databaseOptions)
{
    private const int MaximumRows = 10_000;

    public async Task<Result<byte[]>> ExportAsync(
        Guid actorUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? userId,
        string? eventType,
        bool? succeeded,
        CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty || toUtc <= fromUtc
            || toUtc - fromUtc > TimeSpan.FromDays(31)
            || eventType is { Length: > 100 })
        {
            return Invalid("identity.authentication_events.invalid_export_range",
                "Invalid authentication event export range.");
        }

        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AuthenticationEventSql.ExportSqlServer,
            DatabaseProvider.MySql => AuthenticationEventSql.ExportMySql,
            _ => throw new InvalidOperationException("Unsupported database provider.")
        };
        var rows = await queryExecutor.QueryAsync<AuthenticationEventResponse>(
                statement,
                IdentitySqlParameters.Create(
                    ("FromUtc", fromUtc), ("ToUtc", toUtc), ("UserId", userId),
                    ("EventType", string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim()),
                    ("Succeeded", succeeded), ("MaxRows", MaximumRows + 1)),
                cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count > MaximumRows)
        {
            return Invalid("identity.authentication_events.export_too_large",
                "Narrow the export range to 10000 rows or fewer.");
        }

        var csv = new StringBuilder("Id,OccurredAtUtc,EventType,ResultCode,Succeeded,UserId,ActorUserId,AuthenticationMethod,ClientId,TraceId,SessionId,CenterSessionId,ApplicationSessionId\r\n");
        foreach (var row in rows)
        {
            var values = new[]
            {
                row.Id.ToString("D"), row.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                row.EventType, row.ResultCode, row.Succeeded ? "true" : "false",
                row.UserId?.ToString("D"), row.ActorUserId?.ToString("D"),
                row.AuthenticationMethod, row.ClientId, row.TraceId, row.SessionId?.ToString("D"),
                row.CenterSessionId?.ToString("D"), row.ApplicationSessionId?.ToString("D")
            };
            csv.AppendJoin(',', values.Select(EscapeCell)).Append("\r\n");
        }

        // 导出本身也是身份安全事件；审计写入失败时不返回文件。
        var audit = new AuthAuditEvent(ids.NewId(), actorUserId, null, string.Empty,
            "authentication_events.export", "identity.authentication_events_exported", true,
            null, null, null, clock.UtcNow, actorUserId,
            Activity.Current?.TraceId.ToString(), "session");
        var affected = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertAuthAudit, audit, cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidOperationException("Authentication event export audit insert failed.");
        }

        var body = Encoding.UTF8.GetBytes(csv.ToString());
        return Result<byte[]>.Success([.. Encoding.UTF8.GetPreamble(), .. body]);
    }

    private static string EscapeCell(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Length > 0 && (safe[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n'))
        {
            safe = "'" + safe;
        }

        return '"' + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static Result<byte[]> Invalid(string code, string message) =>
        Result<byte[]>.Failure(new Error(code, message, ErrorType.Validation));
}
