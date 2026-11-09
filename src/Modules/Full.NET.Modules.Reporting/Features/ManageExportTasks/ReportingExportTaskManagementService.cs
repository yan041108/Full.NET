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
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Full.NET.Modules.Reporting.Serialization;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>冻结租户导出请求；由 Worker 领取并生成，API 不占用请求执行外部报表查询。</summary>
/// <param name="definitionQueries">报表定义读取。</param>
/// <param name="resourceFiles">导出文件读取。</param>
/// <param name="queryExecutor">受租户守卫保护的读执行器。</param>
/// <param name="commandExecutor">受租户守卫保护的写执行器。</param>
/// <param name="currentTenant">当前可信租户。</param>
/// <param name="clock">时钟。</param>
/// <param name="idGenerator">任务 UUID。</param>
internal sealed class ReportingExportTaskManagementService(
    ReportingExecutionCatalog definitionQueries,
    ITenantResourceFileStore resourceFiles,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IReportingExportTaskAuthority authority,
    IBackgroundSessionAuthorization sessions,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    IIdentityPermissionEvaluator permissionEvaluator)
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>创建排队任务，固定发布版本、原会话和可见列权限。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="requestedByUserId">已授权主体。</param>
    /// <param name="principal">列权限主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<ReportingExportTaskDetailResponse>> CreateAsync(
        CreateReportingExportTaskRequest request,
        Guid requestedByUserId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTenantContext();
        var tenantId = currentTenant.Id!.Value;
        if (!ReportingExportSessionBinding.TryRead(principal, tenantId, out var binding) || binding.UserId != requestedByUserId)
            return Result<ReportingExportTaskDetailResponse>.Failure(ReportingExportTaskAuthority.Denied());

        if (request is null || request.Parameters is null || request.Parameters.Any(parameter => parameter is null))
            return Result<ReportingExportTaskDetailResponse>.Failure(ExportFailedError("The export request is invalid."));
        if (!string.Equals(request.FormatKey, ReportingExportFormatKeys.Excel, StringComparison.Ordinal))
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(UnsupportedFormatError());
        }

        if (request.Parameters.Any(parameter => string.IsNullOrWhiteSpace(parameter.ParameterKey)))
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(ExportFailedError(
                "Export parameter keys are required."));
        }

        var definitionResult = await definitionQueries.GetByIdAsync(request.DefinitionId, cancellationToken)
            .ConfigureAwait(false);
        if (!definitionResult.IsSuccess || definitionResult.Value is null)
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(definitionResult.Error!);
        }

        var definition = definitionResult.Value;
        if (!definition.IsEnabled)
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(ExportFailedError(
                "The reporting definition is disabled."));
        }

        var versionNumber = request.VersionNumber ?? definition.LatestPublishedVersionNumber;
        if (versionNumber <= 0)
        {
            return Result<ReportingExportTaskDetailResponse>.Failure(new Error(
                ReportingErrorCodes.DefinitionNotPublished,
                "The reporting definition has no published version to export.",
                ErrorType.Validation));
        }

        var version = await definitionQueries.GetVersionAsync(definition.Id, versionNumber, cancellationToken).ConfigureAwait(false);
        if (!version.IsSuccess) return Result<ReportingExportTaskDetailResponse>.Failure(version.Error!);
        var taskId = idGenerator.NewId();
        var now = clock.UtcNow;
        var parametersJson = ReportingExportTaskMapper.SerializeParameters(request.Parameters);
        var permissionCodes = ReportingExportTaskAuthority.FreezePermissions(version.Value!, code => permissionEvaluator.HasPermission(principal, code));
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
            ActorPermissionCodesJson = ReportingExportTaskMapper.SerializePermissionCodes(permissionCodes),
            ActorSessionBindingJson = JsonSerializer.Serialize(binding, ReportingJsonSerializerContext.Default.SessionBindingSnapshot),
            Version = 1,
        };

        if (await authority.ValidateAsync(record, cancellationToken).ConfigureAwait(false) is { } denied)
            return Result<ReportingExportTaskDetailResponse>.Failure(denied);
        await commandExecutor.ExecuteAsync(
                ReportingExportTaskSql.InsertFor(databaseOptions.Value.Provider),
                record,
                cancellationToken)
            .ConfigureAwait(false);

        return await LoadResultAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>打开已完成导出任务的文件内容流。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<TenantResourceFileContent>> OpenDownloadAsync(
        Guid taskId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTenantContext();
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindByIdFor(databaseOptions.Value.Provider),
                ReportingSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (record is null)
        {
            return Result<TenantResourceFileContent>.Failure(TaskNotFoundError());
        }
        if (!ReportingExportSessionBinding.TryRead(principal, currentTenant.Id, out var caller) || caller.UserId != record.RequestedByUserId)
            return Result<TenantResourceFileContent>.Failure(ReportingExportTaskAuthority.Denied());
        var denied = await AuthorizeOutputAsync(record, caller, cancellationToken).ConfigureAwait(false);
        if (denied is not null) return Result<TenantResourceFileContent>.Failure(denied);

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
            cancellationToken.ThrowIfCancellationRequested();
            return Result<TenantResourceFileContent>.Failure(content.Error!);
        }

        var file = content.Value!; var transferred = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await AuthorizeOutputAsync(record, caller, cancellationToken).ConfigureAwait(false) is not null)
                return Result<TenantResourceFileContent>.Failure(ReportingExportTaskAuthority.Denied());
            cancellationToken.ThrowIfCancellationRequested();
            var result = Result<TenantResourceFileContent>.Success(new TenantResourceFileContent(
                file.Content, file.ContentType ?? WorkbookContentType, record.OutputFileName ?? file.OriginalFileName));
            transferred = true; return result;
        }
        finally { if (!transferred) await file.Content.DisposeAsync().ConfigureAwait(false); }
    }

    /// <summary>文件内容与元数据共用原任务、当前会话及受保护列的授权边界。</summary>
    internal async Task<Error?> AuthorizeOutputAsync(ReportingExportTaskRecord record, SessionBindingSnapshot caller,
        CancellationToken cancellationToken)
    {
        if (caller.UserId != record.RequestedByUserId || currentTenant.Id != caller.TenantId)
            return ReportingExportTaskAuthority.Denied();
        var actor = await sessions.AuthorizeAsync(caller, ReportingExportTaskPermissions.Download, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (currentTenant.Id != caller.TenantId || actor is null || actor.UserId != caller.UserId
            || actor.TenantId != caller.TenantId || actor.SessionId != caller.SessionId)
            return ReportingExportTaskAuthority.Denied();
        var denied = await authority.ValidateAsync(record, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return currentTenant.Id == caller.TenantId ? denied : ReportingExportTaskAuthority.Denied();
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
                ErrorType.Validation));
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
