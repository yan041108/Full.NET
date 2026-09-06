using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageGroups;

/// <summary>报表分组只读查询。</summary>
internal sealed class ReportingGroupQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<ReportingGroupResponse>> GetByIdAsync(
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ReportingGroupRecord>(
                ReportingGroupSql.FindById,
                ReportingSqlParameters.Create(("GroupId", groupId)),
                cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFound()
            : Result<ReportingGroupResponse>.Success(Map(row));
    }

    public async Task<Result<IReadOnlyList<ReportingGroupResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<ReportingGroupRecord>(
                ReportingGroupSql.List,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<ReportingGroupResponse>>.Success(rows.Select(Map).ToArray());
    }

    internal static ReportingGroupResponse Map(ReportingGroupRecord row) =>
        new(
            row.Id,
            row.ParentId,
            row.Name,
            row.SortOrder,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static Result<ReportingGroupResponse> NotFound() =>
        Result<ReportingGroupResponse>.Failure(new Error(
            ReportingErrorCodes.GroupNotFound,
            "The reporting group was not found.",
            ErrorType.NotFound));
}
