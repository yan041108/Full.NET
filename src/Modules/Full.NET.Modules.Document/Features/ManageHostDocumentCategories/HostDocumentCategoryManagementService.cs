using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentCategories;

/// <summary>
/// Host 文档分类目录管理服务。分类支持父子层级（ParentId 自引用）与软删除，
/// 通过乐观并发 Version 字段防止跨客户端覆盖；删除前必须校验无活动子分类与无文档引用，
/// 存在引用时返回 InUse 错误而非级联删除，以保证层级与引用完整性。
/// 同步写入 Code/Icon/Color/Description 四列，与 Tag 表统一字段顺序以便 UI 复用。
/// </summary>
internal sealed class HostDocumentCategoryManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentCategoryQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<HostDocumentCategoryResponse>> CreateAsync(
        CreateHostDocumentCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(request.Name, out var name))
        {
            return Task.FromResult(Invalid());
        }

        // 修复：传递新字段 Code/Icon/Color/Description 到 CreateCoreAsync，确保写入SQL时不丢失数据
        return transaction.ExecuteAsync(
            token => CreateCoreAsync(request.ParentId, name, request.SortOrder, request.Code, request.Icon, request.Color, request.Description, token),
            cancellationToken);
    }

    public Task<Result<HostDocumentCategoryResponse>> UpdateAsync(
        Guid categoryId,
        UpdateHostDocumentCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(request.Name, out var name) || request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        // 修复：传递新字段 Code/Icon/Color/Description 到 UpdateCoreAsync，确保更新SQL时同步写入
        return transaction.ExecuteAsync(
            token => UpdateCoreAsync(categoryId, request.ParentId, name, request.SortOrder, request.Code, request.Icon, request.Color, request.Description, request.Version, token),
            cancellationToken);
    }

    public Task<Result<bool>> DeleteAsync(
        Guid categoryId,
        Guid actorUserId,
        DeleteHostDocumentCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(Result<bool>.Failure(InvalidError()));
        }

        return transaction.ExecuteAsync(
            token => DeleteCoreAsync(categoryId, actorUserId, request.Version, token),
            cancellationToken);
    }

    // 修复：CreateCoreAsync 方法签名新增 code/icon/color/description 参数，与 Contracts 和 SQL 列对齐
    private async Task<Result<HostDocumentCategoryResponse>> CreateCoreAsync(
        Guid? parentId,
        string name,
        int sortOrder,
        string? code,
        string? icon,
        string? color,
        string? description,
        CancellationToken cancellationToken)
    {
        if (parentId is not null)
        {
            var parent = await queryExecutor
                .QuerySingleOrDefaultAsync<DocumentCategoryRecord>(
                    DocumentCategorySql.FindActiveById,
                    new { Id = parentId.Value },
                    cancellationToken)
                .ConfigureAwait(false);
            if (parent is null)
            {
                return InvalidParent();
            }
        }

        if (await FindNameConflictAsync(parentId, name, null, cancellationToken).ConfigureAwait(false))
        {
            return NameExists();
        }

        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        // 修复：Insert SQL 匿名对象补齐 Code/Icon/Color/Description 四个新字段，确保写入数据库完整
        await commandExecutor.ExecuteAsync(
                DocumentCategorySql.Insert,
                new
                {
                    Id = id,
                    ParentId = parentId,
                    Name = name,
                    SortOrder = sortOrder,
                    Code = code,
                    Icon = icon,
                    Color = color,
                    Description = description,
                    CreatedAtUtc = now,
                    Version = 1,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    // 修复：UpdateCoreAsync 方法签名新增 code/icon/color/description 参数，与 Contracts 和 SQL 列对齐
    private async Task<Result<HostDocumentCategoryResponse>> UpdateCoreAsync(
        Guid categoryId,
        Guid? parentId,
        string name,
        int sortOrder,
        string? code,
        string? icon,
        string? color,
        string? description,
        long version,
        CancellationToken cancellationToken)
    {
        if (await queries.GetByIdAsync(categoryId, cancellationToken).ConfigureAwait(false) is { IsSuccess: false })
        {
            return NotFound();
        }

        if (parentId == categoryId)
        {
            return InvalidParent();
        }

        if (parentId is not null)
        {
            var parent = await queryExecutor
                .QuerySingleOrDefaultAsync<DocumentCategoryRecord>(
                    DocumentCategorySql.FindActiveById,
                    new { Id = parentId.Value },
                    cancellationToken)
                .ConfigureAwait(false);
            if (parent is null)
            {
                return InvalidParent();
            }
        }

        if (await FindNameConflictAsync(parentId, name, categoryId, cancellationToken).ConfigureAwait(false))
        {
            return NameExists();
        }

        var now = clock.UtcNow;
        // 修复：Update SQL 匿名对象补齐 Code/Icon/Color/Description 四个新字段，确保更新操作完整写入
        var affected = await commandExecutor.ExecuteAsync(
                DocumentCategorySql.Update,
                new
                {
                    Id = categoryId,
                    ParentId = parentId,
                    Name = name,
                    SortOrder = sortOrder,
                    Code = code,
                    Icon = icon,
                    Color = color,
                    Description = description,
                    UpdatedAtUtc = now,
                    Version = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(categoryId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid categoryId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        if (await queries.GetByIdAsync(categoryId, cancellationToken).ConfigureAwait(false) is { IsSuccess: false })
        {
            return Result<bool>.Failure(NotFoundError());
        }

        var childCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                DocumentCategorySql.CountActiveChildren,
                new { ParentId = categoryId },
                cancellationToken)
            .ConfigureAwait(false);
        if (childCount > 0)
        {
            return Result<bool>.Failure(HasChildrenError());
        }

        var itemCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                DocumentCategorySql.CountActiveItems,
                new { CategoryId = categoryId },
                cancellationToken)
            .ConfigureAwait(false);
        if (itemCount > 0)
        {
            return Result<bool>.Failure(InUseError());
        }

        var affected = await commandExecutor.ExecuteAsync(
                DocumentCategorySql.SoftDelete,
                new
                {
                    Id = categoryId,
                    DeletedAtUtc = clock.UtcNow,
                    DeletedByUserId = actorUserId,
                    Version = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(VersionConflictError());
    }

    private async Task<bool> FindNameConflictAsync(
        Guid? parentId,
        string name,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentNameConflictRecord>(
                DocumentCategorySql.FindActiveByParentAndName,
                new { ParentId = parentId, Name = name },
                cancellationToken)
            .ConfigureAwait(false);
        return existing is not null && existing.Id != excludeId;
    }

    private static bool TryNormalizeName(string? value, out string name)
    {
        name = value?.Trim() ?? string.Empty;
        return name.Length is >= 1 and <= 128;
    }

    private static Result<HostDocumentCategoryResponse> Invalid() =>
        Result<HostDocumentCategoryResponse>.Failure(InvalidError());

    private static Result<HostDocumentCategoryResponse> NotFound() =>
        Result<HostDocumentCategoryResponse>.Failure(NotFoundError());

    private static Result<HostDocumentCategoryResponse> NameExists() =>
        Result<HostDocumentCategoryResponse>.Failure(
            new Error(DocumentErrorCodes.CategoryNameExists, "Category name already exists.", ErrorType.Conflict));

    private static Result<HostDocumentCategoryResponse> InvalidParent() =>
        Result<HostDocumentCategoryResponse>.Failure(
            new Error(DocumentErrorCodes.CategoryInvalidParent, "Parent category is invalid.", ErrorType.Validation));

    private static Result<HostDocumentCategoryResponse> VersionConflict() =>
        Result<HostDocumentCategoryResponse>.Failure(VersionConflictError());

    private static Error InvalidError() =>
        new(DocumentErrorCodes.CategoryInvalid, "The category request is invalid.", ErrorType.Validation);

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.CategoryNotFound, "Document category was not found.", ErrorType.NotFound);

    private static Error VersionConflictError() =>
        new(DocumentErrorCodes.CategoryVersionConflict, "Category was updated by another operation.", ErrorType.Conflict);

    private static Error HasChildrenError() =>
        new(DocumentErrorCodes.CategoryHasChildren, "Category still has child categories.", ErrorType.BusinessRule);

    private static Error InUseError() =>
        new(DocumentErrorCodes.CategoryInUse, "Category is referenced by documents.", ErrorType.BusinessRule);
}
