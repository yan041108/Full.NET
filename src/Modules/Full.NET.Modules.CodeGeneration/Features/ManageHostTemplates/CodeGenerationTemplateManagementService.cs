using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;

/// <summary>
/// 使用可信主体审计、规范 Schema 与乐观并发管理 Host 模板。
/// </summary>
internal sealed class CodeGenerationTemplateManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    CodeGenerationSchemaNormalizer schemaNormalizer,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>
    /// 规范化输入后在命令事务内创建模板，写入 SchemaJson、SchemaSha256 与初始版本 1；输入不通过规范化时以 Invalid 错误码返回且不开启事务。
    /// </summary>
    public Task<Result<CodeGenerationTemplateResponse>> CreateAsync(
        Guid actorUserId,
        CreateCodeGenerationTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var input = NormalizeInput(
            request.Name,
            request.Description,
            request.Schema);
        if (!input.IsSuccess)
        {
            return Task.FromResult(
                Result<CodeGenerationTemplateResponse>.Failure(input.Error!));
        }

        return transaction.ExecuteAsync(
            token => CreateCoreAsync(actorUserId, input.Value!, token),
            cancellationToken);
    }

    /// <summary>
    /// 规范化输入后以乐观并发更新模板；affected != 1 时返回 VersionConflict，成功后版本号自增并保留原始创建审计。
    /// </summary>
    public Task<Result<CodeGenerationTemplateResponse>> UpdateAsync(
        Guid templateId,
        Guid actorUserId,
        UpdateCodeGenerationTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var input = NormalizeInput(
            request.Name,
            request.Description,
            request.Schema);
        if (!input.IsSuccess || request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteAsync(
            token => UpdateCoreAsync(
                templateId,
                actorUserId,
                request.Version,
                input.Value!,
                token),
            cancellationToken);
    }

    /// <summary>
    /// 软删除模板，先校验存在再以乐观并发标记 DeletedAt/DeletedBy；并发冲突或版本不符返回 VersionConflict。
    /// </summary>
    public Task<Result<bool>> DeleteAsync(
        Guid templateId,
        Guid actorUserId,
        DeleteCodeGenerationTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Version < 1)
        {
            return Task.FromResult(
                Result<bool>.Failure(InvalidError()));
        }

        return transaction.ExecuteAsync(
            token => DeleteCoreAsync(
                templateId,
                actorUserId,
                request.Version,
                token),
            cancellationToken);
    }

    private async Task<Result<CodeGenerationTemplateResponse>> CreateCoreAsync(
        Guid actorUserId,
        NormalizedTemplateInput input,
        CancellationToken cancellationToken)
    {
        var id = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                CodeGenerationTemplateSql.Insert,
                new
                {
                    Id = id,
                    input.Name,
                    input.Description,
                    SchemaJson = input.Schema.CanonicalJson,
                    SchemaSha256 = input.Schema.SchemaSha256,
                    CreatedAtUtc = now,
                    CreatedByUserId = actorUserId,
                    Version = 1L,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Result<CodeGenerationTemplateResponse>.Success(
            new CodeGenerationTemplateResponse(
                id,
                input.Name,
                input.Description,
                input.Schema.CanonicalRequest,
                input.Schema.SchemaSha256,
                now,
                actorUserId,
                null,
                null,
                1));
    }

    private async Task<Result<CodeGenerationTemplateResponse>> UpdateCoreAsync(
        Guid templateId,
        Guid actorUserId,
        long version,
        NormalizedTemplateInput input,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(templateId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                CodeGenerationTemplateSql.Update,
                new
                {
                    Id = templateId,
                    input.Name,
                    input.Description,
                    SchemaJson = input.Schema.CanonicalJson,
                    SchemaSha256 = input.Schema.SchemaSha256,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorUserId,
                    Version = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return VersionConflict();
        }

        return Result<CodeGenerationTemplateResponse>.Success(
            new CodeGenerationTemplateResponse(
                templateId,
                input.Name,
                input.Description,
                input.Schema.CanonicalRequest,
                input.Schema.SchemaSha256,
                existing.CreatedAtUtc,
                existing.CreatedByUserId,
                now,
                actorUserId,
                version + 1));
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid templateId,
        Guid actorUserId,
        long version,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(templateId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            return Result<bool>.Failure(NotFoundError());
        }

        var affected = await commandExecutor.ExecuteAsync(
                CodeGenerationTemplateSql.SoftDelete,
                new
                {
                    Id = templateId,
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

    private Task<CodeGenerationTemplateRecord?> FindAsync(
        Guid templateId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<CodeGenerationTemplateRecord>(
            CodeGenerationTemplateSql.FindById,
            new { Id = templateId },
            cancellationToken);

    private Result<NormalizedTemplateInput> NormalizeInput(
        string? name,
        string? description,
        CodeGenerationPreviewRequest? schema)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
        if (normalizedName.Length is < 1 or > 128
            || normalizedDescription is { Length: > 512 }
            || schema is null)
        {
            return Result<NormalizedTemplateInput>.Failure(InvalidError());
        }

        var normalizedSchema = schemaNormalizer.Normalize(schema);
        return normalizedSchema.IsSuccess
            ? Result<NormalizedTemplateInput>.Success(
                new NormalizedTemplateInput(
                    normalizedName,
                    normalizedDescription,
                    normalizedSchema.Value!))
            : Result<NormalizedTemplateInput>.Failure(InvalidError());
    }

    private static Result<CodeGenerationTemplateResponse> Invalid() =>
        Result<CodeGenerationTemplateResponse>.Failure(InvalidError());

    private static Result<CodeGenerationTemplateResponse> NotFound() =>
        Result<CodeGenerationTemplateResponse>.Failure(NotFoundError());

    private static Result<CodeGenerationTemplateResponse> VersionConflict() =>
        Result<CodeGenerationTemplateResponse>.Failure(
            VersionConflictError());

    private static Error InvalidError() => new(
        CodeGenerationTemplateErrorCodes.Invalid,
        "The code generation template is invalid.",
        ErrorType.Validation);

    private static Error NotFoundError() => new(
        CodeGenerationTemplateErrorCodes.NotFound,
        "The code generation template was not found.",
        ErrorType.NotFound);

    private static Error VersionConflictError() => new(
        CodeGenerationTemplateErrorCodes.VersionConflict,
        "The code generation template was updated concurrently.",
        ErrorType.Conflict);

    private sealed record NormalizedTemplateInput(
        string Name,
        string? Description,
        NormalizedCodeGenerationSchema Schema);
}
