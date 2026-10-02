using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.AiRetrieval.Probe;

internal sealed record ProbeChunk(string ChunkId, string SourceVersion, string TenantScope, string Text);
internal sealed record ProbeCase(string CaseId, string Question, string TenantScope, string[] Allowed, string[] Expected);
internal sealed record ProbeObservation(string Scenario, bool Passed, string Detail);
internal sealed record ProbeReport(string Provider, string Runtime, string ServerVersion,
    string DatasetSha256, string VectorModel, string EmbeddingStatus, double RecallAt5,
    int ChunkCount, int Dimension, ProbeObservation[] Observations);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ProbeReport))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;

/// <summary>只读取 R02 冻结语料；检索实验不生成答案，不能关闭端到端质量门禁。</summary>
internal static class ProbeCorpus
{
    internal const string Model = "synthetic-featurehash-v1";
    internal const int Dimension = 128;
    internal const string Digest = "3380bf8d0b8352df48093b1c0dee7d4216b0eeae30733cc37f04cd65e778ec6b";

    internal static (ProbeChunk[] Chunks, ProbeCase[] Cases) Read(string file)
    {
        var text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))) != Digest)
            throw new InvalidDataException("必须使用 R02 冻结语料。");
        using var json = JsonDocument.Parse(text);
        var chunks = json.RootElement.GetProperty("chunks").EnumerateArray().Select(x => new ProbeChunk(
            x.GetProperty("chunkId").GetString()!, x.GetProperty("sourceVersion").GetString()!,
            x.GetProperty("tenantScope").GetString()!, x.GetProperty("text").GetString()!)).ToArray();
        var cases = json.RootElement.GetProperty("cases").EnumerateArray().Select(x => new ProbeCase(
            x.GetProperty("caseId").GetString()!, x.GetProperty("question").GetString()!,
            x.GetProperty("tenantScope").GetString()!,
            x.GetProperty("allowedSourceVersions").EnumerateArray().Select(v => v.GetString()!).ToArray(),
            x.GetProperty("expectedChunkIds").EnumerateArray().Select(v => v.GetString()!).ToArray())).ToArray();
        return (chunks, cases);
    }

    internal static async Task<double> CheckRetrievalAsync(ProbeChunk[] chunks, ProbeCase[] cases,
        Func<ProbeCase, Task<string[]>> query, List<ProbeObservation> observations)
    {
        var recalls = new List<double>();
        foreach (var testCase in cases)
        {
            var hits = await query(testCase);
            var authorized = hits.All(id => chunks.Any(x => x.ChunkId == id
                && x.TenantScope == testCase.TenantScope && testCase.Allowed.Contains(x.SourceVersion, StringComparer.Ordinal)));
            observations.Add(new($"filter:{testCase.CaseId}", authorized, $"authorized hits={hits.Length}"));
            if (testCase.Expected.Length > 0)
                recalls.Add((double)testCase.Expected.Count(id => hits.Contains(id, StringComparer.Ordinal)) / testCase.Expected.Length);
        }
        return recalls.Average();
    }

    internal static async Task CheckBroadRankingAsync(ProbeChunk[] chunks, ProbeCase[] cases,
        Func<ProbeCase, Task<string[]>> query, List<ProbeObservation> observations)
    {
        var number = cases.Single(x => x.CaseId == "exact-number");
        // 独立排序探针扩大合成允许集，不修改 R02 样例或将撤权来源作为正式授权。
        var scope = chunks.Where(x => x.TenantScope == number.TenantScope).ToArray();
        if (scope.Length <= 5) throw new InvalidDataException("排序探针必须含超过 K 的同范围候选。");
        var broad = number with { Allowed = scope.Select(x => x.SourceVersion).Distinct(StringComparer.Ordinal).ToArray() };
        var expected = ProbeVectorSearch.Rank(ProbeVectorSearch.Embed(number.Question),
            scope.Select(x => new ProbeVector(x.ChunkId, Model, 1, ProbeVectorSearch.Embed(x.Text))).ToArray(), Model, 1, 4096, 5);
        var hits = await query(broad);
        observations.Add(new("broad-number-ranking", hits.Length == 5 && hits[0] == expected[0] && hits[0] == "ticket-1",
            $"same-scope candidates={scope.Length}; top1 matches managed exact cosine and synthetic identifier"));
    }
}
