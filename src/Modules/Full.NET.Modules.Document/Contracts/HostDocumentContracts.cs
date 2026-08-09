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
    IReadOnlyList<Guid>? TagIds);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateHostDocumentItemRequest(
    string Title,
    string? Description,
    Guid? CategoryId,
    string? Thumbnail,
    HostDocumentStatus? Status,
    int? Sort,
    IReadOnlyList<Guid>? TagIds,
    long Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddHostDocumentVersionRequest(
    Guid FileId,
    string? ChangeDescription);

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
    string? Password,
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
