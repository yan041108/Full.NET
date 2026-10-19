using Full.NET.Modules.Tenancy.Contracts;

/// <summary>模块内端口适配或空实现；供测试或未启用能力时闭合 DI。</summary>
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