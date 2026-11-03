using Full.NET.Hosting.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageKnowledgeIndexing;
using Full.NET.Abstractions.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeEvaluationSets;

/// <summary>实际运行使用原交互会话和显式领取；请求不能传入模型输出、任意提示词或操作身份。</summary>
internal static class RunEndpoint
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/v1/ai/knowledge-bases/{knowledgeBaseId:guid}").WithTags("AiKnowledgeEvaluationRuns");
        Endpoint.Common(group.MapGet("/evaluation-strategies",async(Guid knowledgeBaseId,Guid? afterId,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.EntriesAsync(knowledgeBaseId,afterId,Actor(http),false,token).ConfigureAwait(false),http)),"aiListKnowledgeEvaluationStrategies",AiKnowledgeEvaluationRunPermissions.Read,"afterId").Produces<AiKnowledgeEvaluationEntryPage>();
        Endpoint.Common(group.MapPut("/evaluation-strategies/{strategyId:guid}",async(Guid knowledgeBaseId,Guid strategyId,CaptureAiKnowledgeEvaluationStrategy request,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.SaveStrategyAsync(knowledgeBaseId,strategyId,Actor(http),request,token).ConfigureAwait(false),http)),"aiCaptureKnowledgeEvaluationStrategy",AiKnowledgeEvaluationRunPermissions.Plan).Produces<AiKnowledgeEvaluationStrategy>();
        Endpoint.Common(group.MapGet("/evaluation-strategies/{strategyId:guid}",async(Guid knowledgeBaseId,Guid strategyId,Guid? revisionId,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.GetStrategyAsync(knowledgeBaseId,strategyId,revisionId,Actor(http),token).ConfigureAwait(false),http)),"aiGetKnowledgeEvaluationStrategy",AiKnowledgeEvaluationRunPermissions.Read,"revisionId").Produces<AiKnowledgeEvaluationStrategy>();
        Endpoint.Common(group.MapGet("/evaluation-strategies/{strategyId:guid}/admission",async(Guid knowledgeBaseId,Guid strategyId,Guid revisionId,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.AdmissionAsync(knowledgeBaseId,strategyId,revisionId,Actor(http),token).ConfigureAwait(false),http)),"aiPreviewKnowledgeEvaluationRun",AiKnowledgeEvaluationRunPermissions.Create,"revisionId").Produces<AiKnowledgeEvaluationRunAdmission>();
        Endpoint.Common(group.MapPost("/evaluation-strategies/{strategyId:guid}/runs",async(Guid knowledgeBaseId,Guid strategyId,CreateAiKnowledgeEvaluationRun request,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.CreateRunAsync(knowledgeBaseId,strategyId,Actor(http),request,token).ConfigureAwait(false),http)),"aiCreateKnowledgeEvaluationRun",AiKnowledgeEvaluationRunPermissions.Create).Produces<AiKnowledgeEvaluationRun>();
        Endpoint.Common(group.MapGet("/evaluation-runs",async(Guid knowledgeBaseId,Guid? afterId,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.EntriesAsync(knowledgeBaseId,afterId,Actor(http),true,token).ConfigureAwait(false),http)),"aiListKnowledgeEvaluationRuns",AiKnowledgeEvaluationRunPermissions.Read,"afterId").Produces<AiKnowledgeEvaluationEntryPage>();
        Endpoint.Common(group.MapGet("/evaluation-runs/{runId:guid}",async(Guid knowledgeBaseId,Guid runId,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.GetRunAsync(knowledgeBaseId,runId,Actor(http),token).ConfigureAwait(false),http)),"aiGetKnowledgeEvaluationRun",AiKnowledgeEvaluationRunPermissions.Read).Produces<AiKnowledgeEvaluationRun>();
        Endpoint.Common(group.MapGet("/evaluation-runs/{runId:guid}/cases/{caseId:guid}",async(Guid knowledgeBaseId,Guid runId,Guid caseId,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.CaseAsync(knowledgeBaseId,runId,caseId,Actor(http),token).ConfigureAwait(false),http)),"aiGetKnowledgeEvaluationCase",AiKnowledgeEvaluationRunPermissions.Read).Produces<AiKnowledgeEvaluationCaseResult>();
        Endpoint.Common(group.MapPost("/evaluation-runs/{runId:guid}/cases/{caseId:guid}/execute",async(Guid knowledgeBaseId,Guid runId,Guid caseId,ExecuteAiKnowledgeEvaluationCase request,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.ActAsync(knowledgeBaseId,runId,caseId,Actor(http),request,"execute",token).ConfigureAwait(false),http)),"aiExecuteKnowledgeEvaluationCase",AiKnowledgeEvaluationRunPermissions.Execute).Produces<AiKnowledgeEvaluationCaseResult>();
        Endpoint.Common(group.MapPost("/evaluation-runs/{runId:guid}/cases/{caseId:guid}/skip",async(Guid knowledgeBaseId,Guid runId,Guid caseId,ExecuteAiKnowledgeEvaluationCase request,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.ActAsync(knowledgeBaseId,runId,caseId,Actor(http),request,"skip",token).ConfigureAwait(false),http)),"aiSkipKnowledgeEvaluationCase",AiKnowledgeEvaluationRunPermissions.Skip).Produces<AiKnowledgeEvaluationCaseResult>();
        Endpoint.Common(group.MapPost("/evaluation-runs/{runId:guid}/cases/{caseId:guid}/close-unknown",async(Guid knowledgeBaseId,Guid runId,Guid caseId,ExecuteAiKnowledgeEvaluationCase request,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.ActAsync(knowledgeBaseId,runId,caseId,Actor(http),request,"close_unknown",token).ConfigureAwait(false),http)),"aiCloseUnknownKnowledgeEvaluationCase",AiKnowledgeEvaluationRunPermissions.CloseUnknown).Produces<AiKnowledgeEvaluationCaseResult>();
        Endpoint.Common(group.MapPost("/evaluation-runs/{runId:guid}/cases/{caseId:guid}/reviews",async(Guid knowledgeBaseId,Guid runId,Guid caseId,ReviewAiKnowledgeEvaluationResult request,AiKnowledgeEvaluationRunService service,IApiResultMapper mapper,HttpContext http,CancellationToken token)=>
            mapper.Map(await service.ReviewAsync(knowledgeBaseId,runId,caseId,Actor(http),request,token).ConfigureAwait(false),http)),"aiReviewKnowledgeEvaluationCase",AiKnowledgeEvaluationRunPermissions.Review).Produces<AiKnowledgeEvaluationCaseResult>();
    }
    private static Guid Actor(HttpContext http)=>AiKnowledgeIndexHttpBinding.TryCreate(http,http.RequestServices.GetRequiredService<ICurrentTenant>().Id,out var session)?session.UserId:Guid.Empty;
}
