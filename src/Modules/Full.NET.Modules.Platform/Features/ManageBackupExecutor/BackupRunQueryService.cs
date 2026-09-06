using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Platform.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Platform.Features.ManageBackupExecutor;

/// <summary>授权备份运行结果分页查询。</summary>
internal sealed class BackupRunQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    BackupExecutorStatusService statusService)
{
    /// <summary>分页查询备份运行结果。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="taskId">可选任务过滤。</param>
    /// <param name="status">可选状态过滤。</param>
    /// <param name="fromUtc">可选起始时间（UTC）。</param>
    /// <param name="toUtc">可选结束时间（UTC）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<BackupRunResponse>>> ListAsync(
        int page,
        int pageSize,
        Guid? taskId,
        string? status,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var parameters = BackupExecutorSqlParameters.Create(
            ("TaskId", taskId),
            ("Status", string.IsNullOrWhiteSpace(status) ? null : status.Trim()),
            ("FromUtc", fromUtc),
            ("ToUtc", toUtc),
            ("Offset", offset),
            ("PageSize", pageSize));
        var countStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => BackupExecutorSql.CountRunsSqlServer,
            DatabaseProvider.MySql => BackupExecutorSql.CountRunsMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };
        var listStatement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => BackupExecutorSql.ListRunsSqlServer,
            DatabaseProvider.MySql => BackupExecutorSql.ListRunsMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<BackupRunRecord>(
                listStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var artifactRootExists = Directory.Exists(statusService.ResolveArtifactRootPath());
        return Result<PagedResult<BackupRunResponse>>.Success(
            new PagedResult<BackupRunResponse>(
                rows.Select(record => Map(record, artifactRootExists)).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>按标识查询单条备份运行结果。</summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行详情或稳定未找到错误。</returns>
    public async Task<Result<BackupRunResponse>> GetByIdAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<BackupRunRecord>(
                BackupExecutorSql.FindRunById,
                BackupExecutorSqlParameters.Create(("Id", runId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<BackupRunResponse>.Failure(new Error(
                PlatformErrorCodes.BackupRunNotFound,
                "The backup run was not found.",
                ErrorType.NotFound));
        }

        var artifactRootExists = Directory.Exists(statusService.ResolveArtifactRootPath());
        return Result<BackupRunResponse>.Success(Map(record, artifactRootExists));
    }

    private static BackupRunResponse Map(BackupRunRecord record, bool artifactRootExists) =>
        new(
            record.Id,
            record.TaskId,
            record.TaskKey,
            record.TaskDisplayName,
            record.Status,
            record.StartedAtUtc,
            record.CompletedAtUtc,
            record.ArtifactFileName,
            record.ArtifactSizeBytes,
            record.ArtifactContentType,
            record.SummaryMessage,
            record.CreatedAtUtc,
            CanDownload(record, artifactRootExists));

    private static bool CanDownload(BackupRunRecord record, bool artifactRootExists) =>
        artifactRootExists
        && string.Equals(record.Status, BackupRunStatuses.Succeeded, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(record.ArtifactFileName)
        && BackupRunArtifactService.IsSafeArtifactFileName(record.ArtifactFileName);
}
