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
    /// 仅在调用令牌已取消且 Provider 返回取消候选码时，收敛为统一取消异常。
    /// </summary>
    /// <param name="exception">保留为内部异常的原始 Provider 诊断。</param>
    /// <param name="cancellationToken">当前数据库操作使用的调用令牌。</param>
    /// <param name="mapped">携带原异常和调用令牌的取消异常。</param>
    /// <returns>符合受限取消特征时返回 true；其他故障保留原有传播方式。</returns>
    /// <remarks>候选码不证明因果或写入回滚；调用方仍须遵守事务和结果不确定性边界。</remarks>
    public static bool TryMapCancellation(
        Exception exception,
        CancellationToken cancellationToken,
        out OperationCanceledException mapped)
    {
        if (cancellationToken.IsCancellationRequested && IsCancellationCandidate(exception))
        {
            mapped = new OperationCanceledException(
                "The database operation was canceled.", exception, cancellationToken);
            return true;
        }

        mapped = null!;
        return false;
    }

    private static bool IsCancellationCandidate(Exception exception)
    {
        if (exception is MySqlException mySqlException)
        {
            return mySqlException.ErrorCode == MySqlErrorCode.QueryInterrupted;
        }

        if (exception is not SqlException sqlException || sqlException.Errors.Count == 0)
        {
            return false;
        }

        // 停止在途查询可能返回零码或批次中止 3980；后者也可能是会话繁忙，因此仍必须有调用取消。
        // 错误集合出现语法、权限、超时等明确故障时，不能被同时发生的取消掩盖。
        foreach (SqlError error in sqlException.Errors)
        {
            if (error.Number is not (0 or 3980)) return false;
        }

        return true;
    }

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
