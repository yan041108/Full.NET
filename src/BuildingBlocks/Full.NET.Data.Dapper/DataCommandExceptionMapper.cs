using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 将 Provider 异常收敛为不泄漏数据库实现的稳定数据边界错误。
/// </summary>
internal static class DataCommandExceptionMapper
{
    public static bool TryMap(
        Exception exception,
        out DataCommandException mapped)
    {
        if (exception is SqlException { Number: 2601 or 2627 }
            || exception is MySqlException
            {
                ErrorCode: MySqlErrorCode.DuplicateKeyEntry,
            })
        {
            mapped = new DataCommandException(
                DataCommandFailureKind.UniqueConstraint,
                exception);
            return true;
        }

        mapped = null!;
        return false;
    }
}
