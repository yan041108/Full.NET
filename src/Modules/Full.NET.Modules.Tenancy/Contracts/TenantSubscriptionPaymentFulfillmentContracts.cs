namespace Full.NET.Modules.Tenancy.Contracts;

/// <summary>
/// 租户订阅支付履行的对外端口；用于在订阅创建与支付完成时触发权益生效。
/// </summary>
/// <remarks>
/// 实现方须保证：注册履行与完成履行均幂等；完成履行须与订阅状态迁移原子提交（通过 Outbox），避免支付成功但订阅状态未更新；
/// 跨模块事件顺序须为：先写订阅状态再发履行完成事件，调用方不应假设回调即时完成。
/// </remarks>
public interface ITenantSubscriptionPaymentFulfillmentPort
{
    /// <summary>
    /// 注册待履行的支付；订阅创建后调用，等待支付完成回调时触发权益生效。
    /// </summary>
    /// <remarks>
    /// 幂等：同一 (tenantId, subscriptionId) 重复注册视为成功，不产生多条履行记录。
    /// </remarks>
    /// <param name="tenantId">目标租户标识；须与订阅所属租户一致。</param>
    /// <param name="subscriptionId">待履行的订阅标识。</param>
    /// <param name="packageId">套餐标识；<see langword="null"/> 表示自定义订阅，按订阅自身配置履行。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    Task RegisterPendingFulfillmentAsync(
        Guid tenantId,
        Guid subscriptionId,
        Guid? packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在支付完成时触发履行；将订阅状态迁移为 Active 并按套餐配置激活权益。
    /// </summary>
    /// <remarks>
    /// 幂等：同一 externalPaymentReference 重复完成视为成功。完成须与订阅状态迁移原子提交，避免支付成功但订阅仍处于 PastDue。
    /// </remarks>
    /// <param name="tenantId">目标租户标识；须与订阅所属租户一致。</param>
    /// <param name="subscriptionId">待履行的订阅标识。</param>
    /// <param name="externalPaymentReference">外部支付系统返回的支付引用；用于幂等键与对账。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    Task CompleteFromPaymentAsync(
        Guid tenantId,
        Guid subscriptionId,
        string externalPaymentReference,
        CancellationToken cancellationToken = default);
}