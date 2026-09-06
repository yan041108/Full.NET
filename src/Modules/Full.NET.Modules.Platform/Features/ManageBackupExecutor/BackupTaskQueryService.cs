using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Platform.Contracts;
using Full.NET.Modules.Platform.Persistence;

namespace Full.NET.Modules.Platform.Features.ManageBackupExecutor;

/// <summary>授权备份任务目录查询。</summary>
internal sealed class BackupTaskQueryService(IQueryExecutor queryExecutor)
{
    /// <summary>列出全部备份任务目录项。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>任务目录列表。</returns>
    public async Task<Result<IReadOnlyList<BackupTaskResponse>>> ListAsync(
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<BackupTaskRecord>(
                BackupExecutorSql.ListTasks,
                BackupExecutorSqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<BackupTaskResponse>>.Success(
            rows.Select(Map).ToArray());
    }

    /// <summary>按标识查询单条备份任务。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>任务详情或稳定未找到错误。</returns>
    public async Task<Result<BackupTaskResponse>> GetByIdAsync(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<BackupTaskRecord>(
                BackupExecutorSql.FindTaskById,
                BackupExecutorSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<BackupTaskResponse>.Failure(new Error(
                PlatformErrorCodes.BackupTaskNotFound,
                "The backup task was not found.",
                ErrorType.NotFound))
            : Result<BackupTaskResponse>.Success(Map(record));
    }

    private static BackupTaskResponse Map(BackupTaskRecord record) =>
        new(
            record.Id,
            record.TaskKey,
            record.DisplayName,
            record.Description,
            record.DatabaseProvider,
            record.IsEnabled,
            record.SortOrder,
            record.CreatedAtUtc,
            record.UpdatedAtUtc);
}
