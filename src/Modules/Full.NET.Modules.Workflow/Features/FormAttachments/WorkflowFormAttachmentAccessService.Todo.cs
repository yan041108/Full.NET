using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>个人待办文件按精确 Todo 授权；隐藏字段、兄弟分支和本人其他活动的文件均不能借此读取。</summary>
internal sealed partial class WorkflowFormAttachmentAccessService
{
    public async Task<Result<HostFileContent>> OpenTodoAttachmentAsync(Guid todoId, Guid fileId, Guid actor,
        string scope, CancellationToken token = default)
    {
        if (todoId == Guid.Empty || fileId == Guid.Empty || actor == Guid.Empty || string.IsNullOrWhiteSpace(scope))
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        var statement = databaseOptions.Value.Provider switch {
            DatabaseProvider.SqlServer => WorkflowFormSubmissionAttachmentSql.ReadTodoViewSqlServer,
            DatabaseProvider.MySql => WorkflowFormSubmissionAttachmentSql.ReadTodoViewMySql,
            _ => throw new InvalidOperationException("Unsupported workflow database provider.") };
        var view = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowTodoAttachmentReadViewRecord>(statement,
            WorkflowSqlParameters.Create(("TodoId", todoId), ("ActorUserId", actor), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (view is null) return Failure(WorkflowErrorCodes.TodoForbidden, ErrorType.Forbidden);
        var reference = await queryExecutor.QuerySingleOrDefaultAsync<int>(WorkflowFormSubmissionAttachmentSql.ExistsForInstanceFile,
            WorkflowSqlParameters.Create(("InstanceId", view.InstanceId), ("FileId", fileId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
        if (reference == 1)
        {
            var asset = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
                WorkflowSqlParameters.Create(("DefinitionVersionId", view.DefinitionVersionId), ("TenantScopeKey", scope)), token).ConfigureAwait(false);
            var schema = asset is null ? null : JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            if (asset is not null && schema is not null && WorkflowNodeFieldPolicy.TryResolve(asset.CanonicalJson, view.NodeKey, schema, out var policy) &&
                ContainsFile(schema, view.InputJson, view.OutputJson, fileId, policy))
                return await OpenReferencedContentAsync(view.InstanceId, fileId, scope, token).ConfigureAwait(false);
        }
        return await OpenVoteAttachmentAsync(view.InstanceId, fileId, actor, scope, token, todoId).ConfigureAwait(false);
    }
}
