using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>创建报表导出任务并同步领取执行；崩溃后由 Worker 按租约恢复。</summary>
/// <param name="definitionQueries">报表定义读取。</param>
/// <param name="resourceFiles">导出文件读取。</param>
/// <param name="queryExecutor">受租户守卫保护的读执行器。</param>
/// <param name="commandExecutor">受租户守卫保护的写执行器。</param>
/// <param name="runner">导出领取与恢复执行器。</param>
/// <param name="currentTenant">当前可信租户。</param>
/// <param name="clock">时钟。</param>
/// <param name="idGenerator">任务 UUID。</param>
internal sealed class ReportingExportTaskManagementService(
    ReportingDefinitionQueryService definitionQueries,
    ITenantResourceFileStore resourceFiles,
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ReportingExportTaskRunner runner,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>创建导出任务并尽量在同一请求内完成；失败时返回已持久化的错误。</summary>
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

        var taskId = idGenerator.NewId();
        var now = clock.UtcNow;
        var parametersJson = ReportingExportTaskMapper.SerializeParameters(request.Parameters);
        var permissionCodes = principal.FindAll(FullNetIdentityClaimTypes.Permission)
            .Select(claim => claim.Value)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
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
            ActorPermissionCodesJson = ReportingExportTaskMapper.SerializePermissionCodes(permissionCodes),
            Version = 1,
        };

        await commandExecutor.ExecuteAsync(
                ReportingExportTaskSql.Insert,
                record,
                cancellationToken)
            .ConfigureAwait(false);

        await runner.RunOwnedAsync(taskId, principal, cancellationToken).ConfigureAwait(false);
        return await LoadResultAsync(taskId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>打开已完成导出任务的文件内容流。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Result<TenantResourceFileContent>> OpenDownloadAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindById,
                ReportingSqlParameters.Create(("Id", taskId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<TenantResourceFileContent>.Failure(TaskNotFoundError());
        }

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
                ReportingExportTaskSql.FindById,
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
