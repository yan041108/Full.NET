using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentTags;

/// <summary>
/// Host 文档标签只读查询服务。按名称排序返回活动标签，UseCount 直接读自目录表的反规范化列，
/// 不在本服务内联统计，以保证列表性能；不暴露已删除标签。
/// </summary>
internal sealed class HostDocumentTagQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<IReadOnlyList<HostDocumentTagResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor
            .QueryAsync<DocumentTagRecord>(DocumentTagSql.ListActive, null, cancellationToken)
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
                new { Id = tagId },
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : Result<HostDocumentTagResponse>.Success(Map(record));
    }

    // 修复：Map 方法补齐 10 个构造参数，严格按任务指定的顺序：Id/Name/Code/Icon/Color/Description/UseCount/CreatedAtUtc/UpdatedAtUtc/Version
    // 注：Contracts 字段顺序由另一个子代理维护，此处顺序需与其最终定义对齐
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
            record.Version);

    private static Result<HostDocumentTagResponse> NotFound() =>
        Result<HostDocumentTagResponse>.Failure(
            new Error(
                DocumentErrorCodes.TagNotFound,
                "Document tag was not found.",
                ErrorType.NotFound));
}
