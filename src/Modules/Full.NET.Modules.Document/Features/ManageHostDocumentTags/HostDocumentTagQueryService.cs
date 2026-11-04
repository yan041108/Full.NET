using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentTags;

/// <summary>
/// Host 文档标签只读查询服务。按名称排序返回活动标签；UseCount由同一SQL统计保留的Host引用，
/// 包括回收站关联，忽略历史计数列；不暴露已删除标签。
/// </summary>
internal sealed class HostDocumentTagQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<IReadOnlyList<HostDocumentTagResponse>>> ListAsync(
        bool? isHot,
        bool? isRecommended,
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor
            .QueryAsync<DocumentTagRecord>(
                DocumentTagSql.ListActive,
                DocumentSqlParameters.Create(
                    ("IsHot", isHot),
                    ("IsRecommended", isRecommended)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<HostDocumentTagResponse>>.Success(
            rows.Select(Map).ToArray());
    }

    public async Task<Result<HostDocumentTagResponse>> GetByIdAsync(
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentTagRecord>(
                DocumentTagSql.FindActiveById,
                DocumentSqlParameters.Create(("Id", tagId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : Result<HostDocumentTagResponse>.Success(Map(record));
    }

    /// <summary>事务写入后的回读不读取文档行；计数由门禁内写入前的权威读取提供。</summary>
    public async Task<Result<HostDocumentTagResponse>> ReloadAfterWriteAsync(
        Guid tagId,
        int useCount,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<DocumentTagRecord>(
                DocumentTagSql.FindActiveByIdForMutationReload,
                DocumentSqlParameters.Create(("Id", tagId), ("UseCount", useCount)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : Result<HostDocumentTagResponse>.Success(Map(record));
    }

    // 读取与管理回读共用映射，确保派生计数与既有契约字段顺序一致。
    internal static HostDocumentTagResponse Map(DocumentTagRecord record) =>
        new(
            record.Id,
            record.Name,
            record.Code,
            record.Icon,
            record.Color,
            record.Description,
            record.UseCount,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version,
            record.IsHot,
            record.IsRecommended);

    private static Result<HostDocumentTagResponse> NotFound() =>
        Result<HostDocumentTagResponse>.Failure(
            new Error(
                DocumentErrorCodes.TagNotFound,
                "Document tag was not found.",
                ErrorType.NotFound));
}
