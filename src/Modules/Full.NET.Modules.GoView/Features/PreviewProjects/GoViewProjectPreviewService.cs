using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.GoView.Contracts;
using Full.NET.Modules.GoView.Features.ManageProjects;

namespace Full.NET.Modules.GoView.Features.PreviewProjects;

/// <summary>返回已发布大屏项目快照，不提供数据源查询能力。</summary>
internal sealed class GoViewProjectPreviewService(
    GoViewProjectQueryService projectQueries,
    IClock clock)
{
    /// <summary>读取指定发布版本的只读画布快照。</summary>
    public async Task<Result<GoViewProjectPreviewResponse>> PreviewAsync(
        Guid projectId,
        PreviewGoViewProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var projectResult = await projectQueries.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (!projectResult.IsSuccess || projectResult.Value is null)
        {
            return Result<GoViewProjectPreviewResponse>.Failure(projectResult.Error!);
        }

        var project = projectResult.Value;
        if (!project.IsEnabled)
        {
            return InvalidProject("The GoView project is disabled.");
        }

        var versionNumber = request.VersionNumber ?? project.LatestPublishedVersionNumber;
        if (versionNumber <= 0)
        {
            return Result<GoViewProjectPreviewResponse>.Failure(new Error(
                GoViewErrorCodes.ProjectNotPublished,
                "The GoView project has no published version to preview.",
                ErrorType.Validation));
        }

        var versionResult = await projectQueries
            .GetVersionAsync(projectId, versionNumber, cancellationToken)
            .ConfigureAwait(false);
        if (!versionResult.IsSuccess || versionResult.Value is null)
        {
            return Result<GoViewProjectPreviewResponse>.Failure(versionResult.Error!);
        }

        return Result<GoViewProjectPreviewResponse>.Success(new GoViewProjectPreviewResponse(
            project.Id,
            project.ProjectKey,
            project.Name,
            versionNumber,
            versionResult.Value.CanvasJson,
            clock.UtcNow));
    }

    private static Result<GoViewProjectPreviewResponse> InvalidProject(string message) =>
        Result<GoViewProjectPreviewResponse>.Failure(new Error(
            GoViewErrorCodes.ProjectInvalid,
            message,
            ErrorType.Validation));
}
