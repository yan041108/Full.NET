using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 将 Provider 异常收敛为不泄漏数据库实现的稳定数据边界错误。
/// </summary>
internal static class DataCommandExceptionMapper
{
    /// <summary>
    /// 尝试把数据库 Provider 异常转换为稳定的数据命令失败类别。
    /// </summary>
    /// <param name="exception">待识别的数据库异常。</param>
    /// <param name="mapped">识别成功后返回的数据命令异常。</param>
    /// <returns>识别成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    public static bool TryMap(
        Exception exception,
        out DataCommandException mapped)
    {
        var kind = exception switch
        {
            SqlException sqlException => ClassifySqlServer(sqlException.Number),
            MySqlException mySqlException => ClassifyMySql(mySqlException.ErrorCode),
            _ => null,
        };
        if (kind is not null)
        {
            mapped = new DataCommandException(
                kind.Value,
                exception);
            return true;
        }

        mapped = null!;
        return false;
    }

    /// <summary>
    /// 按 SQL Server 错误编号识别稳定的数据命令失败类别。
    /// </summary>
    /// <param name="errorNumber">SQL Server 错误编号。</param>
    /// <returns>已识别的失败类别；未知编号返回 <see langword="null"/>。</returns>
    internal static DataCommandFailureKind? ClassifySqlServer(int errorNumber) =>
        errorNumber switch
        {
            2601 or 2627 => DataCommandFailureKind.UniqueConstraint,
            1205 => DataCommandFailureKind.Deadlock,
            _ => null,
        };

    /// <summary>
    /// 按 MySQL 错误码识别稳定的数据命令失败类别。
    /// </summary>
    /// <param name="errorCode">MySQL Provider 错误码。</param>
    /// <returns>已识别的失败类别；未知错误码返回 <see langword="null"/>。</returns>
    internal static DataCommandFailureKind? ClassifyMySql(MySqlErrorCode errorCode) =>
        errorCode switch
        {
            MySqlErrorCode.DuplicateKeyEntry => DataCommandFailureKind.UniqueConstraint,
            MySqlErrorCode.LockDeadlock or MySqlErrorCode.UserLockDeadlock =>
                DataCommandFailureKind.Deadlock,
            _ => null,
        };
}
