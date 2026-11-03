using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeEvaluationSets;

/// <summary>评估修订只接受可信原交互会话；公开接口不接受来源 JSON、租户或模型调用开关。</summary>
internal static class Endpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/evaluation-sets").WithTags("AiKnowledgeEvaluationSets");
        Common(group.MapPost("/feedback-candidates", async (Guid knowledgeBaseId, PrepareAiKnowledgeEvaluationFeedback request, AiKnowledgeEvaluationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.PrepareFeedbackAsync(knowledgeBaseId, Actor(context), request, token).ConfigureAwait(false), context)),
            "aiPrepareKnowledgeEvaluationFeedback", AiKnowledgeEvaluationPermissions.Feedback).Produces<AiKnowledgeEvaluationPreparedFeedback>();
        Common(group.MapGet("", async (Guid knowledgeBaseId, Guid? afterId, AiKnowledgeEvaluationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.ListAsync(knowledgeBaseId, afterId, Actor(context), token).ConfigureAwait(false), context)),
            "aiListKnowledgeEvaluationSets", AiKnowledgeEvaluationPermissions.Read, "afterId").Produces<AiKnowledgeEvaluationPage>();
        Common(group.MapGet("/{setId:guid}", async (Guid knowledgeBaseId, Guid setId, Guid? revisionId, AiKnowledgeEvaluationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.GetAsync(knowledgeBaseId, setId, revisionId, Actor(context), token).ConfigureAwait(false), context)),
            "aiGetKnowledgeEvaluationSet", AiKnowledgeEvaluationPermissions.Read, "revisionId").Produces<AiKnowledgeEvaluationWorkspace>();
        Common(group.MapPost("", async (Guid knowledgeBaseId, SaveAiKnowledgeEvaluationSet request, AiKnowledgeEvaluationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.SaveAsync(knowledgeBaseId, null, Actor(context), request, token).ConfigureAwait(false), context)),
            "aiCreateKnowledgeEvaluationSet", AiKnowledgeEvaluationPermissions.Create).Produces<AiKnowledgeEvaluationMutation>();
        Common(group.MapPut("/{setId:guid}", async (Guid knowledgeBaseId, Guid setId, SaveAiKnowledgeEvaluationSet request, AiKnowledgeEvaluationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.SaveAsync(knowledgeBaseId, setId, Actor(context), request, token).ConfigureAwait(false), context)),
            "aiAppendKnowledgeEvaluationSet", AiKnowledgeEvaluationPermissions.Append).Produces<AiKnowledgeEvaluationMutation>();
        Common(group.MapPost("/{setId:guid}/restore", async (Guid knowledgeBaseId, Guid setId, RestoreAiKnowledgeEvaluationSet request, AiKnowledgeEvaluationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.RestoreAsync(knowledgeBaseId, setId, Actor(context), request, token).ConfigureAwait(false), context)),
            "aiRestoreKnowledgeEvaluationSet", AiKnowledgeEvaluationPermissions.Restore).Produces<AiKnowledgeEvaluationMutation>();
        Common(group.MapPost("/{setId:guid}/copy", async (Guid knowledgeBaseId, Guid setId, CopyAiKnowledgeEvaluationSet request, AiKnowledgeEvaluationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) =>
            mapper.Map(await service.CopyAsync(knowledgeBaseId, setId, Actor(context), request, token).ConfigureAwait(false), context)),
            "aiCopyKnowledgeEvaluationSet", AiKnowledgeEvaluationPermissions.Copy).Produces<AiKnowledgeEvaluationMutation>();
        Common(group.MapPost("/{setId:guid}/approvals", async (Guid knowledgeBaseId, Guid setId, ReviewAiKnowledgeEvaluationSet request, AiKnowledgeEvaluationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => request.Decision == "approved"
            ? mapper.Map(await service.ReviewAsync(knowledgeBaseId, setId, Actor(context), request, token).ConfigureAwait(false), context) : Invalid(mapper, context)),
            "aiApproveKnowledgeEvaluationSet", AiKnowledgeEvaluationPermissions.Approve).Produces<AiKnowledgeEvaluationMutation>();
        Common(group.MapPost("/{setId:guid}/withdrawals", async (Guid knowledgeBaseId, Guid setId, ReviewAiKnowledgeEvaluationSet request, AiKnowledgeEvaluationService service,
            IApiResultMapper mapper, HttpContext context, CancellationToken token) => request.Decision == "withdrawn"
            ? mapper.Map(await service.ReviewAsync(knowledgeBaseId, setId, Actor(context), request, token).ConfigureAwait(false), context) : Invalid(mapper, context)),
            "aiWithdrawKnowledgeEvaluationSet", AiKnowledgeEvaluationPermissions.Withdraw).Produces<AiKnowledgeEvaluationMutation>();
    }

    internal static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint, string name, string permission, params string[] query) =>
        endpoint.WithName(name).RequireAuthorization(new[] { AiKnowledgePermissions.Read, AiKnowledgeEvaluationPermissions.Read, permission }.Distinct().Select(FullNetPermissionPolicies.For).ToArray())
            .AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                var mapper = http.RequestServices.GetRequiredService<IApiResultMapper>();
                if (!AiKnowledgeIndexHttpBinding.TryCreate(http, http.RequestServices.GetRequiredService<ICurrentTenant>().Id, out _))
                    return mapper.Map(Result<AiKnowledgeEvaluationWorkspace>.Failure(new("authorization.permission_denied", "An interactive original session is required.", ErrorType.Forbidden)), http);
                if (http.Request.Query.Any(pair => pair.Value.Count != 1 || !query.Contains(pair.Key, StringComparer.Ordinal))
                    || HttpMethods.IsGet(http.Request.Method) && (http.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody ?? http.Request.ContentLength>0)) return Invalid(mapper, http);
                return await next(context).ConfigureAwait(false);
            }).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

    private static Guid Actor(HttpContext context) => AiKnowledgeIndexHttpBinding.TryCreate(context,
        context.RequestServices.GetRequiredService<ICurrentTenant>().Id, out var original) ? original.UserId : Guid.Empty;
    private static IResult Invalid(IApiResultMapper mapper, HttpContext context) => mapper.Map(Result<AiKnowledgeEvaluationWorkspace>.Failure(
        new("ai.knowledge_evaluation_sets.input_invalid", "The evaluation set input is invalid.", ErrorType.Validation)), context);
}
