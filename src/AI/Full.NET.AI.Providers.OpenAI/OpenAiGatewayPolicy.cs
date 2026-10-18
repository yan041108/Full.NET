using System.Collections.Frozen;
using Microsoft.Extensions.Configuration;

namespace Full.NET.AI.Providers.OpenAI;

/// <summary>宿主声明的兼容网关协议能力快照，不替代模型授权、网络政策或预算。</summary>
public sealed class OpenAiGatewayPolicy
{
    private readonly FrozenDictionary<(string Endpoint, string Model), bool> streamingUsage;
    private readonly FrozenSet<(string Endpoint, string Model)> responsesInputTokens;

    /// <summary>读取精确端点与模型能力；未知字段、重复声明及无效值必须在装配时拒绝。</summary>
    /// <param name="configuration">可信宿主配置；更改后需重启宿主。</param>
    public OpenAiGatewayPolicy(IConfiguration configuration)
    {
        var entries = new Dictionary<(string, string), bool>();
        var exact = new HashSet<(string, string)>();
        foreach (var section in configuration.GetSection("FullNet:Ai:OpenAiGateways").GetChildren())
        {
            if (section.GetChildren().Any(child => !new[] { "Endpoint", "ModelId", "SupportsStreamingUsage", "SupportsResponsesInputTokens" }
                    .Contains(child.Key, StringComparer.OrdinalIgnoreCase))
                || !Uri.TryCreate(section["Endpoint"], UriKind.Absolute, out var endpoint)
                || endpoint.Scheme != "https" || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0
                || endpoint.Fragment.Length != 0 || endpoint.Host.Contains('*')
                || string.IsNullOrWhiteSpace(section["ModelId"]) || section["ModelId"]!.Contains('*')
                || !bool.TryParse(section["SupportsStreamingUsage"], out var supported))
                throw new InvalidOperationException("AI gateway capability configuration is invalid.");

            var key = (Key(endpoint), section["ModelId"]!.Trim());
            if (!entries.TryAdd(key, supported))
                throw new InvalidOperationException("AI gateway capability configuration contains duplicate bindings.");
            var count = section.GetSection("SupportsResponsesInputTokens");
            if (section.GetChildren().Any(child => child.Key.Equals("SupportsResponsesInputTokens", StringComparison.OrdinalIgnoreCase)))
            {
                if (count.GetChildren().Any() || !bool.TryParse(count.Value, out var supportsCount))
                    throw new InvalidOperationException("AI exact input capability requires an explicit boolean.");
                if (supportsCount) exact.Add(key);
            }
        }
        streamingUsage = entries.ToFrozenDictionary();
        responsesInputTokens = exact.ToFrozenSet();
    }

    /// <summary>未知绑定采用最小文本协议，不猜测可选用量扩展；实际返回的 usage 仍可解析。</summary>
    internal bool SupportsStreamingUsage(Uri endpoint, string modelId) =>
        streamingUsage.GetValueOrDefault((Key(endpoint), modelId));

    /// <summary>只有显式批准的相同端点和模型才使用 Responses 计数与生成协议。</summary>
    internal bool SupportsResponsesInputTokens(Uri endpoint, string modelId) => responsesInputTokens.Contains((Key(endpoint), modelId));

    private static string Key(Uri endpoint) => endpoint.AbsoluteUri.TrimEnd('/');
}
