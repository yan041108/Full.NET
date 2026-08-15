namespace Full.NET.Abstractions.Results;

/// <summary>
/// 表示分页查询的结果载体，包含当前页数据及用于分页计算的元数据。
/// </summary>
/// <remarks>
/// <see cref="Total"/> 为全部匹配记录数，用于计算总页数；
/// <see cref="Items"/> 仅包含当前页的数据子集，且基于 1-based 页码约定。
/// </remarks>
/// <typeparam name="T">分页项的元素类型。</typeparam>
public sealed record PagedResult<T>(
    /// <summary>
    /// 当前页的数据项集合；当无匹配记录时返回空集合，而非 <see langword="null"/>。
    /// </summary>
    IReadOnlyList<T> Items,
    /// <summary>
    /// 当前页码，从 1 开始。
    /// </summary>
    int Page,
    /// <summary>
    /// 每页记录数，即分页大小；应始终为正整数。
    /// </summary>
    int PageSize,
    /// <summary>
    /// 全部匹配记录的总数；用于客户端计算总页数与分页控件渲染。
    /// </summary>
    long Total);
