namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 定义生成器可为字段选择的稳定控件种类。
/// </summary>
public enum FullNetColumnControlKind
{
    /// <summary>单行文本。</summary>
    Text = 0,

    /// <summary>多行文本。</summary>
    Textarea = 1,

    /// <summary>数值输入。</summary>
    Number = 2,

    /// <summary>布尔开关。</summary>
    Switch = 3,

    /// <summary>UTC 日期时间。</summary>
    DateTime = 4,

    /// <summary>UUID 只读或选择。</summary>
    Uuid = 5,
}

/// <summary>
/// 定义列表查询可使用的稳定比较方式。
/// </summary>
public enum FullNetColumnQueryKind
{
    /// <summary>不参与查询。</summary>
    None = 0,

    /// <summary>等值比较。</summary>
    Equals = 1,

    /// <summary>字符串包含。</summary>
    Contains = 2,

    /// <summary>闭区间范围。</summary>
    Range = 3,
}

/// <summary>
/// 保存不影响物理列名的展示、表单与查询元数据。
/// 确定性：进入模板哈希的字段顺序固定；显式声明后模板必须按字面量渲染，禁止二次推导导致漂移。
/// FAIL-closed：若 Required=true 但物理列 IsNullable=true，生成器立即抛异常；禁止表单契约与物理约束冲突。
/// </summary>
/// <param name="ControlKind">工作台与生成页面使用的控件。</param>
/// <param name="ShowInList">是否出现在列表列。</param>
/// <param name="IncludeInCreate">是否进入创建表单。</param>
/// <param name="IncludeInUpdate">是否进入更新表单。</param>
/// <param name="Required">表单是否必填；不得覆盖数据库可空性。</param>
/// <param name="Sortable">列表是否允许排序。</param>
/// <param name="Queryable">列表是否允许过滤。</param>
/// <param name="QueryKind">过滤比较方式。</param>
/// <param name="Unique">写入时是否做同作用域唯一校验。</param>
/// <param name="IncludeInImportExport">是否纳入导入导出列。</param>
public sealed record FullNetColumnUi(
    [property: System.ComponentModel.Description("工作台与生成页面使用的控件；枚举值稳定，未知控件 FAIL-closed 回退为 Text。")]
    FullNetColumnControlKind ControlKind,
    [property: System.ComponentModel.Description("是否出现在列表列；系统列除 Id 外默认隐藏，显式声明优先。")]
    bool ShowInList,
    [property: System.ComponentModel.Description("是否进入创建表单；CreatedAtUtc/Version 等系统列必须为 false。")]
    bool IncludeInCreate,
    [property: System.ComponentModel.Description("是否进入更新表单；Id/DeletedAtUtc 等系统列必须为 false。")]
    bool IncludeInUpdate,
    [property: System.ComponentModel.Description("表单是否必填；不得覆盖数据库可空性。物理可空但 Required=true 时生成器 FAIL-closed。")]
    bool Required,
    [property: System.ComponentModel.Description("列表是否允许排序；用于生成 a-sortable-column 与 OrderBy 白名单。")]
    bool Sortable,
    [property: System.ComponentModel.Description("列表是否允许过滤；为 false 时 QueryKind 忽略，不进入查询契约。")]
    bool Queryable,
    [property: System.ComponentModel.Description("过滤比较方式；None 即使 Queryable=true 也不生成过滤入口。")]
    FullNetColumnQueryKind QueryKind,
    [property: System.ComponentModel.Description("写入时是否做同作用域唯一校验；生成时将在 CommandValidator 内追加 Duplicate 检测。")]
    bool Unique,
    [property: System.ComponentModel.Description("是否纳入导入导出列；用于 CSV/Xlsx 列头生成与导入列校验。")]
    bool IncludeInImportExport)
{
    /// <summary>
    /// 按列名与标量类型推导默认展示元数据，避免把系统列送进创建表单。
    /// </summary>
    public static FullNetColumnUi DefaultFor(
        string databaseName,
        FullNetScalarType scalarType,
        bool isNullable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        var isSystemColumn = IsSystemColumn(databaseName);
        var controlKind = scalarType switch
        {
            FullNetScalarType.Boolean => FullNetColumnControlKind.Switch,
            FullNetScalarType.DateTimeUtc => FullNetColumnControlKind.DateTime,
            FullNetScalarType.Int32 or FullNetScalarType.Int64
                or FullNetScalarType.Decimal => FullNetColumnControlKind.Number,
            FullNetScalarType.Uuid => FullNetColumnControlKind.Uuid,
            _ => FullNetColumnControlKind.Text,
        };
        var queryKind = scalarType switch
        {
            FullNetScalarType.String => FullNetColumnQueryKind.Contains,
            FullNetScalarType.DateTimeUtc => FullNetColumnQueryKind.Range,
            FullNetScalarType.Boolean or FullNetScalarType.Uuid =>
                FullNetColumnQueryKind.Equals,
            FullNetScalarType.Int32 or FullNetScalarType.Int64
                or FullNetScalarType.Decimal => FullNetColumnQueryKind.Equals,
            _ => FullNetColumnQueryKind.None,
        };
        return new FullNetColumnUi(
            controlKind,
            ShowInList: !isSystemColumn || databaseName == "Id",
            IncludeInCreate: !isSystemColumn,
            IncludeInUpdate: !isSystemColumn,
            Required: !isNullable && !isSystemColumn,
            Sortable: !isSystemColumn || databaseName == "Id",
            Queryable: !isSystemColumn,
            queryKind,
            Unique: false,
            IncludeInImportExport: !isSystemColumn);
    }

    private static bool IsSystemColumn(string databaseName) =>
        databaseName is "Id"
            or "TenantId"
            or "Version"
            or "CreatedAtUtc"
            or "CreatedByUserId"
            or "UpdatedAtUtc"
            or "UpdatedByUserId"
            or "DeletedAtUtc"
            or "DeletedByUserId"
            or "IsDeleted";
}
