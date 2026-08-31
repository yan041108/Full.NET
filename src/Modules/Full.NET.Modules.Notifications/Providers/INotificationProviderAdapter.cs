using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Providers;

/// <summary>
/// 外部渠道 Adapter 的闭合入口。生产目录允许为空；测试 Adapter 不得进入产品程序集。
/// </summary>
/// <remarks>
/// 生产目录允许为空。Worker 在短事务领取后于事务外调用 <see cref="SendAsync"/>；
/// Provider Accepted 只推进到 Sent，Delivered 只能来自可信回执。
/// </remarks>
internal interface INotificationProviderAdapter
{
    NotificationProviderTypeDescriptor Descriptor { get; }

    ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken);
}

/// <summary>事务外 Provider 调用请求；不得携带明文 Secret。</summary>
internal sealed record NotificationProviderRequest(
    Guid DeliveryId,
    string ChannelKey,
    string RecipientEndpoint,
    string Subject,
    string Body,
    string IdempotencyKey);

/// <summary>Provider 调用结果；Accepted 不等于 Delivered。</summary>
internal sealed record NotificationProviderResult(
    bool Accepted,
    string ResultCategory,
    string? ProviderMessageId,
    TimeSpan? RetryAfter);
