using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Budgets;

/// <summary>绑定完整历史与模型；每段包含字节长度，避免分隔字符导致摘要歧义。</summary>
internal static class AiChatBudgetRequest
{
    internal static AiOperationRequest Create(Guid operationId, AiModelConfigRecord model, IReadOnlyList<(string RoleKey, string Content)> history)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, model.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Append(hash, model.EndpointBaseUrl);
        Append(hash, model.OrganizationId ?? string.Empty);
        long input = 0;
        foreach (var message in history)
        {
            Append(hash, message.RoleKey);
            Append(hash, message.Content);
            input = checked(input + Encoding.UTF8.GetByteCount(message.Content) + 64);
        }
        return new(operationId, null, model.Id, "chat", Convert.ToHexString(hash.GetHashAndReset()), input, AiChatContentPolicy.MaxCompletionTokens, ProviderKey: model.ProviderKey, ModelId: model.ModelId);
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
