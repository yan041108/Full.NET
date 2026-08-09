namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 追加式 Messaging Outbox 与旧轮询 Outbox 的切换配置。
/// </summary>
/// <remarks>
/// 该选项保留到下一个版本用于配置迁移；自引入 DapperRoutedOutboxWriter 起，
/// Outbox 写入不再按全局 Mode 二选一，而是通过 Messaging Topic 目录 + 持久化切流记录
/// 的 <see cref="Full.NET.Messaging.Abstractions.IEffectiveEventDeliveryOwnerResolver"/>
/// 逐流路由。宿主升级时建议删除配置文件中的 Messaging:Outbox:Mode。
/// </remarks>
public sealed class MessagingOutboxOptions
{
    public const string SectionName = "Messaging:Outbox";

    /// <summary>
    /// 默认保持旧 <c>fn_outbox_message</c> 写入，直至受控切换任务显式启用 AppendOnlyV2。
    /// </summary>
    [Obsolete(
        "Outbox routing is now stream-level and driven by " +
        "IEffectiveEventDeliveryOwnerResolver. Remove the Messaging:Outbox:Mode key " +
        "from configuration after one release. See DeliveryCutoverService for per-stream " +
        "cutover controls.",
        error: false)]
    public MessagingOutboxMode Mode { get; set; } = MessagingOutboxMode.Legacy;
}

public enum MessagingOutboxMode
{
    Legacy,
    AppendOnlyV2,
}