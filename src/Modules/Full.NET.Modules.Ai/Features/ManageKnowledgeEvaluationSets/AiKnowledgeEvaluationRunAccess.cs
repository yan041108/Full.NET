using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeEvaluationSets;

/// <summary>实验仍使用现有集合、库ACL、完整检索来源和模型批准，不创建另一套空间或授权缓存。</summary>
internal sealed class AiKnowledgeEvaluationRunAccess(IQueryExecutor query,IDataTransactionState transactions,ICurrentTenant tenant,
    IOptions<DatabaseOptions> database,ICurrentSessionAuthorization sessions,AiKnowledgeReadAccess access,
    AiKnowledgeEvaluationService sets,AiKnowledgeRetrievalService retrieval,AiKnowledgeFaqPublishedSourceReader faqs)
{
    internal sealed record Context(Guid Actor,Guid SessionId,Guid? TenantId,bool Scoped,AiKnowledgeBaseRecord Knowledge,
        AiKnowledgeEvaluationWorkspace Set,AiModelConfigRecord Embedding,AiModelConfigRecord? Processing,AiKnowledgeRetrievalSnapshot Sources,
        AiKnowledgeEvaluationService.RunProof SetProof,bool RequireOwner);

    internal async Task<Context> CaptureAsync(Guid knowledge,Guid actor,string permission,CaptureAiKnowledgeEvaluationStrategy request,CancellationToken token)
    {
        if(request is null||string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>160||request.ExpectedSources is not {Count:>0 and <=8}||!AiKnowledgeEvaluationRunPolicy.Mode(request.Baseline,request.Candidate))throw Invalid();
        return await ProveAsync(knowledge,actor,permission,request.SetId,request.SetRevisionId,request.SetVersion,request.SetApprovalId,
            request.EmbeddingModelConfigId,request.EmbeddingModelVersion,request.ProcessingModelConfigId,request.ProcessingModelVersion,
            request.Candidate,request.DocumentIds,request.ExpectedSources,true,null,token).ConfigureAwait(false);
    }

    internal Task<Context> ProveAsync(Guid actor,string permission,AiKnowledgeEvaluationStrategyDefinition definition,bool owned,CancellationToken token)
    {
        AiKnowledgeEvaluationRunPolicy.Validate(definition);
        return ProveAsync(definition.KnowledgeBaseId,actor,permission,definition.SetId,definition.SetRevisionId,definition.SetVersion,definition.SetApprovalId,
            definition.EmbeddingModel.Id,definition.EmbeddingModel.Version,definition.ProcessingModel?.Id,definition.ProcessingModel?.Version,
            definition.Candidate,definition.DocumentIds,definition.Sources,owned,definition,token);
    }

    private async Task<Context> ProveAsync(Guid knowledge,Guid actor,string permission,Guid setId,Guid revision,int setVersion,Guid approval,
        Guid embedding,int embeddingVersion,Guid? processing,int? processingVersion,string candidate,IReadOnlyList<Guid> documents,
        IReadOnlyList<AiKnowledgeSearchSource> sources,bool owned,AiKnowledgeEvaluationStrategyDefinition? expected,CancellationToken token)
    {
        if(transactions.HasTransaction)throw new InvalidOperationException("运行权威核证不能加入本地事务。");
        var scope=tenant.Id;var scoped=AiKnowledgeScope.IsTenant(tenant);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(90));token=deadline.Token;
        var required=new List<string>{AiKnowledgePermissions.Read,AiKnowledgeEvaluationPermissions.Read,AiKnowledgeEvaluationRunPermissions.Read,permission,
            AiKnowledgeSearchPermissions.Read,AiKnowledgeDocumentPermissions.Read,
            AiKnowledgeDocumentPermissions.SourceRead,AiKnowledgeDocumentPermissions.IndexRead};
        var dispatch=permission==AiKnowledgeEvaluationRunPermissions.Execute;
        if(dispatch)required.Add(AiKnowledgeSearchPermissions.Compare);
        if(dispatch&&candidate=="rewrite_rerank")required.Add(AiKnowledgeSearchPermissions.Refine);
        var original=await sessions.AuthorizeAllAsync(required,token).ConfigureAwait(false);Check();
        if(original is null||original.UserId!=actor||original.TenantId!=scope||original.SessionId==Guid.Empty)throw Denied();
        var p=await access.ParametersAsync(actor,token).ConfigureAwait(false);p["Id"]=knowledge;Check();
        var root=await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider,scoped),p,token).ConfigureAwait(false);Check();
        if(root is not {IsEnabled:true}||owned&&root.OwnerUserId!=actor)throw Unavailable();
        var proof=await sets.ProveRunAsync(knowledge,setId,actor,original.SessionId,permission,token).ConfigureAwait(false);Check();
        if(proof is null)throw Unavailable();var set=proof.Workspace;
        if((int)proof.Parameters["KnowledgeVersion"]! !=root.Version||(string)proof.Parameters["Classification"]! !=root.DataClassification)throw Unavailable();
        if(set.Revision.Id!=revision||set.Version!=setVersion||set.ApprovalState!="approved"||set.ApprovalId!=approval
            ||set.Revision.State!="frozen"||expected is not null&&(set.Revision.ContentHash!=expected.SetContentHash||set.Revision.SourceHash!=expected.SetSourceHash))throw Unavailable();
        foreach(var sample in set.Revision.Cases)_=AiKnowledgeKeywordRanker.Terms(sample.Sample.Question,token);
        var model=await ModelAsync(embedding,embeddingVersion).ConfigureAwait(false);
        if(root.EmbeddingModelConfigId!=embedding||root.EmbeddingModelVersion!=embeddingVersion)throw Unavailable();
        AiModelConfigRecord? processingModel=null;
        if(candidate=="rewrite_rerank")
        {
            if(processing is not {} id||processingVersion is not {} version)throw Invalid();
            processingModel=await ModelAsync(id,version).ConfigureAwait(false);
            if(root.GenerationModelConfigId!=id||root.GenerationModelVersion!=version)throw Unavailable();
        }
        else if(processing.HasValue||processingVersion.HasValue)throw Invalid();
        if(expected is not null&&(expected.EmbeddingModel!=Model(model,"embedding")
            ||expected.ProcessingModel!=(processingModel is null?null:Model(processingModel,"processing"))))throw Unavailable();
        var corpus=sources.Select(source=>source.KnowledgeBaseId).Distinct().ToArray();
        var selection=AiKnowledgeSearchAnswerSelection.Freeze(new(documents,embeddingVersion,sources),corpus,token)??throw Invalid();
        // 非执行入口只读候选；传入生成模型会被既有检索器视为聊天派发并隐含要求Send。
        // 准确处理模型及每个来源的批准仍由下面CurrentAsync联合SQL核证，实际执行再走重排派发权限。
        AiKnowledgeRetrievalSnapshot current;
        try
        {
            current=await retrieval.PreflightAsync(corpus,actor,model,token,dispatch?processingModel:null,selection,
                searchDiagnostics:false,searchComparison:dispatch,searchRefinement:dispatch&&candidate=="rewrite_rerank").ConfigureAwait(false);
        }
        // 输入已在Freeze完成校验；此处的无效载荷表示当前来源失效，不能误报客户端输入错误。
        catch(Exception error)when(error is InvalidDataException or AiKnowledgeSearchSourceChangedException or IOException){throw Unavailable();}
        Check();
        var context=new Context(actor,original.SessionId,scope,scoped,root,set,model,processingModel,current,proof,owned);
        await CurrentAsync(context,permission,token).ConfigureAwait(false);Check();
        return context;

        async Task<AiModelConfigRecord> ModelAsync(Guid id,int version)
        {
            if(id==Guid.Empty||version<1)throw Invalid();
            var found=await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(scoped?AiModelConfigSql.FindAvailableForTenantChat:AiModelConfigSql.FindAvailableForHostChat,
                new Dictionary<string,object?>{["ModelConfigId"]=id},token).ConfigureAwait(false);Check();
            return found is {IsEnabled:true}&&found.Version==version?found:throw Unavailable();
        }
        void Check(){token.ThrowIfCancellationRequested();if(transactions.HasTransaction||tenant.Id!=scope||AiKnowledgeScope.IsTenant(tenant)!=scoped)throw Denied();}
    }

    /// <summary>本次已验证载荷的身份仍须逐次核对，最后联合SQL同时绑定集合、反馈/父叶、候选和模型目的地。</summary>
    internal async Task<Dictionary<string,object?>> CurrentAsync(Context context,string permission,CancellationToken token)
    {
        if(transactions.HasTransaction||tenant.Id!=context.TenantId||AiKnowledgeScope.IsTenant(tenant)!=context.Scoped)throw Denied();
        var p=new Dictionary<string,object?>(context.SetProof.Parameters);
        var required=new List<string>{AiKnowledgePermissions.Read,AiKnowledgeEvaluationPermissions.Read,AiKnowledgeEvaluationRunPermissions.Read,
            permission,AiKnowledgeSearchPermissions.Read,AiKnowledgeDocumentPermissions.Read,
            AiKnowledgeDocumentPermissions.SourceRead,AiKnowledgeDocumentPermissions.IndexRead};
        required.AddRange(context.SetProof.RequiredPermissions);
        if(permission==AiKnowledgeEvaluationRunPermissions.Execute)
        {
            required.Add(AiKnowledgeSearchPermissions.Compare);
            if(context.Processing is not null)required.Add(AiKnowledgeSearchPermissions.Refine);
        }
        if(context.Sources.FaqSources.Count>0)required.Add(AiKnowledgeFaqPermissions.Read);
        if(context.Sources.FaqSources.Values.Any(value=>value.RequiresFeedbackPermission))required.Add(AiKnowledgeFaqPermissions.Feedback);
        var parentJson=await faqs.CurrentParentsJsonAsync(context.Sources.FaqSources.Values,context.Actor,token).ConfigureAwait(false)??throw Unavailable();
        var bindings=JsonSerializer.Deserialize((string)p["FaqParentBindingsJson"]!,AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray)!
            .Concat(JsonSerializer.Deserialize(parentJson,AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray)!).ToArray();
        p["FaqParentBindingsJson"]=JsonSerializer.Serialize(bindings,AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqParentAuthorizationArray);
        p["FaqFeedbackReadAllowed"]=required.Contains(AiKnowledgeFaqPermissions.Feedback)?1:0;
        // 累计评估来源的窄用途与真正私人FAQ候选分别授权，不能互相冒用。
        p["EvaluationFeedbackReadAllowed"]=1;
        p["SetApprovalId"]=context.Set.ApprovalId;
        p["RequireOwner"]=context.RequireOwner?1:0;
        p["KnowledgeBaseIdsJson"]=JsonSerializer.Serialize(context.Sources.KnowledgeBaseIds.ToArray(),AiJsonSerializerContext.Default.GuidArray);
        // 策略冻结具体文档，后续新增文档不扩大原输入；整组候选与准确发布仍逐条比较。
        p["DocumentIdsJson"]=JsonSerializer.Serialize(context.Sources.Sources.Select(row=>row.DocumentId).ToArray(),AiJsonSerializerContext.Default.GuidArray);
        p["AllDocuments"]=0;
        var model=context.Embedding;var generation=context.Processing;
        p["ModelConfigId"]=model.Id;p["ModelConfigVersion"]=model.Version;p["ExpectedModelTenantId"]=model.TenantId;
        p["ExpectedEndpointBaseUrl"]=model.EndpointBaseUrl;p["ExpectedProviderKey"]=model.ProviderKey;p["ExpectedModelId"]=model.ModelId;
        p["ExpectedOrganizationId"]=model.OrganizationId;p["ExpectedApiKeyProtected"]=model.ApiKeyProtected;
        p["GenerationModelConfigId"]=generation?.Id;p["GenerationModelConfigVersion"]=generation?.Version;p["GenerationTenantId"]=generation?.TenantId;
        p["GenerationEndpointBaseUrl"]=generation?.EndpointBaseUrl;p["GenerationProviderKey"]=generation?.ProviderKey;p["GenerationModelId"]=generation?.ModelId;
        p["GenerationOrganizationId"]=generation?.OrganizationId;p["GenerationApiKeyProtected"]=generation?.ApiKeyProtected;
        var relations=await access.ParametersAsync(context.Actor,token).ConfigureAwait(false);foreach(var pair in relations)p[pair.Key]=pair.Value;
        var session=await sessions.AuthorizeAllAsync(required.Distinct(StringComparer.Ordinal).ToArray(),token).ConfigureAwait(false);
        if(session?.UserId!=context.Actor||session.TenantId!=context.TenantId||session.SessionId!=context.SessionId)throw Denied();
        if(transactions.HasTransaction||tenant.Id!=context.TenantId||AiKnowledgeScope.IsTenant(tenant)!=context.Scoped)throw Denied();
        var current=await query.QueryAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeEvaluationRunSql.For(database.Value.Provider,context.Scoped,"current"),p,token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if(current.Count!=context.Sources.Sources.Count||context.Sources.Sources.Any(row=>current.Count(value=>value==row)!=1))throw Unavailable();
        return p;
    }

    internal static AiKnowledgeEvaluationModel Model(AiModelConfigRecord model,string purpose)=>new(model.Id,model.Version,purpose,
        AiKnowledgeDocumentModelChunker.Destination(model).ToLowerInvariant());
    private static AiKnowledgeEvaluationRunAccessException Invalid()=>new("ai.knowledge_evaluation_runs.input_invalid",ErrorType.Validation);
    private static AiKnowledgeEvaluationRunAccessException Denied()=>new("authorization.permission_denied",ErrorType.Forbidden);
    private static AiKnowledgeEvaluationRunAccessException Unavailable()=>new("ai.knowledge_evaluation_runs.source_unavailable",ErrorType.Conflict);
}

/// <summary>只携带安全机器码，不交付模型、来源或原始异常细节。</summary>
internal sealed class AiKnowledgeEvaluationRunAccessException(string code,ErrorType type):Exception
{
    internal string Code {get;}=code;
    internal ErrorType Type {get;}=type;
}
