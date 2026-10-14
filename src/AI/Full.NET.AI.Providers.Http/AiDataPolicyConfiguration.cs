using Full.NET.AI.Abstractions.Models;
using Microsoft.Extensions.Configuration;

namespace Full.NET.AI.Providers.Http;

/// <summary>显式读取宿主配置，不通过反射绑定或请求字段生成策略。</summary>
internal static class AiDataPolicyConfiguration
{
    internal static AiDataProcessingPolicy Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("FullNet:Ai:DataPolicy");
        if (section.Value is not null) throw new InvalidOperationException("AI data policy must be an object.");
        var stages = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        IReadOnlyList<string>? privateClasses = null;
        foreach (var child in section.GetChildren())
        {
            if (child.Key == "PrivateClassifications") privateClasses = ReadList(child);
            else if (child.Key is "chat" or "embedding" or "rerank" or "context" or "telemetry" or "connectivity" or "mcp") stages.Add(child.Key, ReadList(child));
            else throw new InvalidOperationException("AI data policy contains an unsupported setting.");
        }
        return new(stages, privateClasses);
    }

    private static IReadOnlyList<string> ReadList(IConfigurationSection section)
    {
        if (section.Value is not null) throw new InvalidOperationException("AI data policy classifications must be an array.");
        var children = section.GetChildren().ToArray();
        if (children.Length > 3) throw new InvalidOperationException("AI data policy classification list is too large.");
        var result = new string[children.Length];
        for (var i = 0; i < children.Length; i++)
        {
            if (children[i].Key != i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || children[i].GetChildren().Any() || children[i].Value is not { } value)
                throw new InvalidOperationException("AI data policy requires a contiguous classification array.");
            result[i] = value;
        }
        return result;
    }
}
