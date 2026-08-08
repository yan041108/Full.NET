namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 追加式 Messaging Outbox 与旧轮询 Outbox 的切换配置。
/// </summary>
public sealed class MessagingOutboxOptions
{
    public const string SectionName = "Messaging:Outbox";

    /// <summary>
    /// 默认保持旧 <c>fn_outbox_message</c> 写入，直至受控切换任务显式启用 AppendOnlyV2。
    /// </summary>
    public MessagingOutboxMode Mode { get; set; } = MessagingOutboxMode.Legacy;
}

public enum MessagingOutboxMode
{
    Legacy,
    AppendOnlyV2,
}