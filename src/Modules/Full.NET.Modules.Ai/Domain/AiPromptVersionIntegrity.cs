using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>格式一固定纯文本 Schema、静态 single-text 定义、零工具与无检索；验证完整规范正文及模型身份。</summary>
internal static class AiPromptVersionIntegrity
{
    internal static string Serialize(AiPromptBody body) => JsonSerializer.Serialize(body, AiJsonSerializerContext.Default.AiPromptBody);
    internal static string Hash(string bodyJson, int modelVersion, string provider, string model) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"text.v1|fullnet-single-text-v1|none|{modelVersion}|{provider.Length}:{provider}|{model.Length}:{model}|{bodyJson}"))));

    internal static AiPromptBody? Decode(AiPromptVersionRecord row)
    {
        if (row.Id == Guid.Empty || row.PromptId == Guid.Empty || row.Version < 1 || row.FormatVersion != 1
            || row.ModelConfigVersion < 1 || row.BodyJson.Length > 65_536 || row.BodyHash.Length != 64
            || row.ProviderKey.Length is < 1 or > 64 || row.ModelId.Length is < 1 or > 256
            || !AiPromptPolicy.ValidUnicode(row.ProviderKey) || !AiPromptPolicy.ValidUnicode(row.ModelId)) return null;
        try
        {
            var body = JsonSerializer.Deserialize(row.BodyJson, AiJsonSerializerContext.Default.AiPromptBody);
            // 存储只接受源生成的规范表示；重复属性、伪造摘要或 Unicode 替代不得换成可执行新策略。
            return AiPromptPolicy.IsValid(body) && Serialize(body!) == row.BodyJson
                && Hash(row.BodyJson, row.ModelConfigVersion, row.ProviderKey, row.ModelId) == row.BodyHash ? body : null;
        }
        catch (JsonException) { return null; }
    }
}
