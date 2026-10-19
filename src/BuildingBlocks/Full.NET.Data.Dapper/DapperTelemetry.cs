using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

/// <summary>参数化 SQL 操作分类；用于指标维度，不暴露原始 SQL 文本。</summary>
internal enum DapperOperation
{
    QuerySingle,
    Query,
    Execute,
    QueryMultiple,
}

/// <summary>记录 SQL 执行次数、失败与耗时直方图；与连接池指标分离观测。</summary>
internal static class DapperTelemetry
{
    internal const string MeterName = "fullnet.data.dapper";

    private static readonly Meter Meter = new(MeterName);
    // 独立总量只按语句、提供程序与操作分组；首次失败不会新增系列并破坏采样前置基线。
    // 与 executions 一样包含完成的失败/取消尝试，不表示这些尝试均已到达数据库。
    private static readonly Counter<long> CompletedOperations = Meter.CreateCounter<long>(
        "fullnet.data.sql.completed_operations",
        unit: "{execution}",
        description: "已完成的参数化 SQL 操作尝试总数，包含成功、失败和取消。");
    private static readonly Counter<long> Executions = Meter.CreateCounter<long>(
        "fullnet.data.sql.executions",
        unit: "{execution}",
        description: "已完成的参数化 SQL 执行次数。");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "fullnet.data.sql.failures",
        unit: "{failure}",
        description: "失败或取消的参数化 SQL 执行次数。");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "fullnet.data.sql.duration",
        unit: "ms",
        description: "参数化 SQL 操作总耗时，包含连接准入、打开、命令执行、结果消费与连接归还。");
    private static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>(
        "fullnet.data.sql.command.duration",
        unit: "ms",
        description: "连接已取得后的命令准备、执行与结果消费耗时；不包含连接准入、打开和归还，不等于数据库服务端 CPU 时间。");

    /// <summary>在取得连接后创建值类型计时范围，退出时早于连接归还；JIT/AOT 共用边界。</summary>
    internal static CommandTiming BeginCommand(string statementName, DatabaseProvider provider, DapperOperation operation) =>
        new(statementName, provider, operation);

    /// <summary>固定语句、提供程序与操作维度的命令阶段计时；异常与取消同样记录已进入的尝试。</summary>
    internal readonly struct CommandTiming(string statementName, DatabaseProvider provider, DapperOperation operation) : IDisposable
    {
        private readonly long _startedAt = Stopwatch.GetTimestamp();
        public void Dispose()
        {
            var tags = new TagList
            {
                { "statement_name", statementName },
                { "provider", GetProviderName(provider) },
                { "operation", GetOperationName(operation) },
            };
            CommandDuration.Record(Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds, tags);
        }
    }

    internal static void Record(
        string statementName,
        DatabaseProvider provider,
        DapperOperation operation,
        TimeSpan elapsed,
        Exception? exception)
    {
        var tags = new TagList
        {
            { "statement_name", statementName },
            { "provider", GetProviderName(provider) },
            { "operation", GetOperationName(operation) },
        };

        CompletedOperations.Add(1, tags);
        tags.Add("outcome", GetOutcome(exception));
        tags.Add("failure_reason", GetFailureReason(exception));
        Executions.Add(1, tags);
        Duration.Record(elapsed.TotalMilliseconds, tags);
        if (exception is not null)
        {
            Failures.Add(1, tags);
        }
    }

    private static string GetProviderName(DatabaseProvider provider) =>
        provider switch
        {
            DatabaseProvider.SqlServer => "sql_server",
            DatabaseProvider.MySql => "my_sql",
            _ => "unknown",
        };

    private static string GetOperationName(DapperOperation operation) =>
        operation switch
        {
            DapperOperation.QuerySingle => "query_single",
            DapperOperation.Query => "query",
            DapperOperation.Execute => "execute",
            DapperOperation.QueryMultiple => "query_multiple",
            _ => "unknown",
        };

    private static string GetOutcome(Exception? exception) =>
        exception switch
        {
            null => "success",
            OperationCanceledException => "canceled",
            _ => "failure",
        };

    private static string GetFailureReason(Exception? exception) =>
        exception switch
        {
            null => "none",
            OperationCanceledException => "canceled",
            SqlException { Number: 1205 } => "deadlock",
            SqlException { Number: -2 } => "command_timeout",
            MySqlException
            {
                ErrorCode:
                    MySqlErrorCode.LockDeadlock
                    or MySqlErrorCode.UserLockDeadlock
            } => "deadlock",
            MySqlException
            {
                ErrorCode: MySqlErrorCode.LockWaitTimeout
            } => "lock_wait_timeout",
            System.Data.Common.DbException => "database_error",
            _ => "application_error",
        };

    internal static string GetDatabaseErrorCode(Exception exception) =>
        exception switch
        {
            SqlException sqlException =>
                sqlException.Number.ToString(CultureInfo.InvariantCulture),
            MySqlException mySqlException =>
                ((int)mySqlException.ErrorCode).ToString(
                    CultureInfo.InvariantCulture),
            System.Data.Common.DbException databaseException =>
                databaseException.ErrorCode.ToString(
                    CultureInfo.InvariantCulture),
            _ => "not_available",
        };
}
