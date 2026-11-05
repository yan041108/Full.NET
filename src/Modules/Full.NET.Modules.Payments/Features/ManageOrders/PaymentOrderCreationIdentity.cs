using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Encodings.Web;
using Full.NET.Modules.Payments.Contracts;

namespace Full.NET.Modules.Payments.Features.ManageOrders;

/// <summary>订单创建摘要的内部稳定字段；数值和字段顺序独立于 HTTP 线格式，保留已存意图身份。</summary>
internal static class PaymentOrderCreationIdentity
{
    internal static string CreateHash(CreatePaymentOrderRequest request)
    {
        var buffer = new ArrayBufferWriter<byte>();
        // 固定原字段名、顺序、空值和数值格式，避免 HTTP 属性或共享 JSON 选项改变已存意图身份。
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
        {
            writer.WriteStartObject();
            writer.WriteString("TenantId", request.TenantId);
            if (request.MerchantConfigId is Guid merchantId) writer.WriteString("MerchantConfigId", merchantId);
            else writer.WriteNull("MerchantConfigId");
            writer.WriteString("ChannelKey", request.ChannelKey);
            writer.WriteNumber("AmountMinor", request.AmountMinor);
            writer.WriteString("Currency", request.Currency);
            writer.WriteString("Subject", request.Subject);
            writer.WriteString("Description", request.Description);
            writer.WriteString("IdempotencyKey", request.IdempotencyKey);
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }
}
