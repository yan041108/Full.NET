using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageGroups;

/// <summary>报表分组创建、更新与删除。</summary>
internal sealed class ReportingGroupManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ReportingGroupQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<ReportingGroupResponse>> CreateAsync(
        CreateReportingGroupRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => CreateCoreAsync(request, token), cancellationToken);

    public Task<Result<ReportingGroupResponse>> UpdateAsync(
        Guid groupId,
        UpdateReportingGroupRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => UpdateCoreAsync(groupId, request, token), cancellationToken);

    public Task<Result<bool>> DeleteAsync(
        Guid groupId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => DeleteCoreAsync(groupId, token), cancellationToken);

    private async Task<Result<ReportingGroupResponse>> CreateCoreAsync(
        CreateReportingGroupRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 128)
        {
            return Invalid<ReportingGroupResponse>();
        }

        if (request.ParentId.HasValue)
        {
            var parent = await queryExecutor.QuerySingleOrDefaultAsync<ReportingGroupRecord>(
                    ReportingGroupSql.FindById,
                    ReportingSqlParameters.Create(("GroupId", request.ParentId.Value)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (parent is null)
            {
                return Invalid<ReportingGroupResponse>();
            }
        }

        var groupId = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                ReportingGroupSql.Insert,
                ReportingSqlParameters.Create(
                    ("Id", groupId),
                    ("ParentId", request.ParentId),
                    ("Name", name),
                    ("SortOrder", request.SortOrder),
                    ("IsEnabled", request.IsEnabled),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);
        return await queries.GetByIdAsync(groupId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ReportingGroupResponse>> UpdateCoreAsync(
        Guid groupId,
        UpdateReportingGroupRequest request,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<ReportingGroupRecord>(
                ReportingGroupSql.FindById,
                ReportingSqlParameters.Create(("GroupId", groupId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFound<ReportingGroupResponse>();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 128)
        {
            return Invalid<ReportingGroupResponse>();
        }

        if (request.ParentId == groupId)
        {
            return Invalid<ReportingGroupResponse>();
        }

        if (request.ParentId.HasValue)
        {
            var parent = await queryExecutor.QuerySingleOrDefaultAsync<ReportingGroupRecord>(
                    ReportingGroupSql.FindById,
                    ReportingSqlParameters.Create(("GroupId", request.ParentId.Value)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (parent is null)
            {
                return Invalid<ReportingGroupResponse>();
            }
        }

        var affected = await commandExecutor.ExecuteAsync(
                ReportingGroupSql.Update,
                ReportingSqlParameters.Create(
                    ("GroupId", groupId),
                    ("ParentId", request.ParentId),
                    ("Name", name),
                    ("SortOrder", request.SortOrder),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Conflict<ReportingGroupResponse>();
        }

        return await queries.GetByIdAsync(groupId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<ReportingGroupRecord>(
                ReportingGroupSql.FindById,
                ReportingSqlParameters.Create(("GroupId", groupId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFound<bool>();
        }

        var childCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                ReportingGroupSql.CountChildren,
                ReportingSqlParameters.Create(("GroupId", groupId)),
                cancellationToken)
            .ConfigureAwait(false);
        var definitionCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                ReportingGroupSql.CountDefinitions,
                ReportingSqlParameters.Create(("GroupId", groupId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (childCount > 0 || definitionCount > 0)
        {
            return Result<bool>.Failure(new Error(
                ReportingErrorCodes.GroupInUse,
                "The reporting group still has child groups or definitions.",
                ErrorType.Conflict));
        }

        await commandExecutor.ExecuteAsync(
                ReportingGroupSql.Delete,
                ReportingSqlParameters.Create(("GroupId", groupId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    private static Result<T> Invalid<T>() =>
        Result<T>.Failure(new Error(
            ReportingErrorCodes.GroupInvalid,
            "The reporting group metadata is invalid.",
            ErrorType.Validation));

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            ReportingErrorCodes.GroupNotFound,
            "The reporting group was not found.",
            ErrorType.NotFound));

    private static Result<T> Conflict<T>() =>
        Result<T>.Failure(new Error(
            ReportingErrorCodes.GroupConcurrencyConflict,
            "The reporting group was modified by another request.",
            ErrorType.Conflict));
}
