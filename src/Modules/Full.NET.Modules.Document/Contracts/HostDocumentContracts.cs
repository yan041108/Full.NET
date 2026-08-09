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
    long Version);

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
