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
public sealed record CodeGenerationCatalogTableResponse(string TableName);

/// <summary>
/// 表示当前进程数据库中的一个只读目录对象（表或视图）。
/// </summary>
public sealed record CodeGenerationCatalogObjectResponse(
    string ObjectName,
    string ObjectKind);

/// <summary>
/// 表示只读 INFORMATION_SCHEMA 列元数据，供检查页展示。
/// </summary>
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
public sealed record CodeGenerationCatalogMetadataResponse(
    string ObjectName,
    string ObjectKind,
    IReadOnlyList<CodeGenerationCatalogMetadataColumnResponse> Columns);

/// <summary>
/// 表示基于基础表生成迁移草案的请求。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationCatalogMigrationDraftRequest(string TableName);

/// <summary>
/// 表示双库 DbUp 迁移草案文本，不执行 DDL。
/// </summary>
public sealed record CodeGenerationCatalogMigrationDraftResponse(
    string TableName,
    string SqlServerDraft,
    string MySqlDraft,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 表示一张基础表的默认可生成列配置。
/// </summary>
public sealed record CodeGenerationCatalogColumnListResponse(
    string TableName,
    IReadOnlyList<CodeGenerationPreviewColumnRequest> Columns,
    IReadOnlyList<string> SkippedColumnNames);

/// <summary>
/// 表示用当前库列集合对照已编辑列配置的同步请求。
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CodeGenerationCatalogColumnSyncRequest(
    string TableName,
    IReadOnlyList<CodeGenerationPreviewColumnRequest> Columns);

/// <summary>
/// 表示列同步结果：新增列带默认 UI，已有列保留人工 UI，删除列只返回名称。
/// </summary>
public sealed record CodeGenerationCatalogColumnSyncResponse(
    string TableName,
    IReadOnlyList<CodeGenerationPreviewColumnRequest> Columns,
    IReadOnlyList<string> AddedColumnNames,
    IReadOnlyList<string> RemovedColumnNames,
    IReadOnlyList<string> SkippedColumnNames);
