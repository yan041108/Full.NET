using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实宿主与双库验证精确权限、所有权、输入和持久生成槽位的 HTTP 契约。</summary>
internal static class AiChatApiAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        var identity = await factory.CreateHostIdentityAsync($"ai-{Guid.NewGuid():N}",
            [AiModelPermissions.Read, AiModelPermissions.Create, AiChatPermissions.Read, AiChatPermissions.Create, AiChatPermissions.Send]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.AccessToken);
        await AssertProblemAsync(client, Guid.NewGuid(), " ", HttpStatusCode.UnprocessableEntity, AiErrorCodes.ChatMessageInvalid);
        await AssertProblemAsync(client, Guid.NewGuid(), "hello", HttpStatusCode.NotFound, AiErrorCodes.ChatSessionNotFound);
        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "测试模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;
        using var sessionResponse = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "测试会话"));
        Assert.IsTrue(sessionResponse.IsSuccessStatusCode, await sessionResponse.Content.ReadAsStringAsync());
        var session = (await sessionResponse.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;

        // 模拟另一实例持有尚未过期的生成槽位；请求必须由真实数据库条件拒绝。
        await using DbConnection connection = factory.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(factory.ConnectionString)
            : new MySqlConnection(new MySqlConnectionStringBuilder(factory.ConnectionString) { GuidFormat = MySqlGuidFormat.Binary16 }.ConnectionString);
        Assert.AreEqual(1, await connection.ExecuteAsync("""
            UPDATE fn_ai_chat_session
            SET IsGenerating = 1, GenerationId = @GenerationId, GenerationExpiresAtUtc = @ExpiresAtUtc
            WHERE Id = @SessionId AND OwnerUserId = @OwnerUserId AND TenantId IS NULL
            """, new { GenerationId = Guid.CreateVersion7(), ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2), SessionId = session.Id, OwnerUserId = identity.UserId }));
        await AssertProblemAsync(client, session.Id, "hello", HttpStatusCode.Conflict, AiErrorCodes.ChatGenerationInProgress);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await factory.CreateHostAccessTokenAsync([AiChatPermissions.Read, AiChatPermissions.Send]));
        await AssertProblemAsync(client, session.Id, "hello", HttpStatusCode.NotFound, AiErrorCodes.ChatSessionNotFound);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await factory.CreateHostAccessTokenAsync([AiChatPermissions.Read]));
        await AssertProblemAsync(client, session.Id, "hello", HttpStatusCode.Forbidden, "authorization.permission_denied");
    }

    private static async Task AssertProblemAsync(HttpClient client, Guid sessionId, string content, HttpStatusCode status, string code)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ai/chat/sessions/{sessionId}/messages/stream")
        { Content = JsonContent.Create(new StreamAiChatMessageRequest(content)) };
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request);
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
        Assert.AreEqual((int)status, problem.RootElement.GetProperty("status").GetInt32());
    }
}
