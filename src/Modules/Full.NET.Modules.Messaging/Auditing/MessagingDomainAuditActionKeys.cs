namespace Full.NET.Modules.Messaging.Auditing;

internal static class MessagingDomainAuditActionKeys
{
    public const string DeadLetterReplay = "messaging.dead_letter.replay";

    public const string DeliveryCutover = "messaging.delivery.cutover";

    public const string DeliveryRollback = "messaging.delivery.rollback";
}

internal static class MessagingDomainAuditOutcomes
{
    public const string Success = "success";

    public const string Failure = "failure";
}
