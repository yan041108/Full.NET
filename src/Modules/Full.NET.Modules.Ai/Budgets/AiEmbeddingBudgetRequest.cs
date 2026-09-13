using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Budgets;

/// <summary>绑定完整 Embedding 输入与模型配置，供统一预算账本预留。</summary>
internal static class AiEmbeddingBudgetRequest
{
    internal static AiOperationRequest Create(Guid operationId, AiModelConfigRecord model, IReadOnlyList<string> inputs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, model.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, model.EndpointBaseUrl);
        Append(hash, model.OrganizationId ?? string.Empty);
        long input = 0;
        foreach (var text in inputs)
        {
            Append(hash, text);
            input = checked(input + Encoding.UTF8.GetByteCount(text) + 32);
        }
        return new(
            operationId,
            null,
            model.Id,
            "embedding",
            Convert.ToHexString(hash.GetHashAndReset()),
            input,
            0,
            ProviderKey: model.ProviderKey,
            ModelId: model.ModelId);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
