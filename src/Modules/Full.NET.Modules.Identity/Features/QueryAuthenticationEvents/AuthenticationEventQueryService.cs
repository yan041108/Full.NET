using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.QueryAuthenticationEvents;

/// <summary>在 Host 权限边界内用稳定游标查询认证审计。</summary>
internal sealed class AuthenticationEventQueryService(
    IQueryExecutor queryExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<AuthenticationEventCursorPage>> ListAsync(
        int page,
        int pageSize,
        Guid? userId,
        string? eventType,
        bool? succeeded,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var normalizedEventType = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim();
        var end = toUtc ?? clock.UtcNow;
        var start = fromUtc ?? end.AddDays(-1);
        DateTimeOffset? cursorTime = null;
        Guid? cursorId = null;
        if (page != 1 || pageSize is < 1 or > 100
            || normalizedEventType is { Length: > 100 })
        {
            return InvalidQuery();
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TryReadCursor(cursor, userId, normalizedEventType, succeeded,
                    out var cursorFrom, out var cursorTo,
                    out var occurredAt, out var id)
                || (fromUtc.HasValue && fromUtc.Value.UtcTicks != cursorFrom.UtcTicks)
                || (toUtc.HasValue && toUtc.Value.UtcTicks != cursorTo.UtcTicks))
            {
                return InvalidQuery();
            }

            start = cursorFrom;
            end = cursorTo;
            cursorTime = occurredAt;
            cursorId = id;
        }

        if (end <= start || end - start > TimeSpan.FromDays(31)
            || (cursorTime.HasValue && (cursorTime < start || cursorTime >= end)))
        {
            return InvalidQuery();
        }

        var parameters = IdentitySqlParameters.Create(
            ("FromUtc", start), ("ToUtc", end), ("UserId", userId),
            ("EventType", normalizedEventType), ("Succeeded", succeeded),
            ("CursorOccurredAtUtc", cursorTime), ("CursorId", cursorId),
            ("FetchSize", pageSize + 1));
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AuthenticationEventSql.ListSqlServer,
            DatabaseProvider.MySql => AuthenticationEventSql.ListMySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };
        var rows = await queryExecutor.QueryAsync<AuthenticationEventResponse>(
            statement, parameters, cancellationToken).ConfigureAwait(false);
        var items = rows.Take(pageSize).ToArray();
        var nextCursor = rows.Count > pageSize
            ? WriteCursor(start, end, items[^1], userId, normalizedEventType, succeeded)
            : null;
        return Result<AuthenticationEventCursorPage>.Success(
            new AuthenticationEventCursorPage(items, nextCursor));
    }

    public async Task<AuthenticationEventResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<AuthenticationEventResponse>(
            AuthenticationEventSql.GetById,
            IdentitySqlParameters.Create(("Id", id)), cancellationToken).ConfigureAwait(false);

    private static Result<AuthenticationEventCursorPage> InvalidQuery() =>
        Result<AuthenticationEventCursorPage>.Failure(
            new Error("identity.authentication_events.invalid_query",
                "Invalid authentication event query.", ErrorType.Validation));

    private static string WriteCursor(
        DateTimeOffset from, DateTimeOffset to, AuthenticationEventResponse last,
        Guid? userId, string? eventType, bool? succeeded)
    {
        var payload = string.Join('|', "v1",
            from.UtcTicks.ToString(CultureInfo.InvariantCulture),
            to.UtcTicks.ToString(CultureInfo.InvariantCulture),
            last.OccurredAtUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            last.Id.ToString("N"), FilterDigest(userId, eventType, succeeded));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryReadCursor(
        string value, Guid? userId, string? eventType, bool? succeeded,
        out DateTimeOffset from, out DateTimeOffset to,
        out DateTimeOffset occurredAt, out Guid id)
    {
        from = to = occurredAt = default;
        id = default;
        if (value.Length is < 1 or > 256 || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        string payload;
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            payload = Encoding.UTF8.GetString(Convert.FromBase64String(
                base64.PadRight((base64.Length + 3) / 4 * 4, '=')));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = payload.Split('|');
        if (parts.Length != 6 || parts[0] != "v1"
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var fromTicks)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var toTicks)
            || !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var rowTicks)
            || !Guid.TryParseExact(parts[4], "N", out id)
            || id == Guid.Empty
            || !string.Equals(parts[5], FilterDigest(userId, eventType, succeeded), StringComparison.Ordinal)
            || fromTicks < DateTimeOffset.MinValue.Ticks
            || fromTicks > DateTimeOffset.MaxValue.Ticks
            || toTicks < DateTimeOffset.MinValue.Ticks
            || toTicks > DateTimeOffset.MaxValue.Ticks
            || rowTicks < DateTimeOffset.MinValue.Ticks
            || rowTicks > DateTimeOffset.MaxValue.Ticks)
        {
            return false;
        }

        from = new DateTimeOffset(fromTicks, TimeSpan.Zero);
        to = new DateTimeOffset(toTicks, TimeSpan.Zero);
        occurredAt = new DateTimeOffset(rowTicks, TimeSpan.Zero);
        return true;
    }

    private static string FilterDigest(Guid? userId, string? eventType, bool? succeeded)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            userId?.ToString("N") ?? string.Empty,
            eventType ?? string.Empty,
            succeeded is null ? string.Empty : succeeded.Value ? "1" : "0")));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }
}
