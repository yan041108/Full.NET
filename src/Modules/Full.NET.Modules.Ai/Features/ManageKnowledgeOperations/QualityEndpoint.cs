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

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeOperations;

/// <summary>财务和质量聚合使用独立精确权限，拒绝客户端scope及任意正文。</summary>
internal static class QualityEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        Common(endpoints.MapGet("/api/v1/ai/knowledge-finance",async(Guid? knowledgeBaseId,DateTimeOffset? fromUtc,DateTimeOffset? toUtc,
            AiKnowledgeQualityService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.FinanceAsync(Actor(http),knowledgeBaseId,fromUtc,toUtc,token).ConfigureAwait(false),http)),
            "aiGetKnowledgeFinance",[AiKnowledgeFinancePermissions.Read],"knowledgeBaseId","fromUtc","toUtc").Produces<AiKnowledgeFinanceReport>();
        Common(endpoints.MapGet("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}/quality-observations",async(Guid knowledgeBaseId,DateTimeOffset? fromUtc,DateTimeOffset? toUtc,
            AiKnowledgeQualityService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.ObservationsAsync(knowledgeBaseId,Actor(http),fromUtc,toUtc,token).ConfigureAwait(false),http)),
            "aiGetKnowledgeQualityObservations",[AiKnowledgeOperationsPermissions.Read,AiKnowledgePermissions.Read,AiKnowledgeAnswerFeedbackReviewPermissions.Read],"fromUtc","toUtc").Produces<AiKnowledgeQualityObservation>();
    }
    private static RouteHandlerBuilder Common(RouteHandlerBuilder endpoint,string name,string[] permissions,params string[] queries)=>endpoint.WithName(name).WithTags("AiKnowledgeOperations")
        .RequireAuthorization(permissions.Select(FullNetPermissionPolicies.For).ToArray()).AddEndpointFilter(async(context,next)=>
        {
            var http=context.HttpContext;http.Response.Headers.CacheControl="no-store";http.Response.Headers["X-Content-Type-Options"]="nosniff";
            var mapper=http.RequestServices.GetRequiredService<IApiResultMapper>();
            if(Actor(http)==Guid.Empty)return mapper.Map(Result<bool>.Failure(new("authorization.permission_denied","An interactive original session is required.",ErrorType.Forbidden)),http);
            if((http.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody??http.Request.ContentLength>0)||http.Request.Query.Any(pair=>pair.Value.Count!=1||!queries.Contains(pair.Key,StringComparer.Ordinal)))
                return mapper.Map(Result<bool>.Failure(new(AiKnowledgeErrorCodes.InputInvalid,"The quality or finance filter is invalid.",ErrorType.Validation)),http);
            return await next(context).ConfigureAwait(false);
        }).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
    private static Guid Actor(HttpContext http)=>AiKnowledgeIndexHttpBinding.TryCreate(http,http.RequestServices.GetRequiredService<ICurrentTenant>().Id,out var session)?session.UserId:Guid.Empty;
}
