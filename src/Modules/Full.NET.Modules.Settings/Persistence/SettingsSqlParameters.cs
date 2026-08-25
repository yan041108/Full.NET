namespace Full.NET.Modules.Settings.Persistence;

/// <summary>
/// Settings Native AOT SQL 参数工厂。匿名对象无法被 Dapper AOT 展开，必须使用字典键名对齐占位符。
/// </summary>
internal static class SettingsSqlParameters
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
