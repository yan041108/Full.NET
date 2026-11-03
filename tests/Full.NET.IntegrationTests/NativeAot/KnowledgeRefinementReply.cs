using System.Text.Json;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>受控模型的静态协议响应；HTTPS夹具不依赖进程内模型工厂或其数据库准备帮助类。</summary>
internal static class KnowledgeRefinementReply
{
    internal static string Create(JsonElement input, string? fault)
    {
        // FAQ 关联场景同时消费已验收的改写协议与有引用的缓冲回答，仍由服务器选择来源身份。
        if (!input.TryGetProperty("task", out _))
        {
            if(fault=="answer_refusal")return JsonSerializer.Serialize(new{answer="",citationIds=Array.Empty<Guid>(),refusal=true});
            return JsonSerializer.Serialize(new { answer = "受控 FAQ 完整回答", citationIds = new[] { input.GetProperty("sources")[0].GetProperty("citationId").GetGuid() }, refusal = false });
        }
        if (fault == "malformed") return "{\"query\":\"伪造\",\"refusal\":false,\"sources\":[]}";
        if (input.GetProperty("task").GetString() == "knowledge_query_rewrite_v1")
            return JsonSerializer.Serialize(new { query = fault == "rewrite" ? "" : "补写 明确", refusal = fault == "rewrite" });
        var candidates = input.GetProperty("candidates"); Assert.IsTrue(candidates.GetArrayLength() is > 0 and <= 8);
        Assert.IsTrue(candidates.EnumerateArray().All(row => row.EnumerateObject().Count() == 2));
        return JsonSerializer.Serialize(new { order = fault == "rerank" ? [] : candidates.EnumerateArray().Reverse().Select(row => row.GetProperty("id").GetString()).ToArray(), refusal = fault == "rerank" });
    }
}
