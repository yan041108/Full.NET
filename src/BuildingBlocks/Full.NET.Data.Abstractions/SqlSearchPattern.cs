namespace Full.NET.Data.Abstractions;

/// <summary>为显式 SQL 的 LIKE 条件生成字面包含模式；语句使用 ESCAPE '!'.</summary>
public static class SqlSearchPattern
{
    /// <summary>保留用户输入的字面语义，避免通配符扩大候选集合。</summary>
    /// <param name="value">搜索文本；空白视为未筛选。</param>
    /// <returns>已转义的包含模式，或未筛选时的空值。</returns>
    public static string? ContainsLiteral(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : $"%{value.Trim().Replace("!", "!!", StringComparison.Ordinal).Replace("%", "!%", StringComparison.Ordinal).Replace("_", "!_", StringComparison.Ordinal).Replace("[", "![", StringComparison.Ordinal)}%";
}
