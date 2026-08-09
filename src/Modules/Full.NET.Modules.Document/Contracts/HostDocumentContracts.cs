using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

public enum HostDocumentType
{
    Unknown = 0,
    Document = 1,
    Image = 2,
    Video = 3,
    Audio = 4,
    Archive = 5,
    Other = 99,
}

public enum HostDocumentStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

public sealed record HostDocumentTagAssignmentResponse(
    Guid TagId,
    string TagName);

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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteHostDocumentItemRequest(long Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RestoreHostDocumentItemRequest(long Version);

public sealed record HostDocumentVersionResponse(
    Guid Id,
    int VersionNumber,
    Guid FileId,
    string? ContentHash,
    long SizeBytes,
    string? ChangeDescription,
    DateTimeOffset CreatedAtUtc,
    Guid UploadedByUserId);

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

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetHostDocumentPermissionsRequest(
    Guid DocumentId,
    IReadOnlyList<HostDocumentPermissionEntry> Permissions);

public sealed record HostDocumentPermissionEntry(
    Guid UserId,
    string PermissionLevel);

public sealed record HostDocumentPermissionResponse(
    Guid Id,
    Guid DocumentId,
    Guid UserId,
    string PermissionLevel,
    DateTimeOffset CreatedAtUtc);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateHostDocumentShareRequest(
    Guid DocumentId,
    int ValidDays,
    string? Password = null,
    int? MaxAccessCount = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateHostDocumentShareStatusRequest(
    bool IsEnabled,
    long Version);

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

public sealed record HostDocumentStatisticsSummaryResponse(
    long TotalItems,
    long TotalVersions,
    long TotalSizeKb,
    string TotalSizeInfo);

public sealed record HostDocumentStatisticsTypeItem(
    string? Extension,
    long Count,
    long TotalSizeKb);

public sealed record HostDocumentStatisticsCategoryItem(
    Guid? CategoryId,
    string? CategoryName,
    long Count);

public sealed record HostDocumentStatisticsResponse(
    HostDocumentStatisticsSummaryResponse Summary,
    IReadOnlyList<HostDocumentStatisticsTypeItem> ByType,
    IReadOnlyList<HostDocumentStatisticsCategoryItem> ByCategory,
    long ShareCount,
    long TodayAccessCount,
    long TodayDownloadCount,
    long TodayCreatedCount,
    long RecycleBinCount);
