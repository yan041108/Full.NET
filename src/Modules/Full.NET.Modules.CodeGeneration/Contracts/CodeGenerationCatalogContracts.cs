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
}

/// <summary>
/// 表示当前进程数据库中的一张基础表。
/// </summary>
public sealed record CodeGenerationCatalogTableResponse(string TableName);

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
