namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 保存同一字段已经确认的数据库、CLR 与 JSON 名称，模板不得再次推导名称。
/// 确定性：列的三项名称一旦进入生成流水线即冻结为字面量；模板层若再推导名称将导致产物哈希漂移。
/// FAIL-closed：列集合在 Schema 构造阶段必须包含且仅包含一个 "Id" 主键列；缺列、重复列或非 Id 主键立即抛异常。
/// </summary>
/// <param name="DatabaseName">PascalCase 数据库列名。</param>
/// <param name="ClrPropertyName">PascalCase CLR 属性名。</param>
/// <param name="JsonPropertyName">camelCase JSON 属性名。</param>
/// <param name="ScalarType">跨数据库逻辑标量类型。</param>
/// <param name="IsNullable">字段是否允许空值。</param>
/// <param name="MaxLength">字符串最大长度；非字符串必须为空。</param>
/// <param name="NumericPrecision">Decimal 总有效位数；非 Decimal 必须为空。</param>
/// <param name="NumericScale">Decimal 小数位数；非 Decimal 必须为空。</param>
/// <param name="Ui">可选展示元数据；空值表示生成时按列名与类型推导，且不进入旧模板哈希。</param>
public sealed record FullNetColumn(
    [property: System.ComponentModel.Description("PascalCase 数据库列名；与 INFORMATION_SCHEMA.COLUMNS.COLUMN_NAME 字面量一致。")]
    string DatabaseName,
    [property: System.ComponentModel.Description("PascalCase CLR 属性名；用于 C# Response/Request record 以及 Sql 常量成员命名。")]
    string ClrPropertyName,
    [property: System.ComponentModel.Description("camelCase JSON 属性名；用于 System.Text.Json 序列化属性名与 TypeScript 接口字段名。")]
    string JsonPropertyName,
    [property: System.ComponentModel.Description("跨数据库逻辑标量类型；映射时禁止方言细节泄漏到生成层，未知类型 FAIL-closed 抛 NotSupportedException。")]
    FullNetScalarType ScalarType,
    [property: System.ComponentModel.Description("字段是否允许空值；决定 C# 可空后缀与 TypeScript | null 联合；表单 Required 标记不覆盖此物理约束。")]
    bool IsNullable = false,
    [property: System.ComponentModel.Description("字符串最大长度；非字符串必须为空。映射时超过 int.MaxValue 或 <=0 FAIL-closed 抛异常，禁止截断。")]
    int? MaxLength = null,
    [property: System.ComponentModel.Description("Decimal 总有效位数；非 Decimal 必须为空。仅在物理列类型为 decimal/numeric 时赋值。")]
    int? NumericPrecision = null,
    [property: System.ComponentModel.Description("Decimal 小数位数；非 Decimal 必须为空。与 NumericPrecision 成对出现。")]
    int? NumericScale = null,
    [property: System.ComponentModel.Description("可选展示元数据；空值表示生成时按列名与类型推导，且不进入旧模板哈希。显式声明后将作为确定性哈希输入。")]
    FullNetColumnUi? Ui = null)
{
    /// <summary>
    /// 解析展示元数据；未声明时按物理列推导，保证生成器始终看到完整 UI 决策。
    /// </summary>
    public FullNetColumnUi ResolvedUi =>
        Ui ?? FullNetColumnUi.DefaultFor(DatabaseName, ScalarType, IsNullable);
}
