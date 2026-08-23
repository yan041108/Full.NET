using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentTags;

/// <summary>
/// Host 文档标签目录管理服务。标签为扁平结构（无父子层级），通过乐观并发 Version 字段防止覆盖；
/// 删除前必须校验无标签引用（CountAssignments），存在引用时返回 InUse 错误。
/// 同步写入 Code/Icon/Color/Description 四列，UseCount 由后端按引用计数维护，禁止由前端直接写入。
/// </summary>
internal sealed class HostDocumentTagManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostDocumentTagQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<HostDocumentTagResponse>> CreateAsync(
        CreateHostDocumentTagRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(request.Name, out var name))
        {
            return Task.FromResult(Invalid());
        }

        // 修复：传递新字段 Code/Icon/Color/Description 到 CreateCoreAsync，确保写入SQL时不丢失数据
        return transaction.ExecuteAsync(
            token => CreateCoreAsync(name, request.Code, request.Icon, request.Color, request.Description, token),
            cancellationToken);
    }

    public Task<Result<HostDocumentTagResponse>> UpdateAsync(
        Guid tagId,
        UpdateHostDocumentTagRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeName(request.Name, out var name) || request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        // 修复：传递新字段 Code/Icon/Color/Description 到 UpdateCoreAsync，确保更新SQL时同步写入
        return transaction.ExecuteAsync(
            token => UpdateCoreAsync(tagId, name, request.Code, request.Icon, request.Color, request.Description, request.Version, token),
            cancellationToken);
    }

    public Task<Result<bool>> DeleteAsync(
        Guid tagId,
        Guid actorUserId,
        DeleteHostDocumentTagRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(Result<bool>.Failure(InvalidError()));
        }

        return transaction.ExecuteAsync(
            token => DeleteCoreAsync(tagId, actorUserId, request.Version, token),
            cancellationToken);
    }

    // 修复：CreateCoreAsync 方法签名新增 code/icon/color/description 参数，与 Contracts 和 SQL 列对齐
    private async Task<Result<HostDocumentTagResponse>> CreateCoreAsync(
        string name,
        string? code,
        string? icon,
        string? color,
        string? description,
        CancellationToken cancellationToken)
    {
        if (await FindNameConflictAsync(name, null, cancellationToken).ConfigureAwait(false))
        {
            return NameExists();
        }

        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        // 修复：Insert SQL 匿名对象补齐 Code/Icon/Color/Description/UseCount，UseCount 新标签默认 0
        await commandExecutor.ExecuteAsync(
                DocumentTagSql.Insert,
                new
                {
                    Id = id,
                    Name = name,
                    Code = code,
                    Icon = icon,
                    Color = color,
                    Description = description,
                    UseCount = 0,
                    CreatedAtUtc = now,
                    Version = 1,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    // 修复：UpdateCoreAsync 方法签名新增 code/icon/color/description 参数，与 Contracts 和 SQL 列对齐
    private async Task<Result<HostDocumentTagResponse>> UpdateCoreAsync(
        Guid tagId,
        string name,
        string? code,
        string? icon,
        string? color,
        string? description,
        long version,
        CancellationToken cancellationToken)
    {
        if (await queries.GetByIdAsync(tagId, cancellationToken).ConfigureAwait(false) is { IsSuccess: false })
        {
            return NotFound();
        }

        if (await FindNameConflictAsync(name, tagId, cancellationToken).ConfigureAwait(false))
        {
            return NameExists();
        }

        var now = clock.UtcNow;
        // 修复：Update SQL 匿名对象补齐 Code/Icon/Color/Description 四个新字段，确保更新操作完整写入
        var affected = await commandExecutor.ExecuteAsync(
                DocumentTagSql.Update,
                new
                {
                    Id = tagId,
                    Name = name,
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

        return await queries.GetByIdAsync(tagId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid tagId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        if (await queries.GetByIdAsync(tagId, cancellationToken).ConfigureAwait(false) is { IsSuccess: false })
        {
            return Result<bool>.Failure(NotFoundError());
        }

        var assignmentCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                DocumentTagSql.CountAssignments,
                new { TagId = tagId },
                cancellationToken)
            .ConfigureAwait(false);
        if (assignmentCount > 0)
        {
            return Result<bool>.Failure(InUseError());
        }

        var affected = await commandExecutor.ExecuteAsync(
                DocumentTagSql.SoftDelete,
                new
                {
                    Id = tagId,
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
        string name,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<DocumentNameConflictRecord>(
                DocumentTagSql.FindActiveByName,
                new { Name = name },
                cancellationToken)
            .ConfigureAwait(false);
        return existing is not null && existing.Id != excludeId;
    }

    private static bool TryNormalizeName(string? value, out string name)
    {
        name = value?.Trim() ?? string.Empty;
        return name.Length is >= 1 and <= 64;
    }

    private static Result<HostDocumentTagResponse> Invalid() =>
        Result<HostDocumentTagResponse>.Failure(InvalidError());

    private static Result<HostDocumentTagResponse> NotFound() =>
        Result<HostDocumentTagResponse>.Failure(NotFoundError());

    private static Result<HostDocumentTagResponse> NameExists() =>
        Result<HostDocumentTagResponse>.Failure(
            new Error(DocumentErrorCodes.TagNameExists, "Tag name already exists.", ErrorType.Conflict));

    private static Result<HostDocumentTagResponse> VersionConflict() =>
        Result<HostDocumentTagResponse>.Failure(VersionConflictError());

    private static Error InvalidError() =>
        new(DocumentErrorCodes.TagInvalid, "The tag request is invalid.", ErrorType.Validation);

    private static Error NotFoundError() =>
        new(DocumentErrorCodes.TagNotFound, "Document tag was not found.", ErrorType.NotFound);

    private static Error VersionConflictError() =>
        new(DocumentErrorCodes.TagVersionConflict, "Tag was updated by another operation.", ErrorType.Conflict);

    private static Error InUseError() =>
        new(DocumentErrorCodes.TagInUse, "Tag is assigned to documents.", ErrorType.BusinessRule);
}
