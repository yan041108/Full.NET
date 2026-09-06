using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.GoView.Persistence;

namespace Full.NET.Modules.GoView.Features.ManageProjects;

/// <summary>GoView 大屏项目 DTO 映射。</summary>
internal static class GoViewProjectMapper
{
    public static GoViewProjectResponse MapProject(GoViewProjectRecord record) =>
        new(
            record.Id,
            record.ProjectKey,
            record.Name,
            record.CanvasJson,
            record.LatestPublishedVersionNumber,
            record.IsEnabled,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    public static GoViewProjectVersionResponse MapVersion(GoViewProjectVersionRecord record) =>
        new(
            record.Id,
            record.ProjectId,
            record.VersionNumber,
            record.CanvasJson,
            record.ChangeNote,
            record.PublishedByUserId,
            record.PublishedAtUtc);
}
