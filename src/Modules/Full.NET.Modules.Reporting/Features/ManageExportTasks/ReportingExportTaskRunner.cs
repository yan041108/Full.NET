using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Configuration;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>领取报表导出任务、绑定已上传文件并完成或失败，支持请求内执行与 Worker 崩溃恢复。</summary>
/// <param name="queryExecutor">受租户守卫保护的读执行器。</param>
/// <param name="commandExecutor">受租户守卫保护的写执行器。</param>
/// <param name="transaction">MySQL 领取短事务。</param>
/// <param name="resourceFiles">租户资源文件存储。</param>
/// <param name="workbookSource">工作簿生成。</param>
/// <param name="tenantResolver">活动租户解析。</param>
/// <param name="currentTenant">当前租户写入器。</param>
/// <param name="clock">时钟。</param>
/// <param name="idGenerator">租约 UUID。</param>
/// <param name="databaseOptions">数据库提供程序。</param>
/// <param name="options">导出 Worker 配置。</param>
internal sealed class ReportingExportTaskRunner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ITenantResourceFileStore resourceFiles,
    IReportingExportWorkbookSource workbookSource,
    IActiveTenantContextResolver tenantResolver,
    ICurrentTenantContextWriter currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions,
    IOptionsMonitor<ReportingExportOptions> options)
{
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>领取并执行当前租户中指定任务，供创建请求保持同步完成语义。</summary>
    /// <param name="taskId">刚插入的排队任务。</param>
    /// <param name="principal">创建请求的授权主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RunOwnedAsync(
        Guid taskId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var claimed = await ClaimByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (claimed is null)
        {
            return;
        }

        await ExecuteClaimedAsync(claimed, principal, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按租户目录领取过期或排队的导出任务并恢复执行。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(options.CurrentValue.BatchSize, 1, 200);
        var now = clock.UtcNow;
        var pendingTenantIds = await queryExecutor.QueryAsync<Guid>(
                databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                    ? ReportingExportTaskSql.ListPendingTenantIdsSqlServer
                    : ReportingExportTaskSql.ListPendingTenantIdsMySql,
                ReportingSqlParameters.Create(("BatchSize", batchSize), ("Now", now)),
                cancellationToken)
            .ConfigureAwait(false);
        var processed = 0;
        foreach (var tenantId in pendingTenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processed >= batchSize)
            {
                break;
            }

            try
            {
                if (!await TrySetTenantScopeAsync(tenantId, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                while (processed < batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!await TrySetTenantScopeAsync(tenantId, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    var claimed = await ClaimNextAsync(cancellationToken).ConfigureAwait(false);
                    if (claimed is null)
                    {
                        break;
                    }

                    var principal = RebuildPrincipal(claimed);
                    await ExecuteClaimedAsync(claimed, principal, cancellationToken).ConfigureAwait(false);
                    processed++;
                }
            }
            finally
            {
                currentTenant.Clear();
            }
        }

        return processed;
    }

    /// <summary>先绑定已上传文件，必要时再生成；取消与未知异常不得写成失败。</summary>
    /// <param name="task">已领取快照。</param>
    /// <param name="principal">列权限主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task ExecuteClaimedAsync(
        ReportingExportTaskRecord task,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await TryAttachExistingOutputAsync(task, cancellationToken).ConfigureAwait(false);
            if (existing)
            {
                return;
            }

            var generated = await workbookSource.GenerateAsync(task, principal, cancellationToken)
                .ConfigureAwait(false);
            if (!generated.IsSuccess)
            {
                await CompleteFailedAsync(
                        task,
                        0,
                        generated.Error!.Code,
                        generated.Error.Message,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var file = generated.Value!;
            await using var uploadStream = new MemoryStream(file.Content, writable: false);
            var uploadResult = await resourceFiles
                .UploadAsync(
                    "reporting",
                    task.Id,
                    task.RequestedByUserId,
                    file.FileName,
                    WorkbookContentType,
                    uploadStream,
                    file.Content.LongLength,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!uploadResult.IsSuccess)
            {
                await CompleteFailedAsync(
                        task,
                        file.RowCount,
                        ReportingErrorCodes.ExportFailed,
                        uploadResult.Error?.Message ?? "Failed to store export file.",
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await CompleteSucceededAsync(
                    task,
                    uploadResult.Value!.FileId,
                    file.FileName,
                    file.RowCount,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 请求或宿主取消后保留 processing，租约到期后由 Worker 恢复。
        }
        catch (Exception)
        {
            // 生成或上传可能已产生对象；保留 processing 以便绑定已有文件或重试生成。
        }
    }

    /// <summary>上传成功但完成写入失败时，把资源上已有就绪文件绑定为输出。</summary>
    /// <param name="task">领取快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<bool> TryAttachExistingOutputAsync(
        ReportingExportTaskRecord task,
        CancellationToken cancellationToken)
    {
        if (task.OutputFileId is not null)
        {
            return await CompleteSucceededAsync(
                    task,
                    task.OutputFileId.Value,
                    task.OutputFileName ?? "reporting-export.xlsx",
                    task.RowCount,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var ready = await resourceFiles.ListReadyAsync("reporting", task.Id, cancellationToken)
            .ConfigureAwait(false);
        var latest = ready.LastOrDefault();
        if (latest is null)
        {
            return false;
        }

        return await CompleteSucceededAsync(
                task,
                latest.FileId,
                latest.OriginalFileName,
                task.RowCount,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>按当前租约完成成功；已终态时视为幂等成功。</summary>
    /// <param name="task">领取快照。</param>
    /// <param name="outputFileId">输出文件。</param>
    /// <param name="outputFileName">下载名。</param>
    /// <param name="rowCount">行数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<bool> CompleteSucceededAsync(
        ReportingExportTaskRecord task,
        Guid outputFileId,
        string outputFileName,
        int rowCount,
        CancellationToken cancellationToken)
    {
        var affected = await commandExecutor.ExecuteAsync(
                databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                    ? ReportingExportTaskSql.CompleteSucceededSqlServer
                    : ReportingExportTaskSql.CompleteSucceeded,
                ReportingSqlParameters.Create(
                    ("Id", task.Id),
                    ("LeaseId", task.LeaseId),
                    ("StatusKey", ReportingExportTaskStatusKeys.Succeeded),
                    ("OutputFileId", outputFileId),
                    ("OutputFileName", outputFileName),
                    ("RowCount", rowCount),
                    ("CompletedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected > 0)
        {
            return true;
        }

        var current = await queryExecutor
            .QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                ReportingExportTaskSql.FindByIdFor(databaseOptions.Value.Provider),
                ReportingSqlParameters.Create(("Id", task.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        return current is not null
            && string.Equals(current.StatusKey, ReportingExportTaskStatusKeys.Succeeded, StringComparison.Ordinal);
    }

    /// <summary>按当前租约写入失败；已被抢走租约时停止。</summary>
    /// <param name="task">领取快照。</param>
    /// <param name="rowCount">已收集行数。</param>
    /// <param name="errorCode">稳定错误码。</param>
    /// <param name="errorMessage">失败说明。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task CompleteFailedAsync(
        ReportingExportTaskRecord task,
        int rowCount,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                    ? ReportingExportTaskSql.CompleteFailedSqlServer
                    : ReportingExportTaskSql.CompleteFailed,
                ReportingSqlParameters.Create(
                    ("Id", task.Id),
                    ("LeaseId", task.LeaseId),
                    ("StatusKey", ReportingExportTaskStatusKeys.Failed),
                    ("RowCount", rowCount),
                    ("ErrorCode", errorCode),
                    ("ErrorMessage", errorMessage),
                    ("CompletedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>领取指定排队任务，供创建请求避免与 Worker 双跑。</summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private Task<ReportingExportTaskRecord?> ClaimByIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        ClaimCoreAsync(taskId, cancellationToken);

    /// <summary>领取当前租户下一条可恢复任务。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    private Task<ReportingExportTaskRecord?> ClaimNextAsync(CancellationToken cancellationToken) =>
        ClaimCoreAsync(null, cancellationToken);

    /// <summary>写入新租约；SQL Server 用 OUTPUT，MySQL 用 SKIP LOCKED 短事务。</summary>
    /// <param name="taskId">指定任务；空表示领取队列头部。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<ReportingExportTaskRecord?> ClaimCoreAsync(
        Guid? taskId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var leaseId = idGenerator.NewId();
        var leaseExpiresAt = now.AddSeconds(Math.Clamp(options.CurrentValue.LeaseSeconds, 30, 3600));
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            var statement = taskId is null
                ? ReportingExportTaskSql.ClaimQueuedSqlServer
                : ReportingExportTaskSql.ClaimByIdSqlServer;
            var claimed = await queryExecutor.QueryAsync<ReportingExportTaskRecord>(
                    statement,
                    ReportingSqlParameters.Create(
                        ("Id", taskId),
                        ("Now", now),
                        ("LeaseId", leaseId),
                        ("LeaseExpiresAtUtc", leaseExpiresAt)),
                    cancellationToken)
                .ConfigureAwait(false);
            return claimed.Count == 0 ? null : claimed[0];
        }

        return await transaction.ExecuteAsync(
                async token =>
                {
                    var select = taskId is null
                        ? ReportingExportTaskSql.SelectClaimableIdsMySql
                        : ReportingExportTaskSql.SelectClaimableIdByIdMySql;
                    var ids = await queryExecutor.QueryAsync<Guid>(
                            select,
                            ReportingSqlParameters.Create(("Id", taskId), ("Now", now)),
                            token)
                        .ConfigureAwait(false);
                    if (ids.Count == 0)
                    {
                        return null;
                    }

                    await commandExecutor.ExecuteAsync(
                            ReportingExportTaskSql.ClaimByIdsMySql,
                            ReportingSqlParameters.Create(
                                ("Ids", ids.ToArray()),
                                ("Now", now),
                                ("LeaseId", leaseId),
                                ("LeaseExpiresAtUtc", leaseExpiresAt)),
                            token)
                        .ConfigureAwait(false);
                    var rows = await queryExecutor.QueryAsync<ReportingExportTaskRecord>(
                            ReportingExportTaskSql.SelectByIdsFor(databaseOptions.Value.Provider),
                            ReportingSqlParameters.Create(("Ids", ids.ToArray())),
                            token)
                        .ConfigureAwait(false);
                    return rows.Count == 0 ? null : rows[0];
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>使用创建时权限快照重建主体，避免 Worker 以空权限重跑改变导出列。</summary>
    /// <param name="task">任务快照。</param>
    private static ClaimsPrincipal RebuildPrincipal(ReportingExportTaskRecord task)
    {
        var identity = new ClaimsIdentity("reporting-export-recovery");
        identity.AddClaim(new Claim(FullNetIdentityClaimTypes.Subject, task.RequestedByUserId.ToString("D")));
        foreach (var code in ReportingExportTaskMapper.DeserializePermissionCodes(task.ActorPermissionCodesJson))
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                identity.AddClaim(new Claim(FullNetIdentityClaimTypes.Permission, code));
            }
        }

        return new ClaimsPrincipal(identity);
    }

    /// <summary>绑定活动租户。</summary>
    /// <param name="tenantId">任务所属租户。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<bool> TrySetTenantScopeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await tenantResolver.ResolveActiveByIdAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return false;
        }

        currentTenant.SetTenant(tenant);
        return true;
    }
}
