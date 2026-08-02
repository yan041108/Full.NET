using System.Text.Json.Serialization;

namespace Full.NET.Modules.Document.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateHostDocumentTagRequest(string Name);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateHostDocumentTagRequest(string Name, long Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeleteHostDocumentTagRequest(long Version);

public sealed record HostDocumentTagResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    long Version);
