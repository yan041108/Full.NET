using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Features.PreviewCrudGeneration;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 编排受跟踪的 Host 代码生成预览，并在返回前写入不可变运行摘要。
/// </summary>
internal sealed class CodeGenerationRunService(
    ICommandExecutor commandExecutor,
    CodeGenerationTemplateQueryService templateQueries,
    CodeGenerationPreviewService previewService,
    CodeGenerationSchemaNormalizer schemaNormalizer,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<Result<CodeGenerationRunPreviewResponse>> PreviewAsync(
        Guid actorUserId,
        CodeGenerationRunPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startedAtUtc = clock.UtcNow;
        var source = await ResolveSourceAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!source.IsSuccess)
        {
            await PersistFailureAsync(
                    actorUserId,
                    request.TemplateId,
                    request.TemplateVersion,
                    source.Error!,
                    startedAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<CodeGenerationRunPreviewResponse>.Failure(
                source.Error!);
        }

        var normalized = schemaNormalizer.Normalize(source.Value!);
        if (!normalized.IsSuccess)
        {
            await PersistFailureAsync(
                    actorUserId,
                    request.TemplateId,
                    request.TemplateVersion,
                    normalized.Error!,
                    startedAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<CodeGenerationRunPreviewResponse>.Failure(
                normalized.Error!);
        }

        Result<CodeGenerationPreviewResponse> preview;
        try
        {
            preview = previewService.Preview(
                normalized.Value!.CanonicalRequest,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            var error = new Error(
                CodeGenerationRunErrorCodes.GenerationFailed,
                "The code generation preview failed.",
                ErrorType.Unexpected);
            await PersistFailureAsync(
                    actorUserId,
                    request.TemplateId,
                    request.TemplateVersion,
                    error,
                    startedAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<CodeGenerationRunPreviewResponse>.Failure(error);
        }

        if (!preview.IsSuccess)
        {
            await PersistFailureAsync(
                    actorUserId,
                    request.TemplateId,
                    request.TemplateVersion,
                    preview.Error!,
                    startedAtUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<CodeGenerationRunPreviewResponse>.Failure(
                preview.Error!);
        }

        var runId = idGenerator.NewId();
        var artifacts = preview.Value!.Artifacts;
        var affectedRows = await commandExecutor.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                new
                {
                    Id = runId,
                    request.TemplateId,
                    request.TemplateVersion,
                    OperationKind = CodeGenerationRunOperationKinds.Preview,
                    Status = CodeGenerationRunStatuses.Succeeded,
                    normalized.Value.Schema.ModuleKey,
                    normalized.Value.Schema.EntityKey,
                    normalized.Value.SchemaSha256,
                    ArtifactCount = artifacts.Count,
                    ManifestSha256 = CodeGenerationRunSummary
                        .ComputeManifestSha256(artifacts),
                    ErrorCode = (string?)null,
                    RequestedByUserId = actorUserId,
                    StartedAtUtc = startedAtUtc,
                    FinishedAtUtc = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureInserted(affectedRows);

        return Result<CodeGenerationRunPreviewResponse>.Success(
            new CodeGenerationRunPreviewResponse(runId, preview.Value));
    }

    private async Task<Result<CodeGenerationPreviewRequest>>
        ResolveSourceAsync(
            CodeGenerationRunPreviewRequest request,
            CancellationToken cancellationToken)
    {
        var hasSchema = request.Schema is not null;
        var hasTemplateId = request.TemplateId.HasValue;
        var hasTemplateVersion = request.TemplateVersion is > 0;
        if (hasSchema
            ? hasTemplateId || request.TemplateVersion.HasValue
            : !hasTemplateId || !hasTemplateVersion)
        {
            return InvalidSource();
        }

        if (hasSchema)
        {
            return Result<CodeGenerationPreviewRequest>.Success(
                request.Schema!);
        }

        var template = await templateQueries.GetByIdAsync(
                request.TemplateId!.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (!template.IsSuccess)
        {
            return Result<CodeGenerationPreviewRequest>.Failure(
                template.Error!);
        }

        if (template.Value!.Version != request.TemplateVersion)
        {
            return Result<CodeGenerationPreviewRequest>.Failure(new Error(
                CodeGenerationRunErrorCodes.TemplateVersionConflict,
                "The code generation template version has changed.",
                ErrorType.Conflict));
        }

        return Result<CodeGenerationPreviewRequest>.Success(
            template.Value.Schema);
    }

    private async Task PersistFailureAsync(
        Guid actorUserId,
        Guid? templateId,
        long? templateVersion,
        Error error,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        var hasCompleteTemplateReference =
            templateId.HasValue && templateVersion is > 0;
        var affectedRows = await commandExecutor.ExecuteAsync(
                CodeGenerationRunSql.Insert,
                new
                {
                    Id = idGenerator.NewId(),
                    TemplateId = hasCompleteTemplateReference
                        ? templateId
                        : null,
                    TemplateVersion = hasCompleteTemplateReference
                        ? templateVersion
                        : null,
                    OperationKind = CodeGenerationRunOperationKinds.Preview,
                    Status = CodeGenerationRunStatuses.Failed,
                    ModuleKey = (string?)null,
                    EntityKey = (string?)null,
                    SchemaSha256 = (string?)null,
                    ArtifactCount = 0,
                    ManifestSha256 = (string?)null,
                    ErrorCode = error.Code,
                    RequestedByUserId = actorUserId,
                    StartedAtUtc = startedAtUtc,
                    FinishedAtUtc = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        EnsureInserted(affectedRows);
    }

    private static void EnsureInserted(int affectedRows)
    {
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Code generation run insert affected {affectedRows} rows instead of one.");
        }
    }

    private static Result<CodeGenerationPreviewRequest> InvalidSource() =>
        Result<CodeGenerationPreviewRequest>.Failure(new Error(
            CodeGenerationRunErrorCodes.InvalidSource,
            "Exactly one code generation source is required.",
            ErrorType.Validation));
}
