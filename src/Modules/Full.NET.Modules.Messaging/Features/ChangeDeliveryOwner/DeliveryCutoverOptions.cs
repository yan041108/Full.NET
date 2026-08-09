namespace Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;

/// <summary>
/// 正式 CDC/Kafka 切流的运维总开关。只有外部 Connector、Topic、ACL、监控和恢复演练
/// 已在目标环境完成后，运维才可显式开启；默认关闭以避免仅部署应用代码就误切流。
/// </summary>
internal sealed class DeliveryCutoverOptions
{
    public const string SectionName = "Messaging:DeliveryCutover";

    public bool Enabled { get; set; }
}
