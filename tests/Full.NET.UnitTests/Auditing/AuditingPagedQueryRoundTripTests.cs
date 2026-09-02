using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.Auditing.Features.QueryHostAccessLogs;
using Full.NET.Modules.Auditing.Features.QueryHostExceptionLogs;
using Full.NET.Modules.Auditing.Features.QueryHostOperationLogs;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditingPagedQueryRoundTripTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "auditing.page_access_logs.sql_server")]
    [DataRow(DatabaseProvider.MySql, "auditing.page_access_logs.my_sql")]
    public async Task Access_log_page_uses_one_multi_result_round_trip(
        DatabaseProvider provider,
        string expectedStatementName)
    {
        var id = Guid.CreateVersion7();
        var executor = new RecordingMultiResultQueryExecutor(
            37L,
            new HostAccessLogQueryService.AccessLogRecord
            {
                Id = id,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                HttpMethod = "GET",
                RequestPath = "/api/v1/status",
                StatusCode = 200,
                DurationMs = 12,
        });
        var service = new HostAccessLogQueryService(
            RejectingQueryExecutor.Instance,
            executor,
            Options.Create(new DatabaseOptions { Provider = provider }),
            CreateContainsTimeRangePolicy());

        var result = await service.ListAsync(
            3,
            20,
            new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
            " get ",
            500,
            " settings ");

        AssertPagedResult(result.Value!, id, 37L);
        AssertSingleRoundTrip(
            executor,
            provider,
            expectedStatementName,
            40,
            20,
            "@StatusCode");
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "auditing.page_operation_logs.sql_server")]
    [DataRow(DatabaseProvider.MySql, "auditing.page_operation_logs.my_sql")]
    public async Task Operation_log_page_uses_one_multi_result_round_trip(
        DatabaseProvider provider,
        string expectedStatementName)
    {
        var id = Guid.CreateVersion7();
        var executor = new RecordingMultiResultQueryExecutor(
            41L,
            new HostOperationLogQueryService.OperationLogRecord
            {
                Id = id,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                ActionKey = "settings.config_entries.update",
                HttpMethod = "PUT",
                RequestPath = "/api/v1/settings/config-entries/value",
                StatusCode = 200,
                DurationMs = 18,
                Succeeded = true,
        });
        var service = new HostOperationLogQueryService(
            RejectingQueryExecutor.Instance,
            executor,
            Options.Create(new DatabaseOptions { Provider = provider }),
            CreateContainsTimeRangePolicy());

        var result = await service.ListAsync(
            3,
            20,
            new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
            " put ",
            true,
            " settings ");

        AssertPagedResult(result.Value!, id, 41L);
        AssertSingleRoundTrip(
            executor,
            provider,
            expectedStatementName,
            40,
            20,
            "@Succeeded");
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "auditing.page_exception_logs.sql_server")]
    [DataRow(DatabaseProvider.MySql, "auditing.page_exception_logs.my_sql")]
    public async Task Exception_log_page_uses_one_multi_result_round_trip(
        DatabaseProvider provider,
        string expectedStatementName)
    {
        var id = Guid.CreateVersion7();
        var executor = new RecordingMultiResultQueryExecutor(
            43L,
            new HostExceptionLogQueryService.ExceptionLogRecord
            {
                Id = id,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                ExceptionType = "System.InvalidOperationException",
                Message = "sensitive",
                StackTrace = "sensitive",
                HttpMethod = "GET",
                RequestPath = "/api/v1/auditing/exception-probes",
        });
        var service = new HostExceptionLogQueryService(
            RejectingQueryExecutor.Instance,
            executor,
            Options.Create(new DatabaseOptions { Provider = provider }),
            CreateContainsTimeRangePolicy());

        var result = await service.ListAsync(
            3,
            20,
            new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero),
            " InvalidOperationException ",
            " probes ");

        AssertPagedResult(result.Value!, id, 43L);
        AssertSingleRoundTrip(
            executor,
            provider,
            expectedStatementName,
            40,
            20,
            "@ExceptionTypeContains");
    }

    [TestMethod]
    public void SqlServer_page_shapes_are_bounded_unique_and_parameterized()
    {
        var accessStatements = Enumerable.Range(0, 32)
            .Select(mask => AccessLogSql.CreatePageFilteredSqlServer(
                HasBit(mask, 0),
                HasBit(mask, 1),
                HasBit(mask, 2),
                HasBit(mask, 3),
                HasBit(mask, 4)))
            .ToArray();
        var operationStatements = Enumerable.Range(0, 32)
            .Select(mask => OperationLogSql.CreatePageFilteredSqlServer(
                HasBit(mask, 0),
                HasBit(mask, 1),
                HasBit(mask, 2),
                HasBit(mask, 3),
                HasBit(mask, 4)))
            .ToArray();
        var exceptionStatements = Enumerable.Range(0, 16)
            .Select(mask => ExceptionLogSql.CreatePageFilteredSqlServer(
                HasBit(mask, 0),
                HasBit(mask, 1),
                HasBit(mask, 2),
                HasBit(mask, 3)))
            .ToArray();

        AssertSqlServerShapes(
            accessStatements,
            32,
            "auditing.page_access_logs.sql_server");
        AssertSqlServerShapes(
            operationStatements,
            32,
            "auditing.page_operation_logs.sql_server");
        AssertSqlServerShapes(
            exceptionStatements,
            16,
            "auditing.page_exception_logs.sql_server");
        StringAssert.Contains(
            accessStatements[^1].Text,
            "StatusCode = @StatusCode");
        StringAssert.Contains(
            operationStatements[^1].Text,
            "Succeeded = @Succeeded");
        StringAssert.Contains(
            exceptionStatements[^1].Text,
            "ExceptionType) > 0");
    }

    private static void AssertPagedResult<T>(
        Full.NET.Abstractions.Results.PagedResult<T> result,
        Guid expectedId,
        long expectedTotal)
    {
        Assert.AreEqual(3, result.Page);
        Assert.AreEqual(20, result.PageSize);
        Assert.AreEqual(expectedTotal, result.Total);
        Assert.HasCount(1, result.Items);
        Assert.AreEqual(
            expectedId,
            (Guid)typeof(T).GetProperty("Id")!.GetValue(result.Items[0])!);
    }

    private static void AssertSingleRoundTrip(
        RecordingMultiResultQueryExecutor executor,
        DatabaseProvider provider,
        string expectedStatementName,
        int expectedOffset,
        int expectedPageSize,
        string providerSpecificParameter)
    {
        Assert.AreEqual(1, executor.CallCount);
        Assert.AreEqual(expectedStatementName, executor.Statement!.Name);
        Assert.AreEqual(SqlDataScope.HostOnly, executor.Statement.Scope);
        Assert.AreEqual(
            expectedOffset,
            ReadParameter<int>(executor.Parameters!, "Offset"));
        Assert.AreEqual(
            expectedPageSize,
            ReadParameter<int>(executor.Parameters!, "PageSize"));
        if (provider == DatabaseProvider.SqlServer)
        {
            Assert.DoesNotContain(
                " IS NULL OR ",
                executor.Statement.Text,
                StringComparison.Ordinal);
            StringAssert.Contains(
                executor.Statement.Text,
                "OccurredAtUtc >= @FromUtc");
            StringAssert.Contains(
                executor.Statement.Text,
                providerSpecificParameter);
        }
        else
        {
            StringAssert.Contains(
                executor.Statement.Text,
                $"({providerSpecificParameter} IS NULL OR");
        }
    }

    private static void AssertSqlServerShapes(
        IReadOnlyList<SqlStatement> statements,
        int expectedCount,
        string expectedStatementName)
    {
        Assert.HasCount(expectedCount, statements);
        Assert.HasCount(
            expectedCount,
            statements.Select(statement => statement.Text).Distinct().ToArray());
        Assert.IsTrue(
            statements.All(
                statement => statement.Name == expectedStatementName
                    && statement.Scope == SqlDataScope.HostOnly
                    && !statement.Text.Contains(
                        " IS NULL OR ",
                        StringComparison.Ordinal)));
    }

    private static bool HasBit(int value, int bit) =>
        (value & (1 << bit)) != 0;

    private static AuditingContainsTimeRangePolicy CreateContainsTimeRangePolicy() =>
        new(Options.Create(new AuditingQueryOptions()));

    private static T ReadParameter<T>(object parameters, string name) =>
        ReadSqlParameter<T>(parameters, name);

    private sealed class RecordingMultiResultQueryExecutor(
        long total,
        object row) : IMultiResultQueryExecutor
    {
        public int CallCount { get; private set; }

        public SqlStatement? Statement { get; private set; }

        public object? Parameters { get; private set; }

        public Task<TResult> QueryMultipleAsync<TResult>(
            SqlStatement statement,
            object? parameters,
            Func<IMultiResultReader, CancellationToken, Task<TResult>> projector,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Statement = statement;
            Parameters = parameters;
            return projector(
                new ScriptedMultiResultReader(total, row),
                cancellationToken);
        }
    }

    private sealed class ScriptedMultiResultReader(
        long total,
        object row) : IMultiResultReader
    {
        public Task<T?> ReadSingleOrDefaultAsync<T>() =>
            Task.FromResult((T?)(object)total);

        public Task<IReadOnlyList<T>> ReadAsync<T>() =>
            Task.FromResult<IReadOnlyList<T>>([(T)row]);
    }

    private sealed class RejectingQueryExecutor : IQueryExecutor
    {
        public static RejectingQueryExecutor Instance { get; } = new();

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "分页列表不得再发起普通单结果查询。");

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "分页列表不得再发起普通列表查询。");
    }
}
