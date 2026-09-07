namespace Full.NET.Modules.Regions.Persistence;

/// <summary>Regions 模块自有具名参数工厂，不暴露第三方参数容器。</summary>
internal static class RegionsSqlParameters
{
    /// <summary>创建具名参数集合。</summary>
    /// <param name="values">固定参数名称及值。</param>
    public static Dictionary<string, object?> Create(params (string Name, object? Value)[] values)
    {
        var parameters = new Dictionary<string, object?>(values.Length, StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            parameters.Add(name, value);
        }

        return parameters;
    }
}
