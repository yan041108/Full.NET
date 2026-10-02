using System.Text.Json.Serialization;

namespace Full.NET.Modules.CodeGeneration.Contracts;

/// <summary>
/// 定义 Host 只读数据库目录的权限边界。
/// </summary>
public static class CodeGenerationCatalogPermissions
{
    public const string Read = "codegen.catalog.read";
}

/// <summary>
/// 定义 Host 数据库目录的稳定错误码。
/// </summary>
public static class CodeGenerationCatalogErrorCodes
{
    public const string InvalidTable = "codegen.catalog.invalid_table";

    public const string TableNotFound = "codegen.catalog.table_not_found";

    public const string ObjectNotFound = "codegen.catalog.object_not_found";

    public const string ViewMigrationDraft = "codegen.catalog.view_migration_draft";
}

/// <summary>
/// 目录对象种类稳定机器码。
/// </summary>
public static class CodeGenerationCatalogObjectKinds
{
    public const string Table = "table";

    public const string View = "view";
}

/// <summary>
/// 表示当前进程数据库中的一张基础表。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="TableName">基础表名称；来自 INFORMATION_SCHEMA，命名因数据库而异。</param>
public sealed record CodeGenerationCatalogTableResponse(string TableName);

/// <summary>
/// 表示当前进程数据库中的一个只读目录对象（表或视图）。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ObjectName">目录对象名称（表或视图）。</param>
/// <param name="ObjectKind">对象种类稳定机器码，取值见 <see cref="CodeGenerationCatalogObjectKinds"/>。</param>
public sealed record CodeGenerationCatalogObjectResponse(
    string ObjectName,
    string ObjectKind);

/// <summary>
/// 表示只读 INFORMATION_SCHEMA 列元数据，供检查页展示。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ColumnName">列名；来自 INFORMATION_SCHEMA，命名因数据库而异。</param>
/// <param name="DataType">基础数据类型名（如 varchar、bigint）。</param>
/// <param name="ColumnType">完整列类型描述，含长度或精度信息。</param>
/// <param name="IsNullable">列是否允许 NULL。</param>
/// <param name="MaxLength">字符或二进制列的最大长度；非字符/二进制列为 <see langword="null"/>。</param>
/// <param name="OrdinalPosition">列在表中的序号位置（从 1 开始）。</param>
/// <param name="NumericPrecision">数值列的精度；非数值列为 <see langword="null"/>。</param>
/// <param name="NumericScale">数值列的小数位数；非数值列为 <see langword="null"/>。</param>
public sealed record CodeGenerationCatalogMetadataColumnResponse(
    string ColumnName,
    string DataType,
    string ColumnType,
    bool IsNullable,
    long? MaxLength,
    int OrdinalPosition,
    int? NumericPrecision,
    int? NumericScale);

/// <summary>
/// 表示表或视图的原始列元数据快照。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ObjectName">目录对象名称。</param>
/// <param name="ObjectKind">对象种类稳定机器码，取值见 <see cref="CodeGenerationCatalogObjectKinds"/>。</param>
/// <param name="Columns">列元数据列表；按 OrdinalPosition 升序排列。</param>
public sealed record CodeGenerationCatalogMetadataResponse(
    string ObjectName,
    string ObjectKind,
    IReadOnlyList<CodeGenerationCatalogMetadataColumnResponse> Columns);

/// <summary>
/// 表示基于基础表生成迁移草案的请求。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="TableName">目标基础表名称；必须为当前库中已存在的表。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationCatalogMigrationDraftRequest(string TableName);

/// <summary>
/// 表示双库 DbUp 迁移草案文本，不执行 DDL。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="TableName">目标基础表名称。</param>
/// <param name="SqlServerDraft">SQL Server 迁移草案文本；不执行 DDL，仅供审阅。</param>
/// <param name="MySqlDraft">MySQL 迁移草案文本；不执行 DDL，仅供审阅。</param>
/// <param name="Warnings">生成过程中检测到的兼容性或风险提示。</param>
public sealed record CodeGenerationCatalogMigrationDraftResponse(
    string TableName,
    string SqlServerDraft,
    string MySqlDraft,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 表示一张基础表的默认可生成列配置。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="TableName">目标基础表名称。</param>
/// <param name="Columns">默认可生成列配置列表。</param>
/// <param name="SkippedColumnNames">被跳过生成的列名列表；通常因类型不支持或显式排除。</param>
public sealed record CodeGenerationCatalogColumnListResponse(
    string TableName,
    IReadOnlyList<CodeGenerationPreviewColumnRequest> Columns,
    IReadOnlyList<string> SkippedColumnNames);

/// <summary>
/// 表示用当前库列集合对照已编辑列配置的同步请求。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="TableName">目标基础表名称。</param>
/// <param name="Columns">已编辑列配置集合；与当前库列集合对照同步。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationCatalogColumnSyncRequest(
    string TableName,
    IReadOnlyList<CodeGenerationPreviewColumnRequest> Columns);

/// <summary>
/// 表示列同步结果：新增列带默认 UI，已有列保留人工 UI，删除列只返回名称。
/// </summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="TableName">目标基础表名称。</param>
/// <param name="Columns">同步后的列配置合并结果。</param>
/// <param name="AddedColumnNames">当前库新增的列名列表；带默认 UI。</param>
/// <param name="RemovedColumnNames">当前库已删除的列名列表；仅返回名称。</param>
/// <param name="SkippedColumnNames">被跳过的列名列表；通常因类型不支持或显式排除。</param>
public sealed record CodeGenerationCatalogColumnSyncResponse(
    string TableName,
    IReadOnlyList<CodeGenerationPreviewColumnRequest> Columns,
    IReadOnlyList<string> AddedColumnNames,
    IReadOnlyList<string> RemovedColumnNames,
    IReadOnlyList<string> SkippedColumnNames);
