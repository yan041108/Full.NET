namespace Full.NET.Modules.SerialNumbers.Persistence;

/// <summary>
/// SerialNumbers Native AOT SQL 参数工厂。参数名使用固定序数比较，确保运行时参数袋与 SQL 占位符稳定对齐。
/// </summary>
internal static class SerialNumbersSqlParameters
{
    public static Dictionary<string, object?> Create(
        params (string Name, object? Value)[] pairs)
    {
        var parameters = new Dictionary<string, object?>(
            pairs.Length,
            StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
        {
            parameters[name] = value;
        }

        return parameters;
    }
}
