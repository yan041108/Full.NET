namespace Full.NET.Abstractions.Results;

/// <summary>有界游标页，不推算全局总数；下一页位置仅由当前来源解释。</summary>
/// <typeparam name="T">来源拥有的只读摘要。</typeparam>
/// <param name="Items">当前页摘要。</param>
/// <param name="NextCursor">存在下一页时的分页位置，否则为空。</param>
/// <param name="HasMore">当前查询是否还存在后续行。</param>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
