namespace Full.NET.Modules.Notifications.Contracts;

public static class AnnouncementStatuses
{
    public const string Draft = "draft";

    public const string Published = "published";
}

public sealed record HostAnnouncementResponse(
    Guid Id,
    string Title,
    string Content,
    string Status,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

public sealed record CreateHostAnnouncementRequest(
    string Title,
    string Content);

public sealed record UpdateHostAnnouncementRequest(
    string Title,
    string Content,
    int Version);

public sealed record PublishHostAnnouncementRequest(int Version);
