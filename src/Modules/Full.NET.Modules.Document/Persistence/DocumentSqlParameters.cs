namespace Full.NET.Modules.Document.Persistence;

/// <summary>
/// Document Native AOT SQL 参数工厂。固定键名参数袋避免匿名类型在原生编译后进入反射绑定。
/// </summary>
internal static class DocumentSqlParameters
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
