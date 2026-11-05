using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Persistence;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>排队或执行中的来源继续保留；到期不改变业务状态和已经提交的行。</summary>
internal static class ImportSourceRetentionRules
{
    internal static bool CanUse(ImportExportTaskRecord task, DateTimeOffset now) =>
        task.SourceReleasedAtUtc is null && task.SourceReleaseStartedAtUtc is null &&
        (IsInUse(task.StatusKey) || task.SourceExpiresAtUtc > now);

    internal static bool IsInUse(string status) => status is ImportExportTaskStatusKeys.Queued or ImportExportTaskStatusKeys.Executing;

    internal static ImportExportSourceRetentionResponse Describe(ImportExportTaskRecord task, DateTimeOffset now) =>
        Describe(task.StatusKey, task.SourceExpiresAtUtc, task.SourceReleaseStartedAtUtc, task.SourceReleasedAtUtc, now);

    internal static ImportExportSourceRetentionResponse Describe(ImportExportTaskListRecord task, DateTimeOffset now) =>
        Describe(task.StatusKey, task.SourceExpiresAtUtc, task.SourceReleaseStartedAtUtc, task.SourceReleasedAtUtc, now);

    private static ImportExportSourceRetentionResponse Describe(string status, DateTimeOffset expires,
        DateTimeOffset? started, DateTimeOffset? released, DateTimeOffset now) => new(expires,
            released.HasValue ? "released" : started.HasValue ? "releasing" : expires > now ? "available"
                : IsInUse(status) ? "retained_for_execution" : "expired", released);
}
