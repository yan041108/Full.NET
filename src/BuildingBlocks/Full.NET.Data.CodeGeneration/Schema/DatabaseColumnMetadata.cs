namespace Full.NET.Data.CodeGeneration.Schema;

internal sealed record DatabaseColumnMetadata(
    string Name,
    string DataType,
    string ColumnType,
    bool IsNullable,
    long? MaxLength,
    int OrdinalPosition,
    int? NumericPrecision = null,
    int? NumericScale = null);
