using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Ai.Features.ManageAnswerFeedbackReview;

/// <summary>交互评审者操作同可信范围的标签，不授予其他人的会话读取权。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        const string path = "/api/v1/ai/knowledge-answer-feedback";
        endpoints.MapPost(path + "/{feedbackId:guid}/evaluation-candidate", async (Guid feedbackId, PrepareAiFeedbackEvaluationRequest request, AiFeedbackEvaluationService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.PrepareAsync(feedbackId, actor, request, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiKnowledgeAnswerFeedback").WithName("aiPrepareFeedbackEvaluationCandidate").Produces<AiFeedbackEvaluationCandidate>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).RequireAuthorization(FullNetPermissionPolicies.For(AiFeedbackEvaluationPermissions.Prepare));
        endpoints.MapPost(path + "/evaluation-admissions", async (AdmitAiFeedbackEvaluationRequest request, AiFeedbackEvaluationService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.AdmitAsync(actor, request, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiKnowledgeAnswerFeedback").WithName("aiAdmitFeedbackEvaluationCandidate").Produces<AiFeedbackEvaluationAdmission>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).RequireAuthorization(FullNetPermissionPolicies.For(AiFeedbackEvaluationPermissions.Admit));
        endpoints.MapGet(path + "/reviews", async (string? after, AiKnowledgeAnswerFeedbackReviewService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.GetAsync(actor, after, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiKnowledgeAnswerFeedback").WithName("aiGetKnowledgeAnswerFeedbackReviews").Produces<AiKnowledgeAnswerFeedbackReviewPage>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeAnswerFeedbackReviewPermissions.Read));
        endpoints.MapPut(path + "/reviews/{feedbackId:guid}", async (Guid feedbackId, ReviewAiKnowledgeAnswerFeedbackRequest request, AiKnowledgeAnswerFeedbackReviewService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.ReviewAsync(feedbackId, actor, request, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiKnowledgeAnswerFeedback").WithName("aiReviewKnowledgeAnswerFeedback").Produces<AiKnowledgeAnswerFeedbackReviewItem>(200)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeAnswerFeedbackReviewPermissions.Review));
        endpoints.MapGet(path + "/export", async (AiKnowledgeAnswerFeedbackReviewService service, IApiResultMapper mapper, HttpContext http, CancellationToken token) =>
            Actor(http) is { } actor ? mapper.Map(await service.ExportAsync(actor, token).ConfigureAwait(false), http) : Results.Unauthorized())
            .WithTags("AiKnowledgeAnswerFeedback").WithName("aiExportKnowledgeAnswerFeedbackLabels").Produces<AiKnowledgeAnswerFeedbackExportResponse>(200)
            .ProducesProblem(401).ProducesProblem(403).RequireAuthorization(FullNetPermissionPolicies.For(AiKnowledgeAnswerFeedbackReviewPermissions.Export));
    }
    private static Guid? Actor(HttpContext http) => http.User.Identity?.AuthenticationType is not ("FullNET.ApiKey" or "FullNET.Signature")
        && Guid.TryParse(http.User.FindFirst("sub")?.Value, out var actor) && actor != Guid.Empty ? actor : null;
}
