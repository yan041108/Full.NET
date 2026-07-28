using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.Auditing.Features.QueryHostAccessLogs;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditingAccessLogCursorTests
{
    private static readonly DateTimeOffset ReferenceUtc =
        new(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Cursor_round_trips_boundary_and_rejects_another_filter()
    {
        var filter = CreateFilter("/api/v1/settings");
        var boundary = new AccessLogCursorBoundary(
            ReferenceUtc.AddMinutes(-5),
            Guid.Parse("01984827-9c80-7000-8000-000000000001"));

        var cursor = AccessLogCursorCodec.Encode(boundary, filter);

        Assert.DoesNotContain("+", cursor, StringComparison.Ordinal);
        Assert.DoesNotContain("/", cursor, StringComparison.Ordinal);
        Assert.DoesNotContain("=", cursor, StringComparison.Ordinal);
        Assert.IsTrue(AccessLogCursorCodec.TryDecode(
            cursor,
            filter,
            out var decoded));
        Assert.AreEqual(boundary, decoded);
        Assert.IsFalse(AccessLogCursorCodec.TryDecode(
            cursor,
            CreateFilter("/api/v1/identity"),
            out _));
    }

    [TestMethod]
    public void Cursor_rejects_malformed_unknown_version_and_empty_identifier()
    {
        var filter = CreateFilter("/api/v1/settings");
        var boundary = new AccessLogCursorBoundary(
            ReferenceUtc,
            Guid.Parse("01984827-9c80-7000-8000-000000000001"));
        var valid = AccessLogCursorCodec.Encode(boundary, filter);
        var nonUrlAlphabet = valid.Replace('-', '+').Replace('_', '/');
        var payload = DecodeBase64Url(valid);

        payload[0] = 2;
        var unknownVersion = EncodeBase64Url(payload);
        Array.Clear(payload, 9, 16);
        payload[0] = 1;
        var emptyIdentifier = EncodeBase64Url(payload);

        Assert.IsFalse(AccessLogCursorCodec.TryDecode("not-base64!", filter, out _));
        Assert.AreNotEqual(valid, nonUrlAlphabet);
        Assert.IsFalse(AccessLogCursorCodec.TryDecode(
            nonUrlAlphabet,
            filter,
            out _));
        Assert.IsFalse(AccessLogCursorCodec.TryDecode(unknownVersion, filter, out _));
        Assert.IsFalse(AccessLogCursorCodec.TryDecode(emptyIdentifier, filter, out _));
    }

    [TestMethod]
    public void Cursor_statements_are_host_only_parameterized_and_offset_free()
    {
        var sqlServerFirst = AccessLogSql.CreateCursorListSqlServer(
            hasCursor: false,
            hasFromUtc: true,
            hasToUtc: true,
            hasHttpMethod: true,
            hasStatusCode: true,
            hasPathContains: true);
        var sqlServerAfter = AccessLogSql.CreateCursorListSqlServer(
            hasCursor: true,
            hasFromUtc: true,
            hasToUtc: true,
            hasHttpMethod: true,
            hasStatusCode: true,
            hasPathContains: true);
        var statements = new[]
        {
            sqlServerFirst,
            sqlServerAfter,
            AccessLogSql.CursorListFirstMySql,
            AccessLogSql.CursorListAfterMySql,
        };

        Assert.IsTrue(statements.All(statement =>
            statement.Scope == SqlDataScope.HostOnly
            && !statement.Text.Contains("COUNT(", StringComparison.OrdinalIgnoreCase)
            && !statement.Text.Contains("OFFSET", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            "@CursorOccurredAtUtc",
            sqlServerFirst.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "@CursorOccurredAtUtc",
            AccessLogSql.CursorListFirstMySql.Text,
            StringComparison.Ordinal);
        foreach (var statement in new[]
                 {
                     sqlServerAfter,
                     AccessLogSql.CursorListAfterMySql,
                 })
        {
            StringAssert.Contains(
                statement.Text,
                "OccurredAtUtc < @CursorOccurredAtUtc");
            StringAssert.Contains(
                statement.Text,
                "OccurredAtUtc = @CursorOccurredAtUtc");
            StringAssert.Contains(statement.Text, "Id < @CursorId");
            StringAssert.Contains(
                statement.Text,
                "ORDER BY OccurredAtUtc DESC, Id DESC");
        }
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "auditing.cursor_access_logs.sql_server.first")]
    [DataRow(DatabaseProvider.MySql, "auditing.cursor_access_logs.my_sql.first")]
    public async Task First_cursor_batch_reads_limit_plus_one_without_count(
        DatabaseProvider provider,
        string expectedStatementName)
    {
        var first = CreateRecord(1, ReferenceUtc);
        var second = CreateRecord(2, ReferenceUtc.AddTicks(-10));
        var third = CreateRecord(3, ReferenceUtc.AddTicks(-20));
        var executor = new RecordingQueryExecutor([first, second, third]);
        var service = CreateService(provider, executor);

        var result = await service.ListCursorAsync(
            2,
            cursor: null,
            fromUtc: null,
            toUtc: null,
            httpMethod: null,
            statusCode: null,
            pathContains: null);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(2, result.Value!.Items);
        Assert.IsTrue(result.Value.HasMore);
        Assert.IsNotNull(result.Value.NextCursor);
        Assert.AreEqual(expectedStatementName, executor.Statement!.Name);
        Assert.AreEqual(3, ReadParameter<int>(executor.Parameters!, "FetchSize"));
        Assert.AreEqual(1, executor.CallCount);
        Assert.IsTrue(AccessLogCursorCodec.TryDecode(
            result.Value.NextCursor,
            CreateFilter(null),
            out var boundary));
        Assert.AreEqual(second.OccurredAtUtc, boundary.OccurredAtUtc);
        Assert.AreEqual(second.Id, boundary.Id);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "auditing.cursor_access_logs.sql_server.after")]
    [DataRow(DatabaseProvider.MySql, "auditing.cursor_access_logs.my_sql.after")]
    public async Task Next_cursor_batch_uses_decoded_boundary_and_returns_terminal_page(
        DatabaseProvider provider,
        string expectedStatementName)
    {
        var filter = CreateFilter(null);
        var boundary = new AccessLogCursorBoundary(
            ReferenceUtc,
            Guid.Parse("01984827-9c80-7000-8000-000000000010"));
        var cursor = AccessLogCursorCodec.Encode(boundary, filter);
        var row = CreateRecord(20, ReferenceUtc.AddTicks(-10));
        var executor = new RecordingQueryExecutor([row]);
        var service = CreateService(provider, executor);

        var result = await service.ListCursorAsync(
            2,
            cursor,
            fromUtc: null,
            toUtc: null,
            httpMethod: null,
            statusCode: null,
            pathContains: null);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, result.Value!.Items);
        Assert.IsFalse(result.Value.HasMore);
        Assert.IsNull(result.Value.NextCursor);
        Assert.AreEqual(expectedStatementName, executor.Statement!.Name);
        Assert.AreEqual(
            boundary.OccurredAtUtc,
            ReadParameter<DateTimeOffset>(
                executor.Parameters!,
                "CursorOccurredAtUtc"));
        Assert.AreEqual(
            boundary.Id,
            ReadParameter<Guid>(executor.Parameters!, "CursorId"));
    }

    [TestMethod]
    public async Task Invalid_cursor_returns_validation_error_without_database_call()
    {
        var executor = new RecordingQueryExecutor([]);
        var service = CreateService(DatabaseProvider.MySql, executor);

        var result = await service.ListCursorAsync(
            20,
            "invalid!",
            fromUtc: null,
            toUtc: null,
            httpMethod: null,
            statusCode: null,
            pathContains: null);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(AuditingErrorCodes.AccessLogCursorInvalid, result.Error!.Code);
        Assert.AreEqual(0, executor.CallCount);
    }

    private static HostAccessLogQueryService CreateService(
        DatabaseProvider provider,
        IQueryExecutor queryExecutor) =>
        new(
            queryExecutor,
            RejectingMultiResultQueryExecutor.Instance,
            Options.Create(new DatabaseOptions { Provider = provider }),
            new AuditingContainsTimeRangePolicy(
                Options.Create(new AuditingQueryOptions())));

    private static AccessLogCursorFilter CreateFilter(string? pathContains) =>
        new(
            FromUtc: null,
            ToUtc: null,
            HttpMethod: null,
            StatusCode: null,
            PathContains: pathContains);

    private static HostAccessLogQueryService.AccessLogRecord CreateRecord(
        int suffix,
        DateTimeOffset occurredAtUtc) =>
        new()
        {
            Id = Guid.Parse($"01984827-9c80-7000-8000-{suffix:D12}"),
            OccurredAtUtc = occurredAtUtc,
            HttpMethod = "GET",
            RequestPath = "/api/v1/settings",
            StatusCode = 200,
            DurationMs = 10,
        };

    private static T ReadParameter<T>(object parameters, string name) =>
        (T)parameters.GetType().GetProperty(name)!.GetValue(parameters)!;

    private static byte[] DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - (padded.Length % 4)) % 4);
        return Convert.FromBase64String(padded);
    }

    private static string EncodeBase64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class RecordingQueryExecutor(
        IReadOnlyList<HostAccessLogQueryService.AccessLogRecord> rows)
        : IQueryExecutor
    {
        public int CallCount { get; private set; }

        public SqlStatement? Statement { get; private set; }

        public object? Parameters { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("游标列表不得执行单行查询。");

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Statement = statement;
            Parameters = parameters;
            return Task.FromResult<IReadOnlyList<T>>(
                rows.Select(row => (T)(object)row).ToArray());
        }
    }

    private sealed class RejectingMultiResultQueryExecutor : IMultiResultQueryExecutor
    {
        public static RejectingMultiResultQueryExecutor Instance { get; } = new();

        public Task<TResult> QueryMultipleAsync<TResult>(
            SqlStatement statement,
            object? parameters,
            Func<IMultiResultReader, CancellationToken, Task<TResult>> projector,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("游标列表不得执行 COUNT 多结果查询。");
    }
}
