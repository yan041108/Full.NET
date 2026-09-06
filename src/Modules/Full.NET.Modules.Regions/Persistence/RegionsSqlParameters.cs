using Dapper;

namespace Full.NET.Modules.Regions.Persistence;

/// <summary>Regions 模块 Dapper 参数工厂。</summary>
internal static class RegionsSqlParameters
{
    /// <summary>创建具名参数集合。</summary>
    public static DynamicParameters Create(params (string Name, object? Value)[] values)
    {
        var parameters = new DynamicParameters();
        foreach (var (name, value) in values)
        {
            parameters.Add(name, value);
        }

        return parameters;
    }
}
