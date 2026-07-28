using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace Full.NET.Data.Dapper;

internal static partial class DapperLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Debug,
        Message = "Executed SQL {StatementName} on {Provider} in {ElapsedMilliseconds} ms")]
    public static partial void StatementExecuted(
        ILogger logger,
        string statementName,
        DatabaseProvider provider,
        double elapsedMilliseconds);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Failed SQL {StatementName} on {Provider} in {ElapsedMilliseconds} ms with database error code {DatabaseErrorCode}")]
    public static partial void StatementFailed(
        ILogger logger,
        string statementName,
        DatabaseProvider provider,
        double elapsedMilliseconds,
        string databaseErrorCode,
        Exception exception);
}
