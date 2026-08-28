namespace Full.NET.Modules.Organization.Persistence;

/// <summary>Organization Native AOT SQL 参数工厂；固定键名避免匿名类型进入原生执行路径。</summary>
internal static class OrganizationSqlParameters
{
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
