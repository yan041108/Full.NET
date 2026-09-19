namespace Full.NET.Modules.Tenancy.Contracts;

public interface ITenantSubscriptionPaymentFulfillmentPort
{
    Task RegisterPendingFulfillmentAsync(
        Guid tenantId,
        Guid subscriptionId,
        Guid? packageId,
        CancellationToken cancellationToken = default);

    Task CompleteFromPaymentAsync(
        Guid tenantId,
        Guid subscriptionId,
        string externalPaymentReference,
        CancellationToken cancellationToken = default);
}
