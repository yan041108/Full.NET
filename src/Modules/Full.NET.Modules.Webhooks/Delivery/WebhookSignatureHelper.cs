using System.Security.Cryptography;
using System.Text;

namespace Full.NET.Modules.Webhooks.Delivery;

internal static class WebhookSignatureHelper
{
    internal static string ComputeSignature(string secret, string payload) =>
        Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(secret),
                    Encoding.UTF8.GetBytes(payload)))
            .ToUpperInvariant();

    internal static string ComputePayloadDigest(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}
