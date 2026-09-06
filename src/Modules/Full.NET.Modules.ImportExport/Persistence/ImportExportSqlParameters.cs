namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>ImportExport Native AOT SQL 参数工厂。</summary>
internal static class ImportExportSqlParameters
{
    public static Dictionary<string, object?> Create(params (string Name, object? Value)[] pairs)
    {
        var parameters = new Dictionary<string, object?>(pairs.Length, StringComparer.Ordinal);
        foreach (var (name, value) in pairs)
        {
            parameters[name] = value;
        }

        return parameters;
    }
}
