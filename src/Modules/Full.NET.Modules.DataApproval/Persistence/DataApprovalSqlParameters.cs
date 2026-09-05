namespace Full.NET.Modules.DataApproval.Persistence;

/// <summary>DataApproval Native AOT SQL 参数工厂。</summary>
internal static class DataApprovalSqlParameters
{
    /// <summary>创建具名 SQL 参数字典。</summary>
    /// <param name="pairs">参数名与值对。</param>
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
