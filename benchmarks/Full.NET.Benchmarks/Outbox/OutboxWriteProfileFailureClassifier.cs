using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Benchmarks.Outbox;

public sealed record OutboxWriteProfileFailureClassification(
    string Reason,
    string DatabaseErrorCode,
    bool WindowOwned);

public sealed record OutboxWriteProfileFailureSummary(
    string Reason,
    string DatabaseErrorCode,
    bool WindowOwned,
    long Count);

/// <summary>
/// 将 Profile 异常收敛为稳定、低基数证据，不把异常消息或 SQL 写入工件。
/// </summary>
public static class OutboxWriteProfileFailureClassifier
{
    public static OutboxWriteProfileFailureClassification Classify(
        Exception exception,
        bool windowCancellationRequested)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var source = Unwrap(exception);
        return new OutboxWriteProfileFailureClassification(
            GetReason(exception, source),
            GetDatabaseErrorCode(source),
            windowCancellationRequested);
    }

    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current is DataCommandException { InnerException: not null })
        {
            current = current.InnerException;
        }

        return current;
    }

    private static string GetReason(Exception original, Exception source) =>
        original switch
        {
            DataCommandException
            {
                Kind: DataCommandFailureKind.UniqueConstraint,
            } => "unique_constraint",
            _ => source switch
            {
                OperationCanceledException => "canceled",
                SqlException { Number: 1205 } => "deadlock",
                SqlException { Number: -2 } => "command_timeout",
                MySqlException
                {
                    ErrorCode:
                        MySqlErrorCode.LockDeadlock
                        or MySqlErrorCode.UserLockDeadlock,
                } => "deadlock",
                MySqlException
                {
                    ErrorCode: MySqlErrorCode.LockWaitTimeout,
                } => "lock_wait_timeout",
                TimeoutException => "timeout",
                DbException => "database_error",
                _ => "application_error",
            },
        };

    private static string GetDatabaseErrorCode(Exception exception) =>
        exception switch
        {
            SqlException sqlException =>
                sqlException.Number.ToString(CultureInfo.InvariantCulture),
            MySqlException mySqlException =>
                ((int)mySqlException.ErrorCode).ToString(
                    CultureInfo.InvariantCulture),
            DbException databaseException =>
                databaseException.ErrorCode.ToString(CultureInfo.InvariantCulture),
            _ => "not_available",
        };
}
