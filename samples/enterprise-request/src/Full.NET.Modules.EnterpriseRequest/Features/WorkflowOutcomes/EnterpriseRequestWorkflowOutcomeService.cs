using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;

namespace Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;

internal sealed class EnterpriseRequestWorkflowOutcomeService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock)
{
    public async Task HandleTerminalWorkflowAsync(
        string businessType,
        string businessId,
        string targetStatus,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(businessType, EnterpriseRequestWorkflowConstants.BusinessType, StringComparison.Ordinal))
        {
            return;
        }

        if (!Guid.TryParse(businessId, out var requestId))
        {
            return;
        }

        if (!EnterpriseRequestStatusTransition.IsTerminal(targetStatus))
        {
            return;
        }

        var row = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                EnterpriseRequestSql.FindByIdStatement,
                new { Id = requestId },
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return;
        }

        if (string.Equals(row.Status, targetStatus, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(row.Status, EnterpriseRequestStatusKeys.Submitted, StringComparison.Ordinal))
        {
            return;
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
                new
                {
                    Id = requestId,
                    TenantId = row.TenantId,
                    Status = targetStatus,
                    UpdatedAtUtc = now,
                    ExpectedStatus = EnterpriseRequestStatusKeys.Submitted,
                    ExpectedVersion = row.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            var latest = await queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                    EnterpriseRequestSql.FindByIdStatement,
                    new { Id = requestId },
                    cancellationToken)
                .ConfigureAwait(false);
            if (latest is null || !string.Equals(latest.Status, targetStatus, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("enterprise_request.status_update_conflict");
            }
        }
    }
}