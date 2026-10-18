using System.Net;
using System.Net.Http.Json;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Native HTTP 验证授权 DTO、原会话冻结和静态行读取；没有来源，因此不创建或执行收费任务。</summary>
internal static class NativeIndexGrantAssertions
{
    internal static async Task VerifyAsync(HttpClient client)
    {
        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(null, "原生授权模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.AreEqual(HttpStatusCode.Created, modelResponse.StatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        using var created = await client.PostAsJsonAsync("/api/v1/ai/knowledge-bases", new CreateAiKnowledgeBaseRequest("原生收费授权边界", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var knowledge = (await created.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var path = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
        using var approved = await client.PutAsJsonAsync(path + "/policy", new UpdateAiKnowledgePolicyRequest("internal", model.Id, model.Version, null, null, knowledge.Version));
        Assert.AreEqual(HttpStatusCode.OK, approved.StatusCode, await approved.Content.ReadAsStringAsync());
        knowledge = (await approved.Content.ReadFromJsonAsync<AiKnowledgeBaseResponse>())!;
        var request = new CreateAiKnowledgeIndexGrantRequest(Guid.CreateVersion7(), knowledge.Version, DateTimeOffset.UtcNow.AddMinutes(10), 2, 100, true);
        using var grantResponse = await client.PostAsJsonAsync(path + "/index-grants", request);
        Assert.AreEqual(HttpStatusCode.OK, grantResponse.StatusCode, await grantResponse.Content.ReadAsStringAsync());
        var grant = (await grantResponse.Content.ReadFromJsonAsync<AiKnowledgeIndexGrantResponse>())!;
        Assert.AreEqual(0, grant.AdmittedDocuments); Assert.AreEqual(100L, grant.InputTokenLimitPerDocument);
        using var list = await client.GetAsync(path + "/index-grants");
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode, await list.Content.ReadAsStringAsync());
        var items = (await list.Content.ReadFromJsonAsync<AiKnowledgeIndexGrantResponse[]>())!; Assert.HasCount(1, items); Assert.AreEqual(grant.Id, items[0].Id);
        using var stop = await client.PostAsJsonAsync(path + $"/index-grants/{grant.Id}/revoke", new RevokeAiKnowledgeIndexGrantRequest(grant.Version));
        Assert.AreEqual(HttpStatusCode.OK, stop.StatusCode, await stop.Content.ReadAsStringAsync());
        Assert.IsTrue((await stop.Content.ReadFromJsonAsync<AiKnowledgeIndexGrantResponse>())!.IsRevoked);
    }
}
