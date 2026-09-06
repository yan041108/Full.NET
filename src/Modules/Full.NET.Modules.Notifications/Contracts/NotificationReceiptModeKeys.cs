namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>Provider 回执能力稳定机器码；与 <see cref="NotificationProviderTypeDescriptor.ReceiptModeKey"/> 对齐。</summary>
public static class NotificationReceiptModeKeys
{
    /// <summary>协议或 Provider 不提供可信送达/退信回执；<c>sent</c> 不得等同于 <c>delivered</c>。</summary>
    public const string None = "none";

    /// <summary>Provider 通过验签 Webhook 提供可信回执；必须注册匹配的 <c>INotificationReceiptVerifier</c>。</summary>
    public const string Signed = "signed";
}
