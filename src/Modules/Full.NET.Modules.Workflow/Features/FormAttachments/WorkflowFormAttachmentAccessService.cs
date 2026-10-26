using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>校验工作流实例上下文内的附件读取授权，并代理 Files 内容读取。</summary>
internal sealed partial class WorkflowFormAttachmentAccessService(
    IQueryExecutor queryExecutor,
    IHostFileContentReader hostFileContentReader,
    IOptions<DatabaseOptions> databaseOptions,
    ITenantResourceFileStore tenantFiles)
{
    /// <summary>
    /// 在实例仍属于当前作用域且附件仍被提交投影引用时打开文件内容。
    /// </summary>
    /// <param name="instanceId">流程实例标识。</param>
    /// <param name="fileId">附件文件标识。</param>
    /// <param name="tenantScopeKey">可信租户作用域键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>文件内容流或稳定业务错误。</returns>
    public async Task<Result<HostFileContent>> OpenInstanceAttachmentAsync(
        Guid instanceId,
        Guid fileId,
        Guid actorUserId,
        string tenantScopeKey,
        CancellationToken cancellationToken = default)
    {
        if (instanceId == Guid.Empty || fileId == Guid.Empty || actorUserId == Guid.Empty || string.IsNullOrWhiteSpace(tenantScopeKey))
        {
            return Failure(WorkflowErrorCodes.SchemaInvalid, ErrorType.Validation);
        }

        var exists = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                WorkflowFormSubmissionAttachmentSql.ExistsForInstanceFile,
                WorkflowSqlParameters.Create(
                    ("InstanceId", instanceId),
                    ("FileId", fileId),
                    ("TenantScopeKey", tenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (exists != 1)
        {
            return await OpenVoteAttachmentAsync(instanceId, fileId, actorUserId, tenantScopeKey, cancellationToken).ConfigureAwait(false);
        }

        var instance = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,
            WorkflowSqlParameters.Create(("Id", instanceId), ("TenantScopeKey", tenantScopeKey)), cancellationToken).ConfigureAwait(false);
        var asset = instance is null ? null : await queryExecutor.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset,
            WorkflowSqlParameters.Create(("DefinitionVersionId", instance.DefinitionVersionId), ("TenantScopeKey", tenantScopeKey)), cancellationToken).ConfigureAwait(false);
        var schema = asset is null ? null : JsonSerializer.Deserialize(asset.FormSchemaJson, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        if (instance is null || asset is null || schema is null) return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        var allowed = false;
        if (instance.StartedById == actorUserId)
        {
            var submission = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowFormSubmissionRecord>(WorkflowSql.FindFormSubmissionByInstance,
                WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", tenantScopeKey)), cancellationToken).ConfigureAwait(false);
            allowed = submission is not null && ContainsFile(schema, submission.SubmissionJson, null, fileId, null);
        }
        if (!allowed)
        {
            var statement = databaseOptions.Value.Provider switch
            {
                DatabaseProvider.MySql => WorkflowFormSubmissionAttachmentSql.ReadViewsMySql,
                DatabaseProvider.SqlServer => WorkflowFormSubmissionAttachmentSql.ReadViewsSqlServer,
                _ => throw new InvalidOperationException("Unsupported workflow database provider."),
            };
            var views = await queryExecutor.QueryAsync<WorkflowAttachmentReadViewRecord>(statement,
                WorkflowSqlParameters.Create(("InstanceId", instanceId), ("TenantScopeKey", tenantScopeKey), ("ActorUserId", actorUserId)), cancellationToken).ConfigureAwait(false);
            allowed = views.Any(view => WorkflowNodeFieldPolicy.TryResolve(asset.CanonicalJson, view.NodeKey, schema, out var policy) &&
                ContainsFile(schema, view.InputJson, view.OutputJson, fileId, policy));
        }
        if (!allowed) return await OpenVoteAttachmentAsync(instanceId, fileId, actorUserId, tenantScopeKey, cancellationToken).ConfigureAwait(false);

        return await OpenReferencedContentAsync(instanceId, fileId, tenantScopeKey, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostFileContent>> OpenReferencedContentAsync(Guid instanceId, Guid fileId, string tenantScopeKey, CancellationToken cancellationToken)
    {
        if (tenantScopeKey != "host")
        {
            var owner = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowAttachmentFileOwnerRecord>(WorkflowFormSubmissionAttachmentSql.FindFileOwner,
                WorkflowSqlParameters.Create(("InstanceId", instanceId), ("FileId", fileId), ("TenantScopeKey", tenantScopeKey)), cancellationToken).ConfigureAwait(false);
            if (owner?.OwnerResourceId is not { } resourceId) return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
            var tenantContent = await tenantFiles.OpenReadyContentAsync(WorkflowSignatureCoordinator.SignatureOwnerModuleKey,
                resourceId, fileId, cancellationToken).ConfigureAwait(false);
            return tenantContent.IsSuccess ? Result<HostFileContent>.Success(new(tenantContent.Value!.Content, tenantContent.Value.ContentType,
                tenantContent.Value.OriginalFileName)) : Failure(WorkflowErrorCodes.FormAttachmentInvalid, ErrorType.NotFound);
        }

        var content = await hostFileContentReader
            .OpenReadyContentAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
        return content.IsSuccess
            ? content
            : Failure(WorkflowErrorCodes.FormAttachmentInvalid, ErrorType.NotFound);
    }

    private static bool ContainsFile(WorkflowFormSchema schema, string input, string? output, Guid fileId, WorkflowNodeFieldPolicy? policy)
    {
        // 输入和本人输出是两个已授权事实，后来移除字段值不抹去原输入的历史证据。
        return ContainsSnapshot(input) || output is not null && ContainsSnapshot(output);

        bool ContainsSnapshot(string json)
        {
            var values = JsonSerializer.Deserialize(json, WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);
            return values is not null && WorkflowFormAttachmentValueRules.ExtractAttachmentFields(schema, values)
                .Any(field => (policy is null || policy.IsVisible(field.Key)) && field.Value.Contains(fileId));
        }
    }

    private static Result<HostFileContent> Failure(string code, ErrorType type) =>
        Result<HostFileContent>.Failure(new Error(code, "The workflow form attachment is unavailable.", type));
}
