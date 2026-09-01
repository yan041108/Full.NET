using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

/// <summary>
/// 主机文档的内容类型稳定枚举；序列化为整型存储，新增值必须追加以保持机器码兼容。
/// </summary>
public enum HostDocumentType
{
    /// <summary>未知或未分类的文档类型。</summary>
    Unknown = 0,

    /// <summary>可通过文档预览器直接打开的非媒体文件，如 PDF、Office 文档等。</summary>
    Document = 1,

    /// <summary>图片类文件。</summary>
    Image = 2,

    /// <summary>视频类文件。</summary>
    Video = 3,

    /// <summary>音频类文件。</summary>
    Audio = 4,

    /// <summary>压缩归档文件，如 zip、rar、7z 等。</summary>
    Archive = 5,

    /// <summary>不属于上述已知类别的其他文件。</summary>
    Other = 99,
}

/// <summary>
/// 主机文档生命周期状态稳定枚举；机器码顺序不可变更，新增值只能追加。
/// </summary>
public enum HostDocumentStatus
{
    /// <summary>草稿状态，仅创建者及显式授权用户可见，不参与对外分享与统计。</summary>
    Draft = 0,

    /// <summary>已发布状态，正常参与查询、分享与统计。</summary>
    Published = 1,

    /// <summary>已归档状态，进入只读保留期，不允许修改或新增版本。</summary>
    Archived = 2,
}

/// <summary>
/// 文档-标签关联响应契约，承载标签分配的最小稳定子集。
/// </summary>
/// <param name="TagId">标签标识。</param>
/// <param name="TagName">标签展示名称。</param>
public sealed record HostDocumentTagAssignmentResponse(
    Guid TagId,
    string TagName);

/// <summary>
/// 创建主机文档项的请求契约；<c>JsonUnmappedMemberHandling.Disallow</c> 保证协议字段严格匹配。
/// </summary>
/// <remarks>
/// 契约字段顺序为机器码的一部分；新增可选字段只能追加以避免位置参数漂移。
/// </remarks>
/// <param name="Title">文档标题。</param>
/// <param name="Description">文档可选描述。</param>
/// <param name="DocumentType">文档内容类型枚举。</param>
/// <param name="Status">文档初始状态枚举。</param>
/// <param name="Sort">同分类下的排序值，升序排列。</param>
/// <param name="Thumbnail">缩略图引用地址，可为空。</param>
/// <param name="CategoryId">所属分类标识，可空表示未分类。</param>
/// <param name="TagIds">初始化分配的标签标识集合，可空表示无标签。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[method: JsonConstructor]
public sealed record CreateHostDocumentItemRequest(
    string Title,
    string? Description,
    HostDocumentType DocumentType,
    HostDocumentStatus Status,
    int Sort,
    string? Thumbnail,
    Guid? CategoryId,
    IReadOnlyList<Guid>? TagIds)
{
    /// <summary>
    /// 保留对标扩展前的构造方式，避免新增可选元数据破坏既有 .NET 调用方。
    /// </summary>
    public CreateHostDocumentItemRequest(string title, string? description)
        : this(
            title,
            description,
            HostDocumentType.Unknown,
            HostDocumentStatus.Draft,
            0,
            null,
            null,
            null)
    {
    }
}

/// <summary>
/// 更新主机文档项的请求契约；可空字段传 null 表示不修改对应列，使用乐观并发 Version 守卫。
/// </summary>
/// <remarks>
/// 契约字段顺序为机器码的一部分；新增可选字段只能追加在 Version 之前。
/// </remarks>
/// <param name="Title">文档标题，非空即覆盖原值。</param>
/// <param name="Description">文档描述，传 null 表示不修改。</param>
/// <param name="CategoryId">分类标识，传 null 表示不修改。</param>
/// <param name="Thumbnail">缩略图地址，传 null 表示不修改。</param>
/// <param name="Status">文档状态，传 null 表示不修改。</param>
/// <param name="Sort">排序值，传 null 表示不修改。</param>
/// <param name="TagIds">标签集合，传 null 表示不修改。</param>
/// <param name="Version">乐观并发版本号，必须等于当前行版本以避免丢失更新。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[method: JsonConstructor]
public sealed record UpdateHostDocumentItemRequest(
    string Title,
    string? Description,
    Guid? CategoryId,
    string? Thumbnail,
    HostDocumentStatus? Status,
    int? Sort,
    IReadOnlyList<Guid>? TagIds,
    long Version)
{
    /// <summary>
    /// 保留原更新契约的构造方式；未提供的新字段表示不修改对应值。
    /// </summary>
    public UpdateHostDocumentItemRequest(string title, string? description, long version)
        : this(title, description, null, null, null, null, null, version)
    {
    }
}

/// <summary>
/// 为主机文档追加一个新版本的请求契约；版本号由服务端单调递增。
/// </summary>
/// <remarks>
/// 契约字段顺序为机器码的一部分；新增可选字段只能追加。
/// </remarks>
/// <param name="FileId">Files 模块中已完成上传并就绪的文件标识。</param>
/// <param name="ChangeDescription">本次版本变更的可读说明，可为空。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[method: JsonConstructor]
public sealed record AddHostDocumentVersionRequest(
    Guid FileId,
    string? ChangeDescription)
{
    /// <summary>
    /// 保留原版本上传构造方式，变更说明缺省为空。
    /// </summary>
    public AddHostDocumentVersionRequest(Guid fileId)
        : this(fileId, null)
    {
    }
}

/// <summary>
/// 软删除主机文档项的请求契约，使用乐观并发 Version 守卫，避免并发删除冲突。
/// </summary>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteHostDocumentItemRequest(long Version);

/// <summary>
/// 从回收站恢复主机文档项的请求契约，使用乐观并发 Version 守卫。
/// </summary>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RestoreHostDocumentItemRequest(long Version);

/// <summary>
/// 主机文档单个版本的响应契约，用于版本列表与当前版本引用。
/// </summary>
/// <param name="Id">版本行标识。</param>
/// <param name="VersionNumber">文档内的单调递增版本号，从 1 开始。</param>
/// <param name="FileId">Files 模块中关联的就绪文件标识。</param>
/// <param name="ContentHash">文件内容摘要，供下游校验一致性，可空表示未计算。</param>
/// <param name="SizeBytes">文件字节数。</param>
/// <param name="ChangeDescription">版本变更说明，可为空。</param>
/// <param name="CreatedAtUtc">版本创建时间（UTC）。</param>
/// <param name="UploadedByUserId">上传该版本的用户标识。</param>
public sealed record HostDocumentVersionResponse(
    Guid Id,
    int VersionNumber,
    Guid FileId,
    string? ContentHash,
    long SizeBytes,
    string? ChangeDescription,
    DateTimeOffset CreatedAtUtc,
    Guid UploadedByUserId);

/// <summary>
/// 主机文档项完整响应契约，用于列表与详情；字段顺序为线格式稳定的一部分。
/// </summary>
/// <param name="Id">文档标识。</param>
/// <param name="DocumentNo">稳定可读文档编号，用于对外引用。</param>
/// <param name="Title">文档标题。</param>
/// <param name="Description">文档描述，可空。</param>
/// <param name="CategoryId">所属分类标识，可空表示未分类。</param>
/// <param name="CategoryName">所属分类展示名称，冗余投影以避免二次查询。</param>
/// <param name="CategoryColor">分类颜色，冗余投影。</param>
/// <param name="DocumentType">文档内容类型枚举。</param>
/// <param name="SizeKb">当前版本文件大小（KB，取整）。</param>
/// <param name="Thumbnail">缩略图地址，可空。</param>
/// <param name="Status">文档生命周期状态枚举。</param>
/// <param name="AccessCount">访问次数累计。</param>
/// <param name="Sort">同分类下的排序值，升序排列。</param>
/// <param name="LastAccessTime">最后访问时间，可空。</param>
/// <param name="CurrentVersion">当前生效版本的详情投影，可空表示无有效版本。</param>
/// <param name="Tags">已分配的标签集合。</param>
/// <param name="CreatedAtUtc">创建时间（UTC）。</param>
/// <param name="CreatedByUserId">创建者用户标识。</param>
/// <param name="UpdatedAtUtc">最后更新时间（UTC），可空。</param>
/// <param name="UpdatedByUserId">最后更新者用户标识，可空。</param>
/// <param name="DeletedAtUtc">软删除时间（UTC），可空表示未删除。</param>
/// <param name="DeletedByUserId">删除执行者用户标识，可空。</param>
/// <param name="Version">乐观并发版本号，用于后续更新、删除请求。</param>
public sealed record HostDocumentItemResponse(
    Guid Id,
    string DocumentNo,
    string Title,
    string? Description,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryColor,
    HostDocumentType DocumentType,
    long SizeKb,
    string? Thumbnail,
    HostDocumentStatus Status,
    int AccessCount,
    int Sort,
    DateTimeOffset? LastAccessTime,
    HostDocumentVersionResponse? CurrentVersion,
    IReadOnlyList<HostDocumentTagAssignmentResponse> Tags,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    DateTimeOffset? DeletedAtUtc,
    Guid? DeletedByUserId,
    long Version);

/// <summary>
/// 设置主机文档精确权限的请求契约；提交集合为整体覆盖，按用户幂等替换。
/// </summary>
/// <param name="DocumentId">目标文档标识。</param>
/// <param name="Permissions">权限条目集合；为空集合表示清空所有非管理员显式授权。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetHostDocumentPermissionsRequest(
    Guid DocumentId,
    IReadOnlyList<HostDocumentPermissionEntry> Permissions);

/// <summary>
/// 文档权限条目，指定单个用户在文档上的权限级别机器码。
/// </summary>
/// <param name="UserId">被授权用户标识。</param>
/// <param name="PermissionLevel">权限级别稳定机器码，如 viewer/editor/owner。</param>
public sealed record HostDocumentPermissionEntry(
    Guid UserId,
    string PermissionLevel);

/// <summary>
/// 文档权限响应契约，用于回显已保存的授权条目。
/// </summary>
/// <param name="Id">权限行标识。</param>
/// <param name="DocumentId">所属文档标识。</param>
/// <param name="UserId">被授权用户标识。</param>
/// <param name="PermissionLevel">权限级别稳定机器码。</param>
/// <param name="CreatedAtUtc">授权创建时间（UTC）。</param>
public sealed record HostDocumentPermissionResponse(
    Guid Id,
    Guid DocumentId,
    Guid UserId,
    string PermissionLevel,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// 创建主机文档匿名分享的请求契约；通过分享码对外暴露，不依赖登录态。
/// </summary>
/// <param name="DocumentId">目标文档标识。</param>
/// <param name="ValidDays">分享有效天数，从创建时刻起计算。</param>
/// <param name="Password">可选访问口令；传入后匿名访问必须提交匹配口令。</param>
/// <param name="MaxAccessCount">可选最大访问次数；到达后分享自动失效，null 表示不限制。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateHostDocumentShareRequest(
    Guid DocumentId,
    int ValidDays,
    string? Password = null,
    int? MaxAccessCount = null);

/// <summary>
/// 启用或停用文档匿名分享的请求契约，使用乐观并发 Version 守卫。
/// </summary>
/// <param name="IsEnabled">true 表示启用分享入口，false 表示停用。</param>
/// <param name="Version">乐观并发版本号，必须等于当前行版本。</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateHostDocumentShareStatusRequest(
    bool IsEnabled,
    long Version);

/// <summary>
/// 主机文档匿名分享响应契约；字段顺序为稳定机器码的一部分。
/// </summary>
/// <param name="Id">分享行标识。</param>
/// <param name="DocumentId">所属文档标识。</param>
/// <param name="ShareCode">对外使用的稳定分享码，URL 安全。</param>
/// <param name="CreatedAtUtc">分享创建时间（UTC）。</param>
/// <param name="ExpireTime">分享到期时间（UTC），由 ValidDays 推算。</param>
/// <param name="Password">始终传 null 的兼容占位，查询响应永不回显口令；实际是否存在口令由 HasPassword 指示。</param>
/// <param name="MaxAccessCount">最大访问次数限制，null 表示不限制。</param>
/// <param name="AccessCount">当前累计访问次数。</param>
/// <param name="IsEnabled">分享是否处于启用状态。</param>
/// <param name="Version">乐观并发版本号，用于后续状态变更请求。</param>
/// <param name="HasPassword">是否设置了访问口令；Password 恒为 null 时由该字段传递口令状态。</param>
[method: JsonConstructor]
public sealed record HostDocumentShareResponse(
    Guid Id,
    Guid DocumentId,
    string ShareCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpireTime,
    [property: JsonIgnore] string? Password,
    int? MaxAccessCount,
    int AccessCount,
    bool IsEnabled,
    long Version,
    bool HasPassword)
{
    /// <summary>
    /// 兼容策略：保留扩展前的旧构造签名，避免新增 Password/MaxAccessCount/
    /// AccessCount/IsEnabled/Version 等位置参数导致既有 .NET 调用方出现
    /// "构造参数数不匹配(CS8852)"编译错误。
    /// 安全说明：Password 始终传 null，查询响应永不回显口令；
    /// HasPassword 由兼容构造函数通过 !string.IsNullOrEmpty(Password) 推导，
    /// 但由于 Password 恒为 null，该构造仅用于历史 API 调用方，HasPassword 恒 false。
    /// AccessCount 默认 0、IsEnabled 默认 true、Version 默认 1。
    /// </summary>
    [Obsolete("保留用于源码兼容；建议使用带完整字段的构造函数")]
    public HostDocumentShareResponse(
        Guid id,
        Guid documentId,
        string shareCode,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expireTime)
        : this(
            id,
            documentId,
            shareCode,
            createdAtUtc,
            expireTime,
            null,
            null,
            0,
            true,
            1L,
            HasPassword: false)
    {
    }

    /// <summary>
    /// 兼容调用方：使用位置参数传入 Password 的旧签名。
    /// Password 仅用于推导 HasPassword（非空则为 true），属性本身立即被置空，
    /// 保证 [JsonIgnore] 的 Password 永远不带值对外。
    /// </summary>
    public HostDocumentShareResponse(
        Guid id,
        Guid documentId,
        string shareCode,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expireTime,
        string? Password,
        int? MaxAccessCount,
        int AccessCount,
        bool IsEnabled,
        long Version)
        : this(
            id,
            documentId,
            shareCode,
            createdAtUtc,
            expireTime,
            Password: null,
            MaxAccessCount,
            AccessCount,
            IsEnabled,
            Version,
            HasPassword: !string.IsNullOrEmpty(Password))
    {
    }
}

/// <summary>匿名分享访问请求：通过 POST 提交口令，规避 GET 产生副作用与缓存泄漏。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccessHostDocumentShareRequest(
    string? Password = null);

/// <summary>匿名分享访问响应：仅回传文档定位、标题元数据与实际下载/展示入口，不含口令相关字段。</summary>
public sealed record HostDocumentShareAccessResponse(
    Guid ShareId,
    Guid DocumentId,
    string ShareCode,
    string Title,
    string? FileName,
    string? MimeType,
    long FileSizeBytes,
    bool HasPassword,
    int AccessCountRemaining);

/// <summary>
/// 文档统计汇总响应契约，提供总量级别的聚合指标。
/// </summary>
/// <param name="TotalItems">未删除文档总数。</param>
/// <param name="TotalVersions">所有版本记录总数。</param>
/// <param name="TotalSizeKb">所有当前版本合计大小（KB，取整）。</param>
/// <param name="TotalSizeInfo">已本地化的可读大小描述，如 "12.4 GB"。</param>
public sealed record HostDocumentStatisticsSummaryResponse(
    long TotalItems,
    long TotalVersions,
    long TotalSizeKb,
    string TotalSizeInfo);

/// <summary>
/// 按文件扩展名分组的文档统计条目。
/// </summary>
/// <param name="Extension">标准化文件扩展名（含前导点），null 表示未识别类型。</param>
/// <param name="Count">该扩展名对应的文档数量。</param>
/// <param name="TotalSizeKb">该扩展名下文档合计大小（KB，取整）。</param>
public sealed record HostDocumentStatisticsTypeItem(
    string? Extension,
    long Count,
    long TotalSizeKb);

/// <summary>
/// 按分类分组的文档统计条目。
/// </summary>
/// <param name="CategoryId">分类标识，null 表示未分类桶。</param>
/// <param name="CategoryName">分类展示名称，用于界面直接渲染。</param>
/// <param name="Count">该分类下的文档数量。</param>
public sealed record HostDocumentStatisticsCategoryItem(
    Guid? CategoryId,
    string? CategoryName,
    long Count);

/// <summary>
/// 主机文档完整统计响应契约，用于后台统计看板。
/// </summary>
/// <param name="Summary">总量级汇总。</param>
/// <param name="ByType">按扩展名分组的统计集合。</param>
/// <param name="ByCategory">按分类分组的统计集合。</param>
/// <param name="ShareCount">当前有效的匿名分享总数。</param>
/// <param name="TodayAccessCount">今日访问次数（自然日，UTC 分区）。</param>
/// <param name="TodayDownloadCount">今日下载次数（自然日，UTC 分区）。</param>
/// <param name="TodayCreatedCount">今日新创建文档数（自然日，UTC 分区）。</param>
/// <param name="RecycleBinCount">当前回收站中未清理的软删除文档数。</param>
public sealed record HostDocumentStatisticsResponse(
    HostDocumentStatisticsSummaryResponse Summary,
    IReadOnlyList<HostDocumentStatisticsTypeItem> ByType,
    IReadOnlyList<HostDocumentStatisticsCategoryItem> ByCategory,
    long ShareCount,
    long TodayAccessCount,
    long TodayDownloadCount,
    long TodayCreatedCount,
    long RecycleBinCount);
