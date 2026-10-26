using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>只向本人待办及实际签署人返回证据；租户和动作身份来自服务端受信上下文。</summary>
internal sealed class WorkflowSignatureReadService(IQueryExecutor query, IHostFileContentReader files, IOptions<DatabaseOptions> database,
    ITenantResourceFileStore tenantFiles)
{
    public async Task<Result<IReadOnlyList<WorkflowSignatureEvidenceResponse>>> ListAsync(Guid todoId, Guid actorId,
        string tenantScopeKey, CancellationToken token)
    {
        var parameters = WorkflowSqlParameters.Create(("TodoId", todoId), ("ActorUserId", actorId), ("TenantScopeKey", tenantScopeKey));
        if (await query.QuerySingleOrDefaultAsync<int>(WorkflowSignatureSql.CanReadTodo, parameters, token).ConfigureAwait(false) != 1)
            return Result<IReadOnlyList<WorkflowSignatureEvidenceResponse>>.Failure(Forbidden());
        var sql = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => WorkflowSignatureSql.ListSqlServer,
            DatabaseProvider.MySql => WorkflowSignatureSql.ListMySql,
            _ => throw new InvalidOperationException("Unsupported workflow database provider."),
        };
        var rows = await query.QueryAsync<WorkflowSignatureEvidenceRecord>(sql, parameters, token).ConfigureAwait(false);
        return Result<IReadOnlyList<WorkflowSignatureEvidenceResponse>>.Success(rows.Select(row => new WorkflowSignatureEvidenceResponse(
            row.Id, row.ActorUserId, row.OriginalAssigneeUserId, row.ActionKey, row.FormVersionId,
            row.FormRevision, row.SubmissionHash, row.FileHash, row.CreatedAtUtc)).ToArray());
    }

    public async Task<Result<HostFileContent>> OpenAsync(Guid todoId, Guid evidenceId, Guid actorId, string tenantScopeKey, CancellationToken token)
    {
        var row = await query.QuerySingleOrDefaultAsync<WorkflowSignatureEvidenceRecord>(WorkflowSignatureSql.FindOwned,
            WorkflowSqlParameters.Create(("TodoId", todoId), ("Id", evidenceId), ("ActorUserId", actorId), ("TenantScopeKey", tenantScopeKey)), token).ConfigureAwait(false);
        if (row is null) return Result<HostFileContent>.Failure(Forbidden());
        if (tenantScopeKey != "host")
        {
            var result = await tenantFiles.OpenReadyContentAsync(WorkflowSignatureCoordinator.SignatureOwnerModuleKey,
                row.ActorUserId, row.FileId, token).ConfigureAwait(false);
            return result.IsSuccess ? Result<HostFileContent>.Success(new(result.Value!.Content, result.Value.ContentType, result.Value.OriginalFileName))
                : Result<HostFileContent>.Failure(result.Error!);
        }
        return await files.OpenReadyContentAsync(row.FileId, token).ConfigureAwait(false);
    }

    private static Error Forbidden() => new(WorkflowErrorCodes.TodoForbidden, "The signature evidence is not available to this actor.", ErrorType.Forbidden);
}
