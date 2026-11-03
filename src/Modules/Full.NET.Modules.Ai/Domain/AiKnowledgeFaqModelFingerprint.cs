using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>冻结收费来源使用的完整批准；仅保存摘要，不落明文凭据或使用可歧义的分隔拼接。</summary>
internal static class AiKnowledgeFaqModelFingerprint
{
    internal static string Create(AiModelConfigRecord model)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var text in new[] { "faq-embedding-model-v1", model.Id.ToString("N"), model.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            model.TenantId?.ToString("N") ?? "host", model.ProviderKey, model.ModelId, model.EndpointBaseUrl, model.OrganizationId ?? "", model.ApiKeyProtected ?? "" })
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
