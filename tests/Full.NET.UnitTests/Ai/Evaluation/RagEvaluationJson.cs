using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.UnitTests.Ai.Evaluation;

/// <summary>严格读取离线证据；数据摘要只规范化换行，跨平台不能改变冻结语料。</summary>
internal static class RagEvaluationJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        WriteIndented = true
    };

    internal static T Read<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        RejectDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("评估证据不能为空。");
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"评估证据包含重复字段：{property.Name}");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
        }
    }

    internal static string Digest(string json)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json.Replace("\r\n", "\n", StringComparison.Ordinal))));

    internal static (RagEvaluationCorpus Corpus, string Digest) LoadCorpus()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ai", "Evaluation", "Fixtures", "rag-cases.json"));
        return (Read<RagEvaluationCorpus>(json), Digest(json));
    }

    internal static RagEvaluationRun LoadReference()
        => Read<RagEvaluationRun>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Ai", "Evaluation", "Fixtures", "reference-results.json")));
}
