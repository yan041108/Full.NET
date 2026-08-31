using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>渠道投递状态；Sent 不等于 Delivered，Unknown 不能推断成功。</summary>
internal enum NotificationDeliveryStatus
{
    Persisted,
    Accepted,
    Sent,
    Delivered,
    Unknown,
    Read,
    Failed,
    Suppressed,
    DeadLettered,
}

/// <summary>状态变迁来源；只有可信回执才能把外部渠道推进到 Delivered。</summary>
internal enum NotificationStatusSource
{
    Provider,
    Receipt,
    User,
    Operator,
}

/// <summary>状态机结果；未应用的乱序回执保留当前终态。</summary>
internal sealed record NotificationStatusTransition(
    bool Applied,
    NotificationDeliveryStatus Status,
    bool IsDuplicate,
    string? ErrorCode);

/// <summary>
/// 投递状态只能按允许的单调图推进。
/// </summary>
/// <remarks>
/// 可信回执可以把 Sent/Unknown 推进到 Delivered；Provider 自报送达只能落到 Unknown。
/// 乱序或重复回执不得把 Delivered/Read/DeadLettered 回退。
/// </remarks>
internal static class NotificationDeliveryStateMachine
{
    public static NotificationStatusTransition Apply(
        NotificationDeliveryStatus current,
        NotificationDeliveryStatus incoming,
        NotificationStatusSource source)
    {
        if (incoming == current)
        {
            return new NotificationStatusTransition(false, current, true, null);
        }

        if (incoming == NotificationDeliveryStatus.Delivered
            && source != NotificationStatusSource.Receipt)
        {
            return new NotificationStatusTransition(
                false,
                current,
                false,
                NotificationsErrorCodes.DeliveryUntrustedDelivered);
        }

        if (!IsAllowed(current, incoming, source))
        {
            return new NotificationStatusTransition(false, current, false, NotificationsErrorCodes.DeliveryTransitionIllegal);
        }

        return new NotificationStatusTransition(true, incoming, false, null);
    }

    private static bool IsAllowed(
        NotificationDeliveryStatus current,
        NotificationDeliveryStatus incoming,
        NotificationStatusSource source) =>
        (current, incoming, source) switch
        {
            (NotificationDeliveryStatus.Persisted, NotificationDeliveryStatus.Accepted, _) => true,
            (NotificationDeliveryStatus.Persisted, NotificationDeliveryStatus.Suppressed, _) => true,
            (NotificationDeliveryStatus.Persisted, NotificationDeliveryStatus.Failed, _) => true,
            (NotificationDeliveryStatus.Persisted, NotificationDeliveryStatus.DeadLettered, NotificationStatusSource.Operator) => true,
            (NotificationDeliveryStatus.Persisted, NotificationDeliveryStatus.Read, NotificationStatusSource.User) => true,
            (NotificationDeliveryStatus.Accepted, NotificationDeliveryStatus.Sent, NotificationStatusSource.Provider) => true,
            (NotificationDeliveryStatus.Accepted, NotificationDeliveryStatus.Unknown, _) => true,
            (NotificationDeliveryStatus.Accepted, NotificationDeliveryStatus.Failed, _) => true,
            (NotificationDeliveryStatus.Accepted, NotificationDeliveryStatus.Suppressed, _) => true,
            (NotificationDeliveryStatus.Accepted, NotificationDeliveryStatus.DeadLettered, NotificationStatusSource.Operator) => true,
            (NotificationDeliveryStatus.Sent, NotificationDeliveryStatus.Delivered, NotificationStatusSource.Receipt) => true,
            (NotificationDeliveryStatus.Sent, NotificationDeliveryStatus.Unknown, _) => true,
            (NotificationDeliveryStatus.Sent, NotificationDeliveryStatus.Failed, _) => true,
            (NotificationDeliveryStatus.Sent, NotificationDeliveryStatus.Read, NotificationStatusSource.User) => true,
            (NotificationDeliveryStatus.Sent, NotificationDeliveryStatus.DeadLettered, NotificationStatusSource.Operator) => true,
            (NotificationDeliveryStatus.Unknown, NotificationDeliveryStatus.Delivered, NotificationStatusSource.Receipt) => true,
            (NotificationDeliveryStatus.Unknown, NotificationDeliveryStatus.Failed, _) => true,
            (NotificationDeliveryStatus.Unknown, NotificationDeliveryStatus.Read, NotificationStatusSource.User) => true,
            (NotificationDeliveryStatus.Unknown, NotificationDeliveryStatus.DeadLettered, NotificationStatusSource.Operator) => true,
            (NotificationDeliveryStatus.Delivered, NotificationDeliveryStatus.Read, NotificationStatusSource.User) => true,
            (NotificationDeliveryStatus.Failed, NotificationDeliveryStatus.DeadLettered, NotificationStatusSource.Operator) => true,
            (NotificationDeliveryStatus.Failed, NotificationDeliveryStatus.Accepted, NotificationStatusSource.Operator) => true,
            (NotificationDeliveryStatus.DeadLettered, NotificationDeliveryStatus.Accepted, NotificationStatusSource.Operator) => true,
            _ => false,
        };
}
