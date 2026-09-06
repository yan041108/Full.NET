using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.K3Cloud.Persistence;

/// <summary>K3Cloud SQL 参数工厂。</summary>
internal static class K3CloudSqlParameters
{
    public static IReadOnlyDictionary<string, object?> Create(
        params (string Name, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
}
