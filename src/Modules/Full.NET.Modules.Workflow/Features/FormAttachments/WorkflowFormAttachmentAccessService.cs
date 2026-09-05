using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>校验工作流实例上下文内的附件读取授权，并代理 Files 内容读取。</summary>
internal sealed class WorkflowFormAttachmentAccessService(
    IQueryExecutor queryExecutor,
    IHostFileContentReader hostFileContentReader)
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
        string tenantScopeKey,
        CancellationToken cancellationToken = default)
    {
        if (instanceId == Guid.Empty || fileId == Guid.Empty || string.IsNullOrWhiteSpace(tenantScopeKey))
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
            return Failure(WorkflowErrorCodes.InstanceForbidden, ErrorType.Forbidden);
        }

        var content = await hostFileContentReader
            .OpenReadyContentAsync(fileId, cancellationToken)
            .ConfigureAwait(false);
        return content.IsSuccess
            ? content
            : Failure(WorkflowErrorCodes.FormAttachmentInvalid, ErrorType.NotFound);
    }

    private static Result<HostFileContent> Failure(string code, ErrorType type) =>
        Result<HostFileContent>.Failure(new Error(code, "The workflow form attachment is unavailable.", type));
}
