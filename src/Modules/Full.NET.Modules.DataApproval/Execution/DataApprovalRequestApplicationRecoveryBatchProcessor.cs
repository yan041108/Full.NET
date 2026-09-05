using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;
using Full.NET.Modules.DataApproval.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.DataApproval.Execution;

/// <summary>有界扫描待应用业务变更的 DataApproval 请求。</summary>
internal sealed class DataApprovalRequestApplicationRecoveryBatchProcessor(
    IQueryExecutor queryExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<DataApprovalRequestApplicationRecoveryWorkerOptions> workerOptions,
    IActiveTenantContextResolver tenantResolver,
    ICurrentTenantContextWriter currentTenant,
    DataApprovalRequestApplicationService applicationService,
    ILogger<DataApprovalRequestApplicationRecoveryBatchProcessor> logger)
{
    private readonly DataApprovalRequestApplicationRecoveryWorkerOptions _options = workerOptions.Value;

    /// <summary>扫描并处理一批待应用请求。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本批处理数量。</returns>
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(_options.BatchSize, 1, 50);
        var notBeforeUtc = clock.UtcNow.AddSeconds(-_options.RetryDelaySeconds);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DataApprovalSql.ListPendingApplicationSqlServer,
            DatabaseProvider.MySql => DataApprovalSql.ListPendingApplicationMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<DataApprovalRequestRecord>(
                statement,
                DataApprovalSqlParameters.Create(
                    ("BatchSize", batchSize),
                    ("NotBeforeUtc", notBeforeUtc)),
                cancellationToken)
            .ConfigureAwait(false);
        var processed = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TrySetScopeAsync(row, cancellationToken).ConfigureAwait(false))
            {
                logger.LogWarning(
                    "Skip DataApproval application recovery for request {RequestId} because tenant scope is unavailable.",
                    row.Id);
                continue;
            }

            try
            {
                await applicationService.TryApplyApprovedChangeAsync(
                        row,
                        row.SubmittedByUserId,
                        $"{row.Id:D}:apply-recovery",
                        cancellationToken)
                    .ConfigureAwait(false);
                processed++;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "DataApproval application recovery failed for request {RequestId}.",
                    row.Id);
            }
            finally
            {
                currentTenant.Clear();
            }
        }

        return processed;
    }

    private async Task<bool> TrySetScopeAsync(
        DataApprovalRequestRecord row,
        CancellationToken cancellationToken)
    {
        if (row.ScopeKey == "host" && row.TenantId is null)
        {
            currentTenant.SetHost();
            return true;
        }

        if (row.ScopeKey != "tenant" || row.TenantId is not { } tenantId)
        {
            return false;
        }

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
