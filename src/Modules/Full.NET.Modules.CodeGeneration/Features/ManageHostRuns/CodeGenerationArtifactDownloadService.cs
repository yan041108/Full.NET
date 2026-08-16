using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Packaging;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>
/// 仅对已成功 Preview/Apply 的运行重新生成产物并打包，不落盘 wwwroot。
/// </summary>
internal sealed class CodeGenerationArtifactDownloadService(
    IQueryExecutor queryExecutor,
    CodeGenerationTemplateQueryService templateQueries,
    CodeGenerationSchemaNormalizer schemaNormalizer)
{
    public async Task<Result<CodeGenerationArtifactZipResponse>> DownloadAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await queryExecutor
            .QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(
                CodeGenerationRunSql.FindById,
                new { Id = runId },
                cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
        {
            return Failure(
                CodeGenerationRunErrorCodes.NotFound,
                "The code generation run was not found.",
                ErrorType.NotFound);
        }

        if (run.Status != CodeGenerationRunStatuses.Succeeded
            || (run.OperationKind != CodeGenerationRunOperationKinds.Preview
                && run.OperationKind != CodeGenerationRunOperationKinds.Apply)
            || !run.TemplateId.HasValue
            || run.ManifestSha256 is null)
        {
            return Failure(
                CodeGenerationRunErrorCodes.InvalidDownloadRun,
                "Only succeeded preview or apply runs can be downloaded.",
                ErrorType.Validation);
        }

        var template = await templateQueries.GetByIdAsync(
                run.TemplateId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (!template.IsSuccess)
        {
            return Result<CodeGenerationArtifactZipResponse>.Failure(
                template.Error!);
        }

        var normalized = schemaNormalizer.Normalize(template.Value!.Schema);
        if (!normalized.IsSuccess)
        {
            return Result<CodeGenerationArtifactZipResponse>.Failure(
                normalized.Error!);
        }

        var artifacts = CrudArtifactGenerator.Generate(normalized.Value!.Schema);
        var manifestSha256 = CodeGenerationRunSummary.ComputeManifestSha256(
            artifacts);
        if (!StringComparer.Ordinal.Equals(manifestSha256, run.ManifestSha256))
        {
            return Failure(
                CodeGenerationRunErrorCodes.StaleApplyPreview,
                "The reviewed code generation preview is stale.",
                ErrorType.Conflict);
        }

        var bytes = GeneratedArtifactZip.Create(artifacts);
        var fileName =
            $"{normalized.Value.Schema.ModuleKey}-{normalized.Value.Schema.EntityKey}-{runId:N}.zip";
        return Result<CodeGenerationArtifactZipResponse>.Success(
            new CodeGenerationArtifactZipResponse(fileName, bytes));
    }

    private static Result<CodeGenerationArtifactZipResponse> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<CodeGenerationArtifactZipResponse>.Failure(
            new Error(code, message, type));
}

/// <summary>内存 zip 与下载文件名；调用方不得把它写成公共 URL。</summary>
internal sealed record CodeGenerationArtifactZipResponse(
    string FileName,
    byte[] Content);
