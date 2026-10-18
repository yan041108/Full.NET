namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>通知所有者提供的投递摘要；受理及渠道发送均不能证明用户已经阅读。</summary>
/// <param name="IntentId">受理意图标识。</param>
/// <param name="AcceptedAtUtc">意图受理时间。</param>
/// <param name="TotalDeliveryCount">外部渠道投递总数；站内信不计入。</param>
/// <param name="PendingDeliveryCount">等待首次发送或后续重试的投递数，不推定已经重试。</param>
/// <param name="SentDeliveryCount">提供程序已确认发送成功的投递数。</param>
/// <param name="FailedDeliveryCount">不可重试的失败数。</param>
/// <param name="DeadLetteredDeliveryCount">重试耗尽的投递数。</param>
/// <param name="UnknownDeliveryCount">结果未知且未安排重试的投递数。</param>
/// <param name="OtherDeliveryCount">尚未认识的状态数；不能视作成功。</param>
/// <param name="NextAttemptAtUtc">已安排投递的最早尝试时间，不保证准点执行。</param>
/// <param name="PersistedDeliveryCount">已持久化、尚未进入可领取状态的投递数。</param>
/// <param name="DeliveredDeliveryCount">提供程序回执确认送达的投递数。</param>
/// <param name="ReadDeliveryCount">提供程序回执确认已读的投递数。</param>
/// <param name="SuppressedDeliveryCount">按通知策略抑制的投递数。</param>
public sealed record NotificationIntentDeliverySnapshot(
    Guid IntentId, DateTimeOffset AcceptedAtUtc, int TotalDeliveryCount,
    int PendingDeliveryCount, int SentDeliveryCount, int FailedDeliveryCount,
    int DeadLetteredDeliveryCount, int UnknownDeliveryCount, int OtherDeliveryCount,
    DateTimeOffset? NextAttemptAtUtc, int PersistedDeliveryCount = 0,
    int DeliveredDeliveryCount = 0, int ReadDeliveryCount = 0, int SuppressedDeliveryCount = 0);

/// <summary>供已验证业务资源授权的模块读取通知摘要，不公开收件人、地址、模板及参数。</summary>
public interface INotificationIntentDeliveryDirectory
{
    /// <summary>仅在当前可信 Host/租户作用域查找固定生产者及幂等键，未受理时返回空值。</summary>
    /// <param name="producerKey">消费模块认可的固定生产者键，不直接接收客户端输入。</param>
    /// <param name="idempotencyKey">来自业务回执的可靠事件幂等键。</param>
    /// <param name="cancellationToken">取消读取，不改变通知或业务状态。</param>
    /// <returns>当前通知摘要，或尚未受理的空值；异常保持传播。</returns>
    Task<NotificationIntentDeliverySnapshot?> FindByIdempotencyAsync(
        string producerKey, string idempotencyKey, CancellationToken cancellationToken = default);
}
