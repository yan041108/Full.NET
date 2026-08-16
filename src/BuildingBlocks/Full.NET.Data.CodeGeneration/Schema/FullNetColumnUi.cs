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
    FullNetColumnControlKind ControlKind,
    bool ShowInList,
    bool IncludeInCreate,
    bool IncludeInUpdate,
    bool Required,
    bool Sortable,
    bool Queryable,
    FullNetColumnQueryKind QueryKind,
    bool Unique,
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
