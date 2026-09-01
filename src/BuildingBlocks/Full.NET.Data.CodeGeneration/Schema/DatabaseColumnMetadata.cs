namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 保存 INFORMATION_SCHEMA 读出的原始列形态，尚未经过 CRUD 不变量校验。
/// </summary>
/// <remarks>
/// 该 record 只是只读目录快照，不进入生成哈希。一旦被
/// <see cref="DatabaseColumnMetadataMapper"/> 映射为 <see cref="FullNetColumn"/>
/// 后，后续步骤不再引用本类型，避免方言细节泄漏到模板层。
/// </remarks>
/// <param name="Name">
/// INFORMATION_SCHEMA.COLUMNS.COLUMN_NAME 字面量，PascalCase 或 snake_case
/// 取决于数据库默认排序规则；映射层会进一步规范化为 PascalCase CLR 名。
/// </param>
/// <param name="DataType">
/// 逻辑数据类型，如 varchar、int、bigint、decimal、datetime、bit。
/// 不含长度、unsigned 等修饰，用于进入 <see cref="FullNetScalarType"/> 映射决策。
/// </param>
/// <param name="ColumnType">
/// 完整物理列类型，如 varchar(255)、decimal(18,4)、bigint unsigned。
/// 仅在 DataType 不足以区分长度/精度时作为补充依据解析 MaxLength/NumericPrecision。
/// </param>
/// <param name="IsNullable">
/// 列是否允许空值；YES/NO 被映射为 true/false。映射时该值是最终契约可空性的下限，
/// 表单层 Required 标记不得覆盖物理约束。
/// </param>
/// <param name="MaxLength">
/// 字符列最大字节数/字符数；非字符列为 null。MySQL varchar 返回字符数，
/// SQL Server nvarchar 返回字节数（2×字符数）；映射层会按 Provider 差异统一。
/// </param>
/// <param name="OrdinalPosition">
/// INFORMATION_SCHEMA 序数位置，从 1 开始。映射后列集合按此排序，保证生成的
/// Response/Request record 参数顺序与物理列顺序一致，避免契约顺序漂移。
/// </param>
/// <param name="NumericPrecision">
/// Decimal/Numeric 的总有效位数；非数值列为 null。与 NumericScale 成对出现。
/// </param>
/// <param name="NumericScale">
/// Decimal/Numeric 的小数位数；非数值列为 null。单独为 null 而 Precision 非空
/// 视为 Provider 异常，映射层 FAIL-closed 抛 NotSupportedException。
/// </param>
public sealed record DatabaseColumnMetadata(
    string Name,
    string DataType,
    string ColumnType,
    bool IsNullable,
    long? MaxLength,
    int OrdinalPosition,
    int? NumericPrecision = null,
    int? NumericScale = null);
