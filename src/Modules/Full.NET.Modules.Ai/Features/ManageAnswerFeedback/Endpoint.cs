using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageAnswerFeedback;

/// <summary>只允许交互用户操作本人实际知识答案反馈；服务端独立复核范围和当前资格。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/ai/chat/sessions/{sessionId:guid}/messages/{messageId:guid}/feedback";
        endpoints.MapGet(path + "/state", async (Guid sessionId, Guid messageId, AiKnowledgeAnswerFeedbackService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.GetStateAsync(sessionId, messageId, actor, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiKnowledgeAnswerFeedback").WithName("aiGetKnowledgeAnswerFeedbackState").Produces<AiKnowledgeAnswerFeedbackStateResponse>(200)
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeAnswerFeedbackPermissions.Withdraw));
        endpoints.MapGet(path, async (Guid sessionId, Guid messageId, AiKnowledgeAnswerFeedbackService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.GetAsync(sessionId, messageId, actor, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiKnowledgeAnswerFeedback").WithName("aiGetKnowledgeAnswerFeedback").Produces<AiKnowledgeAnswerFeedbackResponse>(200)
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeAnswerFeedbackPermissions.Read));
        endpoints.MapPut(path, async (Guid sessionId, Guid messageId, SetAiKnowledgeAnswerFeedbackRequest request, AiKnowledgeAnswerFeedbackService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.SetAsync(sessionId, messageId, actor, request, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiKnowledgeAnswerFeedback").WithName("aiSetKnowledgeAnswerFeedback").Produces<AiKnowledgeAnswerFeedbackResponse>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeAnswerFeedbackPermissions.Submit));
        endpoints.MapDelete(path, async (Guid sessionId, Guid messageId, int version, AiKnowledgeAnswerFeedbackService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.WithdrawAsync(sessionId, messageId, actor, version, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiKnowledgeAnswerFeedback").WithName("aiWithdrawKnowledgeAnswerFeedback").Produces<AiKnowledgeAnswerFeedbackResponse>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeAnswerFeedbackPermissions.Withdraw));
    }
    private static Guid? Actor(HttpContext http) => http.User.Identity?.AuthenticationType is not ("FullNET.ApiKey" or "FullNET.Signature")
        && Guid.TryParse(http.User.FindFirst("sub")?.Value, out var actor) && actor != Guid.Empty ? actor : null;
}
