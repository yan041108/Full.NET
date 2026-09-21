namespace Full.NET.Modules.Regions.Contracts;

/// <summary>行政区域导入合并模式。</summary>
public static class AdministrativeRegionImportMergeModes
{
    /// <summary>按编码合并：新增与更新，不删除既有节点。</summary>
    public const string Merge = "merge";

    /// <summary>替换模式：导入集为权威快照，删除未出现在导入中的节点。</summary>
    public const string Replace = "replace";
}

/// <summary>行政区域响应契约。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Id">区域标识。</param>
/// <param name="ParentId">父级区域标识；根节点为 null。</param>
/// <param name="Code">稳定区域编码。</param>
/// <param name="Name">名称。</param>
/// <param name="ShortName">简称。</param>
/// <param name="MergerName">合并全称路径。</param>
/// <param name="ZipCode">邮政编码。</param>
/// <param name="CityCode">城市编码。</param>
/// <param name="Level">层级，取值 1–5。</param>
/// <param name="RegionType">区域类型。</param>
/// <param name="PinYin">拼音。</param>
/// <param name="Longitude">经度。</param>
/// <param name="Latitude">纬度。</param>
/// <param name="DisplayOrder">显示顺序。</param>
/// <param name="Remark">备注。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">更新时间（UTC）。</param>
/// <param name="Version">乐观并发版本号。</param>
public sealed record AdministrativeRegionResponse(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    string? ShortName,
    string? MergerName,
    string? ZipCode,
    string? CityCode,
    int Level,
    string? RegionType,
    string? PinYin,
    decimal? Longitude,
    decimal? Latitude,
    int DisplayOrder,
    string? Remark,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>级联子节点响应，不含子孙嵌套。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Id">区域标识。</param>
/// <param name="ParentId">父级区域标识。</param>
/// <param name="Code">稳定区域编码。</param>
/// <param name="Name">名称。</param>
/// <param name="Level">层级。</param>
/// <param name="DisplayOrder">显示顺序。</param>
/// <param name="HasChildren">是否存在直接子节点。</param>
public sealed record AdministrativeRegionChildResponse(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    int Level,
    int DisplayOrder,
    bool HasChildren);

/// <summary>行政区域树节点响应。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Id">区域标识。</param>
/// <param name="ParentId">父级区域标识。</param>
/// <param name="Code">稳定区域编码。</param>
/// <param name="Name">名称。</param>
/// <param name="Level">层级。</param>
/// <param name="DisplayOrder">显示顺序。</param>
/// <param name="Children">子节点集合。</param>
public sealed record AdministrativeRegionTreeNodeResponse(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    int Level,
    int DisplayOrder,
    IReadOnlyList<AdministrativeRegionTreeNodeResponse> Children);

/// <summary>数据集清单响应。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Id">清单记录标识。</param>
/// <param name="DatasetKey">数据集键。</param>
/// <param name="DatasetVersion">数据集版本标签。</param>
/// <param name="SourceDigest">来源摘要。</param>
/// <param name="RecordCount">导入记录数。</param>
/// <param name="AppliedAtUtc">应用时间（UTC）。</param>
/// <param name="AppliedByUserId">应用人用户标识。</param>
public sealed record AdministrativeRegionDatasetManifestResponse(
    Guid Id,
    string DatasetKey,
    string DatasetVersion,
    string SourceDigest,
    int RecordCount,
    DateTimeOffset AppliedAtUtc,
    Guid AppliedByUserId);

/// <summary>创建行政区域请求。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="ParentId">父级区域标识；null 表示创建顶级区域。</param>
/// <param name="Code">稳定区域编码；创建后不可改名。</param>
/// <param name="Name">区域名称。</param>
/// <param name="ShortName">区域简称，可空。</param>
/// <param name="MergerName">合并全称路径，可空。</param>
/// <param name="ZipCode">邮政编码，可空。</param>
/// <param name="CityCode">城市编码，可空。</param>
/// <param name="Level">层级，取值 1-5。</param>
/// <param name="RegionType">区域类型，可空。</param>
/// <param name="PinYin">拼音，可空。</param>
/// <param name="Longitude">经度，可空。</param>
/// <param name="Latitude">纬度，可空。</param>
/// <param name="DisplayOrder">显示顺序。</param>
/// <param name="Remark">备注，可空。</param>
public sealed record CreateAdministrativeRegionRequest(
    Guid? ParentId,
    string Code,
    string Name,
    string? ShortName,
    string? MergerName,
    string? ZipCode,
    string? CityCode,
    int Level,
    string? RegionType,
    string? PinYin,
    decimal? Longitude,
    decimal? Latitude,
    int DisplayOrder,
    string? Remark);

/// <summary>更新行政区域请求；<paramref name="Version"/> 用作 CAS 并发守卫。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="ParentId">父级区域标识；null 表示提升为顶级。</param>
/// <param name="Name">区域名称。</param>
/// <param name="ShortName">简称，可空。</param>
/// <param name="MergerName">合并全称路径，可空。</param>
/// <param name="ZipCode">邮政编码，可空。</param>
/// <param name="CityCode">城市编码，可空。</param>
/// <param name="Level">层级，取值 1-5。</param>
/// <param name="RegionType">区域类型，可空。</param>
/// <param name="PinYin">拼音，可空。</param>
/// <param name="Longitude">经度，可空。</param>
/// <param name="Latitude">纬度，可空。</param>
/// <param name="DisplayOrder">显示顺序。</param>
/// <param name="Remark">备注，可空。</param>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
public sealed record UpdateAdministrativeRegionRequest(
    Guid? ParentId,
    string Name,
    string? ShortName,
    string? MergerName,
    string? ZipCode,
    string? CityCode,
    int Level,
    string? RegionType,
    string? PinYin,
    decimal? Longitude,
    decimal? Latitude,
    int DisplayOrder,
    string? Remark,
    int Version);

/// <summary>删除行政区域请求；删除节点及其全部子孙节点。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Version">客户端感知的乐观并发版本号。</param>
public sealed record DeleteAdministrativeRegionRequest(int Version);

/// <summary>导入行政区域数据集的单条记录。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Code">稳定区域编码，用于合并时主键匹配。</param>
/// <param name="ParentCode">父级区域编码；null 表示顶级。</param>
/// <param name="Name">区域名称。</param>
/// <param name="ShortName">简称，可空。</param>
/// <param name="MergerName">合并全称路径，可空。</param>
/// <param name="ZipCode">邮政编码，可空。</param>
/// <param name="CityCode">城市编码，可空。</param>
/// <param name="Level">层级，取值 1-5。</param>
/// <param name="RegionType">区域类型，可空。</param>
/// <param name="PinYin">拼音，可空。</param>
/// <param name="Longitude">经度，可空。</param>
/// <param name="Latitude">纬度，可空。</param>
/// <param name="DisplayOrder">显示顺序，可空表示沿用导入值。</param>
public sealed record ImportAdministrativeRegionItem(
    string Code,
    string? ParentCode,
    string Name,
    string? ShortName,
    string? MergerName,
    string? ZipCode,
    string? CityCode,
    int Level,
    string? RegionType,
    string? PinYin,
    decimal? Longitude,
    decimal? Latitude,
    int? DisplayOrder);

/// <summary>导入行政区域数据集请求。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="DatasetKey">数据集键，标识同一来源的数据集。</param>
/// <param name="DatasetVersion">数据集版本标签，与 DatasetKey 共同唯一标识一次快照。</param>
/// <param name="SourceDigest">来源摘要，用于幂等校验避免重复导入。</param>
/// <param name="MergeMode">合并模式稳定机器码，取值自 AdministrativeRegionImportMergeModes。</param>
/// <param name="Items">待导入的记录集合；空集合仅写入清单不变更数据。</param>
public sealed record ImportAdministrativeRegionsRequest(
    string DatasetKey,
    string DatasetVersion,
    string SourceDigest,
    string MergeMode,
    IReadOnlyList<ImportAdministrativeRegionItem> Items);

/// <summary>导入差异预览中新增项摘要。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Code">新增区域稳定编码。</param>
/// <param name="Name">区域名称。</param>
/// <param name="Level">层级，取值 1-5。</param>
public sealed record ImportAdministrativeRegionAddedSummary(string Code, string Name, int Level);

/// <summary>导入差异预览中更新项摘要。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Code">更新区域稳定编码。</param>
/// <param name="Name">区域名称。</param>
/// <param name="ChangedFields">发生变更的字段名集合，供前端高亮。</param>
public sealed record ImportAdministrativeRegionUpdatedSummary(
    string Code,
    string Name,
    IReadOnlyList<string> ChangedFields);

/// <summary>导入差异预览中移除项摘要（仅 replace 模式）。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Code">移除区域稳定编码。</param>
/// <param name="Name">区域名称。</param>
public sealed record ImportAdministrativeRegionRemovedSummary(string Code, string Name);

/// <summary>导入差异预览响应。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="Added">新增项摘要集合。</param>
/// <param name="Updated">更新项摘要集合。</param>
/// <param name="Removed">移除项摘要集合，仅 replace 模式非空。</param>
/// <param name="SkippedCount">因校验失败跳过的记录数。</param>
public sealed record ImportAdministrativeRegionsPreviewResponse(
    IReadOnlyList<ImportAdministrativeRegionAddedSummary> Added,
    IReadOnlyList<ImportAdministrativeRegionUpdatedSummary> Updated,
    IReadOnlyList<ImportAdministrativeRegionRemovedSummary> Removed,
    int SkippedCount);

/// <summary>导入应用结果响应。</summary>
/// <remarks>
/// 字段顺序与命名为稳定机器码的一部分；发布后不可改名或删除，新增字段只能追加到末尾。
/// </remarks>
/// <param name="AddedCount">实际新增记录数。</param>
/// <param name="UpdatedCount">实际更新记录数。</param>
/// <param name="RemovedCount">实际移除记录数，仅 replace 模式非零。</param>
/// <param name="SkippedCount">跳过记录数。</param>
/// <param name="Manifest">应用后写入的数据集清单，用于审计与回溯。</param>
public sealed record ImportAdministrativeRegionsApplyResponse(
    int AddedCount,
    int UpdatedCount,
    int RemovedCount,
    int SkippedCount,
    AdministrativeRegionDatasetManifestResponse Manifest);
