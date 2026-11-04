// Webhook HMAC 签名辅助；载荷规范化与恒定时间比较，防时序侧信道。
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace Full.NET.Modules.Webhooks.Delivery;

/// <summary>签名 v1 绑定原文字节摘要及明确的密钥/投递身份；不改变事件正文或业务发生时间。</summary>
internal static class WebhookSignatureHelper
{
    internal const string Version = "1";
    internal static string ComputeSignature(string secret, string payload, WebhookSignatureStamp stamp)
    {
        if (stamp.KeyId == Guid.Empty || stamp.EventId == Guid.Empty || stamp.DeliveryId == Guid.Empty || stamp.Timestamp < 0
            || string.IsNullOrEmpty(stamp.EventType) || stamp.EventType.Length > 128 || stamp.EventType.Any(value => value < '!' || value > '~'))
            throw new ArgumentException("The webhook signature identity is invalid.", nameof(stamp));
        var canonical = string.Join('\n', "fullnet.webhook/1", stamp.Timestamp.ToString(CultureInfo.InvariantCulture), stamp.KeyId.ToString("D"),
            stamp.EventId.ToString("D"), stamp.DeliveryId.ToString("D"), stamp.EventType, ComputePayloadDigest(payload));
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(canonical)));
    }

    internal static string ComputePayloadDigest(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>每次发送前生成时间，重试保留事件/投递标识；KeyId 来自原订阅的密钥身份。</summary>
internal sealed record WebhookSignatureStamp(Guid KeyId, Guid EventId, Guid DeliveryId, string EventType, long Timestamp);
