namespace Full.NET.Data.Abstractions;

/// <summary>
/// 在一次数据库往返中顺序读取具有共同参数和一致性窗口的多个结果集。
/// </summary>
public interface IMultiResultQueryExecutor
{
    /// <summary>
    /// 执行多结果集语句，并在 Reader 生命周期内完成投影。
    /// </summary>
    /// <remarks>
    /// 投影器必须按顺序消费全部结果集，不得并行读取，也不得保存 Reader 供方法返回后使用。
    /// </remarks>
    /// <param name="statement">包含显式数据作用域和全部结果集的 SQL 语句。</param>
    /// <param name="parameters">传递给 Dapper 的参数对象；无参数时可为空。</param>
    /// <param name="projector">在底层 Reader 释放前按顺序物化全部结果集的投影器。</param>
    /// <param name="cancellationToken">用于取消连接、命令和投影流程的令牌。</param>
    /// <returns>投影器在同一 Reader 生命周期内生成的聚合结果。</returns>
    /// <exception cref="InvalidOperationException">投影器返回时仍有结果集未消费。</exception>
    Task<TResult> QueryMultipleAsync<TResult>(
        SqlStatement statement,
        object? parameters,
        Func<IMultiResultReader, CancellationToken, Task<TResult>> projector,
        CancellationToken cancellationToken = default);
}
