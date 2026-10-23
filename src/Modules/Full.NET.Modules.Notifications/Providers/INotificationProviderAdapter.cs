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

    /// <summary>
    /// 需要从受保护收件端点解析的类型键；为 <see langword="null"/> 时沿用收件人稳定键。
    /// </summary>
    string? RecipientEndpointKindKey { get; }

    /// <summary>
    /// 调用后结果未知（是否已送达无法判定）时是否允许自动重试。
    /// </summary>
    /// <remarks>
    /// 只有能够用业务幂等键或可信回执对账证明不会重复发送的厂商才可以返回 <see langword="true"/>；
    /// 默认失败关闭，未知结果会暂挂为 <c>unknown</c> 并等待回执对账或人工处置，不得自动换取新尝试。
    /// </remarks>
    bool AllowsAutomaticRetryAfterUnknownResult { get; }

    ValueTask<NotificationProviderResult> SendAsync(
        NotificationProviderRequest request,
        CancellationToken cancellationToken);
}

/// <summary>事务外 Provider 调用请求；不得携带明文 Secret。</summary>
internal sealed record NotificationProviderRequest(
    Guid DeliveryId,
    string ChannelKey,
    string RecipientEndpoint,
    string NonSecretConfigJson,
    string? SecretReference,
    string Subject,
    string Body,
    string IdempotencyKey,
    IReadOnlyList<NotificationProviderAttachment> Attachments);

/// <summary>Provider 调用结果；Accepted 不等于 Delivered。</summary>
internal sealed record NotificationProviderResult(
    bool Accepted,
    string ResultCategory,
    string? ProviderMessageId,
    TimeSpan? RetryAfter);
