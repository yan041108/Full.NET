using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>Reporting SQL 参数工厂。</summary>
internal static class ReportingSqlParameters
{
    /// <summary>创建命名参数集合。</summary>
    /// <param name="values">键值对。</param>
    /// <returns>参数集合。</returns>
    public static IReadOnlyDictionary<string, object?> Create(
        params (string Name, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
}
