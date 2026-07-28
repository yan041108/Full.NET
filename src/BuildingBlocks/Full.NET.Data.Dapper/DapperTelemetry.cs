using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

internal enum DapperOperation
{
    QuerySingle,
    Query,
    Execute,
    QueryMultiple,
}

internal static class DapperTelemetry
{
    internal const string MeterName = "fullnet.data.dapper";

    private static readonly Meter Meter = new(MeterName);
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
        description: "参数化 SQL 执行与结果读取或写入的总耗时。");

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
            { "outcome", GetOutcome(exception) },
            { "failure_reason", GetFailureReason(exception) },
        };

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
