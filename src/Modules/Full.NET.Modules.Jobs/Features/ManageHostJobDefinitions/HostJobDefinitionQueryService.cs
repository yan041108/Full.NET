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
        CancellationToken cancellationToken = default, HostJobDefinitionListFilter? filter = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var parameters = new Dictionary<string, object?>
        {
            ["Offset"] = offset, ["PageSize"] = pageSize,
            ["JobKey"] = SqlSearchPattern.ContainsLiteral(filter?.JobKey),
            ["DisplayName"] = SqlSearchPattern.ContainsLiteral(filter?.DisplayName),
            ["GroupName"] = filter?.GroupName?.Trim() is { Length: > 0 } groupName ? groupName : null,
            ["Status"] = string.IsNullOrWhiteSpace(filter?.Status) ? null : filter.Status.Trim(),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                JobSql.CountDefinitionsFiltered,
                parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<JobDefinitionRecord>(
                databaseOptions.Value.Provider == DatabaseProvider.MySql ? JobSql.ListDefinitionsMySqlFiltered : JobSql.ListDefinitionsSqlServerFiltered,
                parameters,
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
                JobsSqlParameters.Create(("Id", definitionId)),
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
            record.HandlerKind,
            HostJobDefinitionArgsMapper.RedactForResponse(
                record.HandlerKind,
                record.ArgsJson),
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

/// <summary>分页列表的显式筛选条件。</summary>
internal sealed record HostJobDefinitionListFilter(string? JobKey = null, string? DisplayName = null, string? GroupName = null, string? Status = null);
