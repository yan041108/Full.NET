using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>选票附件只有出现在本人本次待办私有输出时可读，发起人和管理员不能借实例身份读取。</summary>
internal sealed partial class WorkflowFormAttachmentAccessService
{
    private async Task<Result<HostFileContent>> OpenVoteAttachmentAsync(Guid instanceId, Guid fileId, Guid actor,
        string scope, CancellationToken token, Guid? todoId = null)
    {
        var statement = (databaseOptions.Value.Provider, todoId.HasValue) switch {
            (DatabaseProvider.SqlServer, true) => WorkflowVoteAttachmentSql.ReadOwnedForTodoSqlServer,
            (DatabaseProvider.MySql, true) => WorkflowVoteAttachmentSql.ReadOwnedForTodoMySql,
            (DatabaseProvider.SqlServer, false) => WorkflowVoteAttachmentSql.ReadOwnedSqlServer,
            (DatabaseProvider.MySql, false) => WorkflowVoteAttachmentSql.ReadOwnedMySql,
            _ => throw new InvalidOperationException("Unsupported workflow database provider.") };
        var rows = await queryExecutor.QueryAsync<WorkflowVoteAttachmentViewRecord>(statement,
            WorkflowSqlParameters.Create(("InstanceId", instanceId), ("FileId", fileId), ("ActorUserId", actor),
                ("TenantScopeKey", scope), ("TodoId", todoId)), token).ConfigureAwait(false);
        WorkflowVoteAttachmentViewRecord? authorized = null;
        foreach (var row in rows)
        {
            try
            {
                var schema = JsonSerializer.Deserialize(row.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
                using var output = JsonDocument.Parse(row.OutputJson);
                var envelope = output.RootElement;
                if (schema is null || envelope.ValueKind != JsonValueKind.Object || envelope.EnumerateObject().Count() != 2 ||
                    !envelope.TryGetProperty("choiceKey", out var choice) || choice.ValueKind != JsonValueKind.String ||
                    !envelope.TryGetProperty("values", out var values) ||
                    !WorkflowVoteSubmissionRules.TryCompile(schema, values, choice.GetString(), true, out _)) continue;
                var answers = values.EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);
                if (WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, answers).Values.Any(files => files.Contains(fileId)))
                { authorized = row; break; }
            }
            catch (JsonException) { }
        }
        if (authorized is null) return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        if (scope == "host")
        {
            var file = await hostFileContentReader.OpenReadyContentAsync(fileId, token).ConfigureAwait(false);
            return file.IsSuccess ? file : Failure(WorkflowErrorCodes.FormAttachmentInvalid, ErrorType.NotFound);
        }
        if (authorized.OwnerResourceId is not { } owner) return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        var content = await tenantFiles.OpenReadyContentAsync(WorkflowSignatureCoordinator.SignatureOwnerModuleKey, owner, fileId, token).ConfigureAwait(false);
        return content.IsSuccess ? Result<HostFileContent>.Success(new(content.Value!.Content, content.Value.ContentType, content.Value.OriginalFileName))
            : Failure(WorkflowErrorCodes.FormAttachmentInvalid, ErrorType.NotFound);
    }
}
