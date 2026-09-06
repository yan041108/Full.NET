namespace Full.NET.Modules.Notifications.Providers;

/// <summary>Provider 发送时的单个附件载荷；内容已在 Worker 侧完成有界装载。</summary>
internal sealed record NotificationProviderAttachment(
    string FileName,
    string ContentType,
    byte[] Content);
