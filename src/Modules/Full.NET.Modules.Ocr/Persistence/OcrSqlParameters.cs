using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ocr.Persistence;

internal static class OcrSqlParameters
{
    public static IReadOnlyDictionary<string, object?> Create(
        params (string Name, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
}
