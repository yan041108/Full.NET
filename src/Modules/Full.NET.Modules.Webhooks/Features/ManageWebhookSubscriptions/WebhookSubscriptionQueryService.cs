using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions.Persistence;

namespace Full.NET.Modules.Webhooks.Features.ManageWebhookSubscriptions;

internal sealed class WebhookSubscriptionQueryService(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant)
{
    public async Task<Result<IReadOnlyList<WebhookSubscriptionResponse>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentTenant.IsAvailable || currentTenant.Id is null)
        {
            return Result<IReadOnlyList<WebhookSubscriptionResponse>>.Failure(new Error(
                "tenancy.context_not_found",
                "Tenant context was not found.",
                ErrorType.Validation));
        }

        var rows = await queryExecutor.QueryAsync<WebhookSubscriptionRecord>(
                WebhookSubscriptionSql.ListByTenant,
                WebhookSqlParameters.Create(("TenantId", currentTenant.Id.Value)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<IReadOnlyList<WebhookSubscriptionResponse>>.Success(items);
    }

    private static WebhookSubscriptionResponse Map(WebhookSubscriptionRecord row) =>
        new(row.Id, row.TenantId, row.EventType, row.TargetUrl, row.IsActive, row.Version);
}
