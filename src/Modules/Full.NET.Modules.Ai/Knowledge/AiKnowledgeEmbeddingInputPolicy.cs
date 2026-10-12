using System.Text;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>仅对已核准官方 Embedding 空间执行固定离线词表准入，不替代授权、网络门禁或预算账本。</summary>
internal static class AiKnowledgeEmbeddingInputPolicy
{
    // 官方接口为 8192；沿用官方 Cookbook 的 8191 保守边界，预留一个 Token 的余量。
    internal const int MaxInputTokens = 8191;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>兼容协议或相同模型名字不能证明网关词表，未知目的地不得宣称获得精确计数。</summary>
    internal static bool Supports(AiModelConfigRecord model) => model.ProviderKey == AiProviderKeys.OpenAiCompatible
        && model.ModelId is "text-embedding-3-small" or "text-embedding-3-large"
        && AiKnowledgeModelChunker.OfficialDestination(model.EndpointBaseUrl);

    /// <summary>在预算/批次身份创建前校验原始输入；不裁剪、重写或改变持久请求指纹。</summary>
    /// <returns>支持范围内的输入拒绝原因；空值不代表未知网关已验证词表。</returns>
    internal static string? Validate(AiModelConfigRecord model, IReadOnlyList<string> inputs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(inputs);
        if (!Supports(model)) return null;
        var bounded = AiEmbeddingContentPolicy.ValidateInputs(inputs); if (bounded is not null) return bounded;
        // 现有 16 条/32000 字符门禁限制同步计数成本；每条 8191 也使整批低于官方 300000 Token 上限。
        foreach (var input in inputs)
        {
            token.ThrowIfCancellationRequested();
            try { _ = StrictUtf8.GetByteCount(input); }
            catch (EncoderFallbackException) { return "Embedding input must contain valid Unicode text."; }
            var count = AiKnowledgeCl100kTokenizer.Get(token).CountTokens(input, considerNormalization: false);
            token.ThrowIfCancellationRequested();
            if (count is < 1 or > MaxInputTokens) return "Embedding input exceeds the supported token boundary.";
        }
        return null;
    }
}
