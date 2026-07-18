namespace Full.NET.Data.Abstractions;

/// <summary>
/// 按 SQL 声明顺序读取多个结果集，不向业务层暴露 Dapper 或 ADO.NET Reader。
/// </summary>
public interface IMultiResultReader
{
    /// <summary>
    /// 读取下一个结果集，并要求该结果集最多包含一行。
    /// </summary>
    /// <returns>唯一行；结果集为空时返回默认值。</returns>
    Task<T?> ReadSingleOrDefaultAsync<T>();

    /// <summary>
    /// 读取并物化下一个结果集的全部行。
    /// </summary>
    /// <returns>按数据库返回顺序物化的只读行集合。</returns>
    Task<IReadOnlyList<T>> ReadAsync<T>();
}
