using Full.NET.Data.Abstractions;

/// <summary>Ocr 模块 Dapper 参数工厂；统一命名与空值语义。</summary>
namespace Full.NET.Modules.Ocr.Persistence;

internal static class OcrSqlParameters
{
    public static IReadOnlyDictionary<string, object?> Create(
        params (string Name, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
}
