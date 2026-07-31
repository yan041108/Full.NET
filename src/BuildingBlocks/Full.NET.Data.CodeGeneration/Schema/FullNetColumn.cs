namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 保存同一字段已经确认的数据库、CLR 与 JSON 名称，模板不得再次推导名称。
/// </summary>
/// <param name="DatabaseName">PascalCase 数据库列名。</param>
/// <param name="ClrPropertyName">PascalCase CLR 属性名。</param>
/// <param name="JsonPropertyName">camelCase JSON 属性名。</param>
/// <param name="ScalarType">跨数据库逻辑标量类型。</param>
/// <param name="IsNullable">字段是否允许空值。</param>
/// <param name="MaxLength">字符串最大长度；非字符串必须为空。</param>
/// <param name="NumericPrecision">Decimal 总有效位数；非 Decimal 必须为空。</param>
/// <param name="NumericScale">Decimal 小数位数；非 Decimal 必须为空。</param>
public sealed record FullNetColumn(
    string DatabaseName,
    string ClrPropertyName,
    string JsonPropertyName,
    FullNetScalarType ScalarType,
    bool IsNullable = false,
    int? MaxLength = null,
    int? NumericPrecision = null,
    int? NumericScale = null);
