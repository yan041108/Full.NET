using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Git;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;

/// <summary>重试已应用运行的远端发布，不重新生成文件或创建第二次 Apply。</summary>
internal sealed class CodeGenerationPublicationRetryService(IQueryExecutor queries,
    IOptions<CodeGenerationApplyOptions> options, CodeGenerationApplyGate gate, CodeGenerationGitWorkspaceService git)
{
    public async Task<Result<CodeGenerationRunPublicationResponse>> RetryAsync(Guid runId, CancellationToken token)
    {
        if (!options.Value.Enabled || !git.IsActive || !Directory.Exists(options.Value.WorkspaceRoot))
            return Failure(CodeGenerationRunErrorCodes.ApplyDisabled, "Code generation publication is disabled.", ErrorType.Conflict);
        if (!await gate.TryEnterAsync(token).ConfigureAwait(false))
            return Failure(CodeGenerationRunErrorCodes.ApplyBusy, "Another workspace operation is in progress.", ErrorType.Conflict);
        try
        {
            var run = await queries.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(CodeGenerationRunSql.FindById,
                CodeGenerationSqlParameters.Create(("Id", runId)), token).ConfigureAwait(false);
            if (run is null) return Failure(CodeGenerationRunErrorCodes.NotFound, "The code generation run was not found.", ErrorType.NotFound);
            if (run.Status != CodeGenerationRunStatuses.Succeeded
                || run.OperationKind is not (CodeGenerationRunOperationKinds.Apply or CodeGenerationRunOperationKinds.Rollback))
                return Failure(CodeGenerationRunErrorCodes.InvalidPublicationRun, "Only locally applied operations can be published.", ErrorType.Validation);
            var saved = await git.GetSavedPublicationAsync(runId, token).ConfigureAwait(false);
            var result = saved.Status == "published" ? saved : await git.RetryPublicationAsync(runId, token).ConfigureAwait(false);
            return Result<CodeGenerationRunPublicationResponse>.Success(new(runId, result.Status, result.CommitSha));
        }
        finally { await gate.ReleaseAsync().ConfigureAwait(false); }
    }

    private static Result<CodeGenerationRunPublicationResponse> Failure(string code, string message, ErrorType type) =>
        Result<CodeGenerationRunPublicationResponse>.Failure(new Error(code, message, type));
}
