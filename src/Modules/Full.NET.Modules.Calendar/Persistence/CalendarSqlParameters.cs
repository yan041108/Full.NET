namespace Full.NET.Modules.Calendar.Persistence;

/// <summary>Calendar Native AOT SQL 参数工厂；固定键名参数袋避免匿名类型进入原生执行路径。</summary>
internal static class CalendarSqlParameters
{
    /// <summary>创建与 SQL 占位符对齐的参数字典。</summary>
    /// <param name="pairs">参数名与值对。</param>
    /// <returns>Ordinal 比较键名的参数字典。</returns>
    public static Dictionary<string, object?> Create(
        params (string Name, object? Value)[] pairs)
    {
        var parameters = new Dictionary<string, object?>(pairs.Length, StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
        {
            parameters[name] = value;
        }

        return parameters;
    }
}
