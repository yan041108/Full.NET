using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI SQL 参数工厂。</summary>
internal static class AiSqlParameters
{
    /// <summary>创建命名参数集合。</summary>
    public static IReadOnlyDictionary<string, object?> Create(
        params (string Name, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
}
