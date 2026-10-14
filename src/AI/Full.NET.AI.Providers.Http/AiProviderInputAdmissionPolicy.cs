using Full.NET.AI.Abstractions.Models;
using Microsoft.Extensions.Configuration;

namespace Full.NET.AI.Providers.Http;

/// <summary>冻结宿主输入计量要求，显式坏值不得默认为估算模式。</summary>
internal static class AiProviderInputAdmissionPolicy
{
    internal static AiChatInputAdmissionPolicy Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("FullNet:Ai:InputAdmission");
        if (section.Value is not null) throw Invalid();
        var children = section.GetChildren().ToArray();
        if (children.Length == 0) return AiChatInputAdmissionPolicy.Default;
        if (children.Length != 1 || children[0].Key != "RequireExactChatTokens" || children[0].GetChildren().Any()
            || !bool.TryParse(children[0].Value, out var required)) throw Invalid();
        return new(required);
    }
    private static InvalidOperationException Invalid() => new("AI input admission requires a single exact boolean setting.");
}
