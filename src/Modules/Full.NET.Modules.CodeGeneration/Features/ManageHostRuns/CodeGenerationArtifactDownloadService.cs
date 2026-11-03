using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>鉴权入口只读取当次不可变包，模板变动、删除或成功回滚不改变历史下载。</summary>
internal sealed class CodeGenerationArtifactDownloadService(IQueryExecutor queries, CodeGenerationArtifactStore archives)
{
    public async Task<Result<CodeGenerationArtifactZipResponse>> DownloadAsync(Guid runId, CancellationToken token = default)
    {
        var run = await queries.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(CodeGenerationRunSql.FindById,
            CodeGenerationSqlParameters.Create(("Id", runId)), token).ConfigureAwait(false);
        if (run is null) return Failure(CodeGenerationRunErrorCodes.NotFound, ErrorType.NotFound);
        if (run.Status != "succeeded" || run.OperationKind is not ("preview" or "apply") || run.ManifestSha256 is null)
            return Failure(CodeGenerationRunErrorCodes.InvalidDownloadRun, ErrorType.Validation);
        try
        {
            var archive = await archives.ReadAsync(runId, token).ConfigureAwait(false);
            if (archive.Record.SchemaSha256 != run.SchemaSha256 || archive.Record.ManifestSha256 != run.ManifestSha256)
                return Failure(CodeGenerationRunErrorCodes.ArchiveCorrupt, ErrorType.Conflict);
            return Result<CodeGenerationArtifactZipResponse>.Success(new(archive.Record.FileName, archive.Bytes));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(exception is CodeGenerationArchiveException archive ? archive.Code : CodeGenerationRunErrorCodes.ArchiveUnavailable, ErrorType.Conflict);
        }
    }

    private static Result<CodeGenerationArtifactZipResponse> Failure(string code, ErrorType type) =>
        Result<CodeGenerationArtifactZipResponse>.Failure(new Error(code, "The saved code generation artifact is unavailable.", type));
}

/// <summary>不可变 zip 与安全文件名，不发布公共静态地址。</summary>
internal sealed record CodeGenerationArtifactZipResponse(string FileName, byte[] Content);
