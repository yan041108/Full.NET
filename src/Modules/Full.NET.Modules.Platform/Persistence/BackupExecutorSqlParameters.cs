using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Platform.Persistence;

/// <summary>授权备份执行器 SQL 参数工厂。</summary>
internal static class BackupExecutorSqlParameters
{
    /// <summary>创建具名参数字典。</summary>
    /// <param name="values">键值对序列。</param>
    /// <returns>可用于 Dapper 执行的参数字典。</returns>
    public static Dictionary<string, object?> Create(params (string Key, object? Value)[] values)
    {
        var parameters = new Dictionary<string, object?>(values.Length, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            parameters[key] = value;
        }

        return parameters;
    }
}
