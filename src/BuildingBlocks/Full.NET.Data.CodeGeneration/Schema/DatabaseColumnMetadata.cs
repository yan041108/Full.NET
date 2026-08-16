namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 保存 INFORMATION_SCHEMA 读出的原始列形态，尚未经过 CRUD 不变量校验。
/// </summary>
public sealed record DatabaseColumnMetadata(
    string Name,
    string DataType,
    string ColumnType,
    bool IsNullable,
    long? MaxLength,
    int OrdinalPosition,
    int? NumericPrecision = null,
    int? NumericScale = null);
