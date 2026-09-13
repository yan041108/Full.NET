using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>委托创建、代行审批与撤销；不存储访问令牌。</summary>
internal static class AiAgentDelegationApiAssertions
{
    private const string DefinitionKey = "fullnet-single-text-v1";

    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");

        string[] requesterPermissions =
        [
            AiModelPermissions.Read,
            AiModelPermissions.Create,
            AiChatPermissions.Read,
            AiChatPermissions.Create,
            AiChatPermissions.Update,
            AiAgentRunPermissions.Read,
            AiAgentRunPermissions.Create,
            AiAgentRunPermissions.Resume,
            AiAgentApprovalPermissions.Read,
            AiAgentApprovalPermissions.Request,
            AiAgentApprovalPermissions.Delegate,
            AiAgentToolPermissions.CatalogRead,
        ];
        string[] delegatePermissions = [AiAgentApprovalPermissions.Read];

        var requester = await factory.CreateHostIdentityAsync($"delegation-requester-{Guid.NewGuid():N}", requesterPermissions);
        var delegateUser = await factory.CreateHostIdentityAsync($"delegation-grantee-{Guid.NewGuid():N}", delegatePermissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requester.AccessToken);

        using var modelResponse = await client.PostAsJsonAsync("/api/v1/ai/model-configs", new CreateAiModelConfigRequest(
            null, "委托模型", "ollama", "https://provider.test", "model", null, null, false, true));
        Assert.IsTrue(modelResponse.IsSuccessStatusCode, await modelResponse.Content.ReadAsStringAsync());
        var model = (await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>())!;

        using var sessionResponse = await client.PostAsJsonAsync("/api/v1/ai/chat/sessions", new CreateAiChatSessionRequest(model.Id, "委托前标题"));
        Assert.IsTrue(sessionResponse.IsSuccessStatusCode, await sessionResponse.Content.ReadAsStringAsync());
        var session = (await sessionResponse.Content.ReadFromJsonAsync<AiChatSessionResponse>())!;

        await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory).ConfigureAwait(false);
        using var runResponse = await client.PostAsJsonAsync("/api/v1/ai/agent/runs", new CreateAiAgentRunRequest(
            Guid.CreateVersion7(), DefinitionKey, model.Id, "delegated approval", 100, 100));
        Assert.AreEqual(HttpStatusCode.Accepted, runResponse.StatusCode);
        var run = (await runResponse.Content.ReadFromJsonAsync<CreateAiAgentRunResponse>())!;

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        using var createDelegation = await client.PostAsJsonAsync("/api/v1/ai/agent/delegations", new CreateAiAgentDelegationRequest(
            delegateUser.UserId,
            "ai.chat.sessions.rename",
            AiAgentApprovalPermissions.Decide,
            expiresAt));
        Assert.IsTrue(createDelegation.IsSuccessStatusCode, await createDelegation.Content.ReadAsStringAsync());
        var delegation = await createDelegation.Content.ReadFromJsonAsync<CreateAiAgentDelegationResponse>();

        using var listDelegations = await client.GetAsync("/api/v1/ai/agent/delegations");
        Assert.IsTrue(listDelegations.IsSuccessStatusCode, await listDelegations.Content.ReadAsStringAsync());
        var listed = await listDelegations.Content.ReadFromJsonAsync<IReadOnlyList<AiAgentDelegationResponse>>();
        Assert.IsTrue(listed!.Any(item => item.Id == delegation!.DelegationId));

        var operationId = Guid.CreateVersion7();
        var argumentsJson = JsonSerializer.Serialize(new { sessionId = session.Id, title = "委托审批标题" });
        using var createdApproval = await client.PostAsJsonAsync("/api/v1/ai/agent/approvals", new CreateAiAgentApprovalRequest(
            run.RunId,
            operationId,
            "ai.chat.sessions.rename",
            1,
            argumentsJson));
        Assert.IsTrue(createdApproval.IsSuccessStatusCode, await createdApproval.Content.ReadAsStringAsync());
        var approval = await createdApproval.Content.ReadFromJsonAsync<CreateAiAgentApprovalResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegateUser.AccessToken);
        using var readApproval = await client.GetAsync($"/api/v1/ai/agent/approvals/{approval!.ApprovalId}");
        Assert.IsTrue(readApproval.IsSuccessStatusCode, await readApproval.Content.ReadAsStringAsync());
        var pending = await readApproval.Content.ReadFromJsonAsync<AiAgentApprovalResponse>();
        using var decide = await client.PostAsJsonAsync(
            $"/api/v1/ai/agent/approvals/{approval.ApprovalId}/decide",
            new DecideAiAgentApprovalRequest(true, pending!.Version));
        Assert.IsTrue(decide.IsSuccessStatusCode, await decide.Content.ReadAsStringAsync());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requester.AccessToken);
        using var revoke = await client.PostAsJsonAsync(
            $"/api/v1/ai/agent/delegations/{delegation!.DelegationId}/revoke",
            new RevokeAiAgentDelegationRequest(1));
        Assert.IsTrue(revoke.IsSuccessStatusCode, await revoke.Content.ReadAsStringAsync());
        var revoked = await revoke.Content.ReadFromJsonAsync<AiAgentDelegationResponse>();
        Assert.IsNotNull(revoked!.RevokedAtUtc);

        var operationId2 = Guid.CreateVersion7();
        using var createdApproval2 = await client.PostAsJsonAsync("/api/v1/ai/agent/approvals", new CreateAiAgentApprovalRequest(
            run.RunId,
            operationId2,
            "ai.chat.sessions.rename",
            1,
            argumentsJson));
        var approval2 = await createdApproval2.Content.ReadFromJsonAsync<CreateAiAgentApprovalResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", delegateUser.AccessToken);
        using var readApproval2 = await client.GetAsync($"/api/v1/ai/agent/approvals/{approval2!.ApprovalId}");
        var pending2 = await readApproval2.Content.ReadFromJsonAsync<AiAgentApprovalResponse>();
        using var decideAfterRevoke = await client.PostAsJsonAsync(
            $"/api/v1/ai/agent/approvals/{approval2.ApprovalId}/decide",
            new DecideAiAgentApprovalRequest(true, pending2!.Version));
        await AiAgentApprovalApiAssertions.AssertProblemAsync(
            decideAfterRevoke,
            HttpStatusCode.Forbidden,
            AiErrorCodes.AgentApprovalNotAuthorized);
    }
}
