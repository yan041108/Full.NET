using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Jobs.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Jobs.Features.ManageHostJobDefinitions;

/// <summary>Host 任务定义分页查询。</summary>
internal sealed class HostJobDefinitionQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostJobDefinitionResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                JobSql.CountDefinitions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<JobDefinitionRecord>(
                ResolveListStatement(),
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<HostJobDefinitionResponse>>.Success(
            new PagedResult<HostJobDefinitionResponse>(
                rows.Select(MapDefinition).ToArray(),
                page,
                pageSize,
                total));
    }

    public async Task<Result<HostJobDefinitionResponse>> GetByIdAsync(
        Guid definitionId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<JobDefinitionRecord>(
                JobSql.FindDefinitionById,
                new { Id = definitionId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? DefinitionNotFound<HostJobDefinitionResponse>()
            : Result<HostJobDefinitionResponse>.Success(MapDefinition(record));
    }

    internal static HostJobDefinitionResponse MapDefinition(JobDefinitionRecord record) =>
        new(
            record.Id,
            record.JobKey,
            record.DisplayName,
            record.Description,
            record.GroupName,
            record.IsEnabled,
            record.AllowConcurrentExecutions,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    /// <summary>
    /// 查询已启用作业定义的去重分组名列表，对应 Admin.NET ListJobGroup，
    /// 供前端分组下拉与按组筛选使用。
    /// </summary>
    public async Task<Result<IReadOnlyList<HostJobGroupResponse>>> ListGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        var groups = await queryExecutor
            .QueryAsync<string>(JobSql.ListJobGroups, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<HostJobGroupResponse>>.Success(
            groups
                .Select(group => new HostJobGroupResponse(group))
                .ToArray());
    }

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => JobSql.ListDefinitionsSqlServer,
            DatabaseProvider.MySql => JobSql.ListDefinitionsMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'.")
        };

    internal static Result<T> DefinitionNotFound<T>() =>
        Result<T>.Failure(new Error(
            JobsErrorCodes.DefinitionNotFound,
            "The job definition was not found.",
            ErrorType.NotFound));
}
