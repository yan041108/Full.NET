using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantSubscriptions;

internal sealed class NullTenantSubscriptionPaymentFulfillmentPort : ITenantSubscriptionPaymentFulfillmentPort
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
        Task.CompletedTask;
}