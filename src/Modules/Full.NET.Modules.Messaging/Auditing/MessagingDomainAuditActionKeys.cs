namespace Full.NET.Modules.Messaging.Auditing;

/// <summary>
/// Messaging 运维操作的稳定域内审计动作键，作为机器契约不可本地化。
/// </summary>
internal static class MessagingDomainAuditActionKeys
{
    public const string DeadLetterReplay = "messaging.dead_letter.replay";

    public const string KafkaRangeReplay = "messaging.kafka.range_replay";

    public const string DeliveryCutover = "messaging.delivery.cutover";

    public const string DeliveryRollback = "messaging.delivery.rollback";
}

/// <summary>
/// Messaging 域内审计结果机值，持久化与协议字段共享同一稳定字符串。
/// </summary>
internal static class MessagingDomainAuditOutcomes
{
    public const string Requested = "requested";

    public const string Success = "success";

    public const string Failure = "failure";
}
