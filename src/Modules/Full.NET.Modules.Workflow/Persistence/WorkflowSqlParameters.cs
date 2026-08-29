namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>Workflow Native AOT SQL 参数工厂；固定键名参数袋避免匿名类型进入原生执行路径。</summary>
internal static class WorkflowSqlParameters
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
