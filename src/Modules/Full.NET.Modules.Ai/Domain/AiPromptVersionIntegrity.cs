using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>保留格式一正文兼容，分别验证旧文本与显式版本二静态策略的完整规范正文、摘要及模型身份。</summary>
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
                && Hash(body!, row.BodyJson, row.ModelConfigVersion, row.ProviderKey, row.ModelId) == row.BodyHash ? body : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>旧正文继续使用原摘要；静态策略版本二使用独立域，不能移除绑定降级为版本一。</summary>
    internal static string Hash(AiPromptBody body, string json, int modelVersion, string provider, string model) => body.Execution is null
        ? Hash(json, modelVersion, provider, model)
        : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"strategy.v2|{modelVersion}|{provider.Length}:{provider}|{model.Length}:{model}|{json}"))));
}
