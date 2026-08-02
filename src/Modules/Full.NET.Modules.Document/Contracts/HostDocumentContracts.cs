using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateHostDocumentItemRequest(string Title, string? Description);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateHostDocumentItemRequest(string Title, string? Description, long Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddHostDocumentVersionRequest(Guid FileId);

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
    DateTimeOffset CreatedAtUtc,
    Guid UploadedByUserId);

public sealed record HostDocumentItemResponse(
    Guid Id,
    string Title,
    string? Description,
    Guid? CategoryId,
    HostDocumentVersionResponse? CurrentVersion,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByUserId,
    long Version);
