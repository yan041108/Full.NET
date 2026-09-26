using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;

/// <summary>测试/人工渠道订阅履约：支付完成时将订阅迁移为 Active。</summary>
internal sealed class TestChannelTenantSubscriptionPaymentFulfillmentPort(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock) : ITenantSubscriptionPaymentFulfillmentPort
{
    public Task RegisterPendingFulfillmentAsync(
        Guid tenantId,
        Guid subscriptionId,
        Guid? packageId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CompleteFromPaymentAsync(
        Guid tenantId,
        Guid subscriptionId,
        string externalPaymentReference,
        CancellationToken cancellationToken = default) =>
        CompleteCoreAsync(tenantId, subscriptionId, externalPaymentReference, cancellationToken);

    private async Task CompleteCoreAsync(
        Guid tenantId,
        Guid subscriptionId,
        string externalPaymentReference,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalPaymentReference))
        {
            throw new InvalidOperationException(TenancyErrorCodes.SubscriptionStatusInvalid);
        }

        await transaction.ExecuteAsync(
                async token =>
                {
                    var rows = await queryExecutor.QueryAsync<TenantSubscriptionRecord>(
                            TenantSubscriptionSql.ListByTenant,
                            Tenancy.Persistence.TenancySqlParameters.Create(("TenantId", tenantId)),
                            token)
                        .ConfigureAwait(false);
                    var subscription = rows.FirstOrDefault(row => row.Id == subscriptionId)
                        ?? throw new InvalidOperationException(TenancyErrorCodes.SubscriptionStatusInvalid);
                    if (subscription.Status == TenantSubscriptionStatuses.Active)
                    {
                        return Result<bool>.Success(true);
                    }

                    var affected = await commandExecutor.ExecuteAsync(
                            TenantSubscriptionSql.ActivateFromPayment,
                            Tenancy.Persistence.TenancySqlParameters.Create(
                                ("Id", subscriptionId),
                                ("TenantId", tenantId),
                                ("Status", TenantSubscriptionStatuses.Active),
                                ("UpdatedAtUtc", clock.UtcNow),
                                ("Version", subscription.Version)),
                            token)
                        .ConfigureAwait(false);
                    if (affected != 1)
                    {
                        throw new InvalidOperationException(TenancyErrorCodes.SubscriptionStatusInvalid);
                    }

                    return Result<bool>.Success(true);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}
