using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实双库与生产 DI 验证本人会话读取、固定参数和跨作用域操作防重。</summary>
internal static class AiToolExecutionAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        string[] permissions = [AiModelPermissions.Read, AiModelPermissions.Create, AiChatPermissions.Read,
            AiChatPermissions.Create, AiAgentToolPermissions.CatalogRead];
        var owner = await factory.CreateHostIdentityAsync($"tool-owner-{Guid.NewGuid():N}", permissions);
        var other = await factory.CreateHostIdentityAsync($"tool-other-{Guid.NewGuid():N}", permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        using var mineResponse = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "本人会话"));
        Assert.IsTrue(mineResponse.IsSuccessStatusCode, await mineResponse.Content.ReadAsStringAsync());
        var mine = (await mineResponse.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", other.AccessToken);
        using var otherResponse = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "其他会话"));
        Assert.IsTrue(otherResponse.IsSuccessStatusCode, await otherResponse.Content.ReadAsStringAsync());
        using var empty = JsonDocument.Parse("{}");
        var invocation = new ToolInvocation(Guid.CreateVersion7(), null, "ai.chat.sessions.list", 1, empty.RootElement);
        var result = await ExecuteAsync(factory, owner.AccessToken, invocation);
        Assert.AreEqual("succeeded", result.StatusKey);
        Assert.IsTrue(result.IsUntrusted);
        var items = result.Value!.Value.GetProperty("items").EnumerateArray().ToArray();
        Assert.AreEqual(1, items.Length);
        Assert.AreEqual(mine.Id, items[0].GetProperty("id").GetGuid());
        Assert.AreNotEqual("succeeded", (await ExecuteAsync(factory, owner.AccessToken, invocation)).StatusKey,
            "新执行器作用域也必须由持久化 OperationId 阻止重放。");
        using var forged = JsonDocument.Parse($"{{\"ownerUserId\":\"{other.UserId}\",\"tenantId\":\"{Guid.NewGuid()}\"}}");
        Assert.AreEqual("denied", (await ExecuteAsync(factory, owner.AccessToken,
            invocation with { OperationId = Guid.CreateVersion7(), Arguments = forged.RootElement })).StatusKey);
        Assert.AreEqual("succeeded", (await ExecuteAsync(factory, owner.AccessToken,
            invocation with { OperationId = Guid.CreateVersion7(), ToolName = "ai.tools.ping" })).StatusKey);
        var models = await ExecuteAsync(factory, owner.AccessToken,
            invocation with { OperationId = Guid.CreateVersion7(), ToolName = "ai.models.list" });
        Assert.AreEqual("succeeded", models.StatusKey);
        Assert.IsFalse(models.Value!.Value.GetRawText().Contains("endpoint", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(models.Value.Value.GetRawText().Contains("apiKey", StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<ToolExecutionResult> ExecuteAsync(FullNetApiFactory factory, string serverIssuedToken, ToolInvocation invocation)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        // 凭据由本夹具调用生产身份服务签发；这里验证执行 Port，HTTP JWT 中间件另有专门契约测试。
        var previous = http.HttpContext;
        http.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new JsonWebToken(serverIssuedToken).Claims, "server-issued-fixture")) };
        tenant.SetHost();
        try { return await scope.ServiceProvider.GetRequiredService<IAgentToolExecutor>().ExecuteAsync(invocation); }
        finally { tenant.Clear(); http.HttpContext = previous; }
    }
}
