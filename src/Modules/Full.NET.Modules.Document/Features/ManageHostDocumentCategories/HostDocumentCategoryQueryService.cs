using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentCategories;

/// <summary>
/// Host 文档分类只读查询服务。按层级返回活动分类，前端在内存内构建树形结构；
/// 本服务不递归查询子分类以避免 SQL 递归的跨库差异，也不暴露已删除分类。
/// </summary>
internal sealed class HostDocumentCategoryQueryService(IQueryExecutor queryExecutor)
{
    public async Task<Result<IReadOnlyList<HostDocumentCategoryResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor
            .QueryAsync<DocumentCategoryRecord>(DocumentCategorySql.ListActive, null, cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<HostDocumentCategoryResponse>>.Success(
            rows.Select(Map).ToArray());
    }

    public async Task<Result<HostDocumentCategoryResponse>> GetByIdAsync(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentCategoryRecord>(
                DocumentCategorySql.FindActiveById,
                DocumentSqlParameters.Create(("Id", categoryId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : Result<HostDocumentCategoryResponse>.Success(Map(record));
    }

    internal static HostDocumentCategoryResponse Map(DocumentCategoryRecord record) =>
        new(
            record.Id,
            record.ParentId,
            record.Name,
            record.SortOrder,
            record.Code,
            record.Icon,
            record.Color,
            record.Description,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<HostDocumentCategoryResponse> NotFound() =>
        Result<HostDocumentCategoryResponse>.Failure(
            new Error(
                DocumentErrorCodes.CategoryNotFound,
                "Document category was not found.",
                ErrorType.NotFound));
}
