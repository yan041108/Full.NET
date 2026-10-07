using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>创建报表导出任务并同步领取执行；崩溃后由 Worker 按租约恢复。</summary>
/// <param name="publishedDefinitions">报表定义读取。</param>
/// <param name="resourceFiles">导出文件读取。</param>
/// <param name="queryExecutor">受租户守卫保护的读执行器。</param>
/// <param name="commandExecutor">受租户守卫保护的写执行器。</param>
/// <param name="runner">导出领取与恢复执行器。</param>
/// <param name="currentTenant">当前可信租户。</param>
/// <param name="clock">时钟。</param>
/// <param name="idGenerator">任务 UUID。</param>
/// <param name="authorization">当前会话与精确权限权威校验。</param>
/// <param name="permissions">身份模块统一的有效权限快照解释器。</param>
internal sealed class ReportingExportTaskManagementService(
    Full.NET.Modules.Reporting.Features.PublishedDefinitions.ReportingPublishedDefinitionResolver publishedDefinitions,
    ITenantResourceFileStore resourceFiles,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ReportingExportTaskRunner runner,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    ReportingExportAuthorization authorization,
    IIdentityPermissionEvaluator permissions)
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>创建导出任务并尽量在同一请求内完成；失败时返回已持久化的错误。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="requestedByUserId">已授权主体。</param>
    /// <param name="principal">列权限主体。</param>
    /// <param name="binding">创建请求的交互会话委托。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<ReportingExportTaskDetailResponse>> CreateAsync(
        CreateReportingExportTaskRequest request,
        Guid requestedByUserId,
        ClaimsPrincipal principal,
        SessionBindingSnapshot binding,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var tenantId = currentTenant.Id!.Value;

        if (!string.Equals(request.FormatKey, ReportingExportFormatKeys.Excel, StringComparison.Ordinal))
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(UnsupportedFormatError());
        }

        if (request.Parameters.Any(parameter => string.IsNullOrWhiteSpace(parameter.ParameterKey)))
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(ExportFailedError(
                "Export parameter keys are required."));
        }

        var published = await publishedDefinitions.ResolveAsync(request.DefinitionId, request.VersionNumber, cancellationToken).ConfigureAwait(false);
        if (!published.IsSuccess) return Result<ReportingExportTaskDetailResponse>.Failure(published.Error!);
        var definition = published.Value!.Definition;
        var versionNumber = published.Value.Version.VersionNumber;

        var taskId = idGenerator.NewId();
        var now = clock.UtcNow;
        var parametersJson = ReportingExportTaskMapper.SerializeParameters(request.Parameters);
        var permissionCodes = permissions.ResolvePermissions(principal)
            .Where(code => permissions.HasPermission(principal, code))
            .ToArray();
        var record = new ReportingExportTaskRecord
        {
            Id = taskId,
            TenantId = tenantId,
            DefinitionId = definition.Id,
            VersionNumber = versionNumber,
            DefinitionKey = definition.DefinitionKey,
            DefinitionName = definition.Name,
            FormatKey = request.FormatKey,
            ParametersJson = parametersJson,
            StatusKey = ReportingExportTaskStatusKeys.Queued,
            RowCount = 0,
            RequestedByUserId = requestedByUserId,
            CreatedAtUtc = now,
            ActorPermissionCodesJson = ReportingExportTaskMapper.SerializeAuthorization(permissionCodes, binding),
            Version = 1,
        };

        if (!await authorization.CanRunAsync(record, cancellationToken).ConfigureAwait(false))
            return Result<ReportingExportTaskDetailResponse>.Failure(new(CommonErrorCodes.PermissionDenied,
                "The export session or permission is no longer valid.", ErrorType.Forbidden));

        await commandExecutor.ExecuteAsync(
                ReportingExportTaskSql.InsertFor(databaseOptions.Value.Provider),
                record,
                cancellationToken)
            .ConfigureAwait(false);

        await runner.RunOwnedAsync(taskId, cancellationToken).ConfigureAwait(false);
        return await LoadResultAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按创建人的当前会话及原受保护列权限复核后打开文件；不重新执行报表查询。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="binding">从已认证请求冻结的当前交互会话。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<TenantResourceFileContent>> OpenDownloadAsync(
        Guid taskId,
        SessionBindingSnapshot binding,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindByIdFor(databaseOptions.Value.Provider),
                ReportingSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<TenantResourceFileContent>.Failure(TaskNotFoundError());
        }

        if (!await authorization.CanDownloadAsync(record, binding, cancellationToken).ConfigureAwait(false))
            return Result<TenantResourceFileContent>.Failure(new Error(CommonErrorCodes.PermissionDenied,
                "The export download session or permission is no longer valid.", ErrorType.Forbidden));

        if (!string.Equals(record.StatusKey, ReportingExportTaskStatusKeys.Succeeded, StringComparison.Ordinal)
            || record.OutputFileId is null)
        {
            return Result<TenantResourceFileContent>.Failure(new Error(
                ReportingErrorCodes.ExportTaskNotReady,
                "The reporting export task is not ready for download.",
                ErrorType.Validation));
        }

        var content = await resourceFiles
            .OpenReadyContentAsync("reporting", taskId, record.OutputFileId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!content.IsSuccess)
        {
            return Result<TenantResourceFileContent>.Failure(content.Error!);
        }

        var file = content.Value!;
        return Result<TenantResourceFileContent>.Success(new TenantResourceFileContent(
            file.Content,
            file.ContentType ?? WorkbookContentType,
            record.OutputFileName ?? file.OriginalFileName));
    }

    /// <summary>把持久化终态映射为创建 API 结果，失败任务仍返回业务错误。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<Result<ReportingExportTaskDetailResponse>> LoadResultAsync(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var detail = await queryExecutor
            .QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindByIdFor(databaseOptions.Value.Provider),
                ReportingSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(TaskNotFoundError());
        }

        if (string.Equals(detail.StatusKey, ReportingExportTaskStatusKeys.Failed, StringComparison.Ordinal))
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(new Error(
                detail.ErrorCode ?? ReportingErrorCodes.ExportFailed,
                detail.ErrorMessage ?? "Reporting export failed.",
                string.Equals(detail.ErrorCode, CommonErrorCodes.PermissionDenied, StringComparison.Ordinal)
                    ? ErrorType.Forbidden
                    : ErrorType.Validation));
        }

        return Result<ReportingExportTaskDetailResponse>.Success(ReportingExportTaskMapper.MapDetail(detail));
    }

    private void EnsureTenantContext()
    {
        if (currentTenant.Id is null)
        {
            throw new InvalidOperationException("Tenant context is required for reporting export tasks.");
        }
    }

    private static Error UnsupportedFormatError() =>
        new(ReportingErrorCodes.ExportFormatUnsupported, "The export format is not supported.", ErrorType.Validation);

    private static Error ExportFailedError(string message) =>
        new(ReportingErrorCodes.ExportFailed, message, ErrorType.Validation);

    private static Error TaskNotFoundError() =>
        new(ReportingErrorCodes.ExportTaskNotFound, "The reporting export task was not found.", ErrorType.NotFound);
}
