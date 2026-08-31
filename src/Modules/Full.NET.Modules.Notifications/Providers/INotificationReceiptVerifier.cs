using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Notifications.Providers;

/// <summary>Provider 专用回执验签器；只接收闭合输入，原始 Body 不得进入日志或数据库全文。</summary>
internal interface INotificationReceiptVerifier
{
    string ProviderTypeKey { get; }

    Result<VerifiedNotificationReceipt> Verify(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string> headers);
}

/// <summary>验签后的回执；载荷只保留摘要与映射状态。</summary>
internal sealed record VerifiedNotificationReceipt(
    string ReceiptIdempotencyKey,
    string? ProviderMessageId,
    string ExternalStatusKey,
    string MappedStatusKey,
    string PayloadDigest);
