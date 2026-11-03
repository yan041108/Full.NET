using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>策略、领取和原操作身份均写入本模块；所有 SQL 使用可信 scope，模型调用不加入事务。</summary>
internal static class AiKnowledgeEvaluationRunSql
{
    private const string StrategyColumns="s.Id,s.KnowledgeBaseId,s.TenantId,s.CreatedById,s.Name,s.HeadRevisionId,s.HeadRevisionNo,s.Version,r.Id AS RevisionId,r.RevisionNo,r.RequestKey,r.RequestHash,r.DefinitionJson,r.DefinitionHash,r.CreatedAtUtc";
    private const string RunColumns="run.Id,run.KnowledgeBaseId,run.TenantId,run.StrategyId,run.StrategyRevisionId,run.CreatedById,run.SessionId,run.RequestKey,run.RequestHash,run.PlanJson,run.PlanHash,run.Version,run.CreatedAtUtc";
    private const string CaseColumns="c.Id,c.RunId,c.SampleId,c.Sequence,c.Version,c.StateKey,c.ReasonCode,c.ExecutionToken,c.CloseRequestKey,c.CloseRequestHash,c.RequestKey,c.RequestHash,c.ResultJson,c.ResultHash,c.DurationMs,c.StartedAtUtc,c.FinishedAtUtc,c.ReviewId,review.BaselineSupports,review.CandidateSupports,c.CreatedAtUtc";
    private static readonly IReadOnlyDictionary<(bool Mysql,bool Scoped,string Purpose),SqlStatement> Statements=Build();
    internal static SqlStatement For(DatabaseProvider provider,bool scoped,string purpose)=>Statements[(provider==DatabaseProvider.MySql,scoped,purpose)];
    private static IReadOnlyDictionary<(bool,bool,string),SqlStatement> Build()
    {
        var map=new Dictionary<(bool,bool,string),SqlStatement>();
        foreach(var mysql in new[]{false,true})foreach(var scoped in new[]{false,true})
        {
            string Scope(string alias)=>scoped?alias+".TenantId=@TenantId":alias+".TenantId IS NULL";
            var root=" FROM fn_ai_knowledge_evaluation_strategy AS s INNER JOIN fn_ai_knowledge_evaluation_strategy_revision AS r ON r.StrategyId=s.Id WHERE s.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("s");
            var runs=" FROM fn_ai_knowledge_evaluation_run AS run WHERE run.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("run");
            var cases=" FROM fn_ai_knowledge_evaluation_case AS c INNER JOIN fn_ai_knowledge_evaluation_run AS run ON run.Id=c.RunId LEFT JOIN fn_ai_knowledge_evaluation_case_review AS review ON review.Id=c.ReviewId AND review.CaseId=c.Id AND review.RunId=run.Id WHERE run.Id=@RunId AND run.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("run");
            Add("strategy", "SELECT "+StrategyColumns+root+" AND s.Id=@StrategyId AND r.Id=@StrategyRevisionId");
            Add("strategy_head", "SELECT "+StrategyColumns+root+" AND s.Id=@StrategyId AND r.Id=s.HeadRevisionId");
            Add("strategy_replay", "SELECT "+StrategyColumns+root+" AND s.Id=@StrategyId AND r.RequestKey=@RequestKey");
            Add("strategies", "SELECT "+(mysql?"":"TOP (9) ")+StrategyColumns+root+" AND r.Id=s.HeadRevisionId AND (@AfterId IS NULL OR s.Id>@AfterId) ORDER BY s.Id"+(mysql?" LIMIT 9":""));
            Add("insert_strategy", "INSERT INTO fn_ai_knowledge_evaluation_strategy (Id,KnowledgeBaseId,TenantId,CreatedById,Name,HeadRevisionId,HeadRevisionNo,Version,CreatedAtUtc) VALUES (@StrategyId,@KnowledgeBaseId,"+(scoped?"@TenantId":"NULL")+",@Actor,@Name,@StrategyRevisionId,1,1,@Now)");
            Add("append_strategy", "INSERT INTO fn_ai_knowledge_evaluation_strategy_revision (Id,StrategyId,RevisionNo,RequestKey,RequestHash,DefinitionJson,DefinitionHash,CreatedById,CreatedAtUtc) SELECT @StrategyRevisionId,s.Id,@RevisionNo,@RequestKey,@RequestHash,@DefinitionJson,@DefinitionHash,@Actor,@Now FROM fn_ai_knowledge_evaluation_strategy AS s WHERE s.Id=@StrategyId AND s.KnowledgeBaseId=@KnowledgeBaseId AND s.HeadRevisionId=@StrategyRevisionId AND "+Scope("s"));
            Add("advance_strategy", "UPDATE fn_ai_knowledge_evaluation_strategy SET Name=@Name,HeadRevisionId=@StrategyRevisionId,HeadRevisionNo=HeadRevisionNo+1,Version=Version+1 WHERE Id=@StrategyId AND KnowledgeBaseId=@KnowledgeBaseId AND "+(scoped?"TenantId=@TenantId":"TenantId IS NULL")+" AND Version=@ExpectedStrategyVersion");
            Add("run", "SELECT "+RunColumns+runs+" AND run.Id=@RunId");
            Add("run_replay", "SELECT "+RunColumns+runs+" AND run.RequestKey=@RequestKey");
            Add("runs", "SELECT "+(mysql?"":"TOP (9) ")+RunColumns+runs+" AND (@AfterId IS NULL OR run.Id>@AfterId) ORDER BY run.Id"+(mysql?" LIMIT 9":""));
            Add("insert_run", "INSERT INTO fn_ai_knowledge_evaluation_run (Id,KnowledgeBaseId,TenantId,StrategyId,StrategyRevisionId,CreatedById,SessionId,RequestKey,RequestHash,PlanJson,PlanHash,Version,CreatedAtUtc) VALUES (@RunId,@KnowledgeBaseId,"+(scoped?"@TenantId":"NULL")+",@StrategyId,@StrategyRevisionId,@Actor,@SessionId,@RequestKey,@RequestHash,@PlanJson,@PlanHash,1,@Now)");
            Add("cases", "SELECT "+CaseColumns+cases+" ORDER BY c.Sequence");
            Add("case", "SELECT "+CaseColumns+cases+" AND c.Id=@CaseId");
            Add("insert_case", "INSERT INTO fn_ai_knowledge_evaluation_case (Id,RunId,SampleId,Sequence,Version,StateKey,CreatedAtUtc) SELECT @CaseId,run.Id,@SampleId,@Sequence,1,'not_run',@Now FROM fn_ai_knowledge_evaluation_run AS run WHERE run.Id=@RunId AND run.KnowledgeBaseId=@KnowledgeBaseId AND run.CreatedById=@Actor AND run.SessionId=@SessionId AND "+Scope("run"));
            Add("insert_operation", "INSERT INTO fn_ai_knowledge_evaluation_operation (Id,RunId,CaseId,KnowledgeBaseId,TenantId,StageKey,ModelConfigId,ModelConfigVersion,CreatedAtUtc) SELECT @OperationId,run.Id,c.Id,run.KnowledgeBaseId,run.TenantId,@StageKey,@ModelConfigId,@ModelConfigVersion,@Now FROM fn_ai_knowledge_evaluation_case AS c INNER JOIN fn_ai_knowledge_evaluation_run AS run ON run.Id=c.RunId WHERE c.Id=@CaseId AND run.Id=@RunId AND run.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("run"));
            // 原创建者及原会话只能领取一次；未知或运行中不回退 not_run。
            Add("claim", "UPDATE fn_ai_knowledge_evaluation_case SET StateKey=@StateKey,ReasonCode=@ReasonCode,ExecutionToken=@ExecutionToken,RequestKey=@RequestKey,RequestHash=@RequestHash,Version=Version+1,StartedAtUtc=@Now,FinishedAtUtc=@FinishedAtUtc WHERE Id=@CaseId AND RunId=@RunId AND Version=@ExpectedCaseVersion AND StateKey='not_run' AND EXISTS (SELECT 1 FROM fn_ai_knowledge_evaluation_run AS run WHERE run.Id=@RunId AND run.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("run")+" AND run.CreatedById=@Actor AND run.SessionId=@SessionId)");
            // 完成写入只接受原领取令牌。即便调用者已断开也只保存安全状态，不能再次派发。
            Add("complete", "UPDATE fn_ai_knowledge_evaluation_case SET StateKey=@StateKey,ReasonCode=@ReasonCode,ResultJson=@ResultJson,ResultHash=@ResultHash,DurationMs=@DurationMs,FinishedAtUtc=@Now,Version=Version+1 WHERE Id=@CaseId AND RunId=@RunId AND StateKey='running' AND ExecutionToken=@ExecutionToken AND EXISTS (SELECT 1 FROM fn_ai_knowledge_evaluation_run AS run WHERE run.Id=@RunId AND run.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("run")+")");
            Add("close_unknown", "UPDATE fn_ai_knowledge_evaluation_case SET StateKey='unknown',ReasonCode='operator_closed_unknown',CloseRequestKey=@RequestKey,CloseRequestHash=@RequestHash,FinishedAtUtc=@Now,Version=Version+1 WHERE Id=@CaseId AND RunId=@RunId AND Version=@ExpectedCaseVersion AND StateKey='running' AND EXISTS (SELECT 1 FROM fn_ai_knowledge_evaluation_run AS run WHERE run.Id=@RunId AND run.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("run")+")");
            Add("touch_run", "UPDATE fn_ai_knowledge_evaluation_run SET Version=Version+1 WHERE Id=@RunId AND KnowledgeBaseId=@KnowledgeBaseId AND "+(scoped?"TenantId=@TenantId":"TenantId IS NULL"));
            Add("operations", "SELECT operation.Id,operation.StageKey,operation.ModelConfigId,operation.ModelConfigVersion,ledger.Id AS LedgerId,ledger.UsageStatus,ledger.Outcome,ledger.QuotaMonthKey,ledger.PriceVersionId,ledger.Currency,ledger.ReservedCost,ledger.ChargedCost,ledger.SettledAtUtc FROM fn_ai_knowledge_evaluation_operation AS operation INNER JOIN fn_ai_knowledge_evaluation_run AS run ON run.Id=operation.RunId LEFT JOIN fn_ai_operation_budget AS ledger ON ledger.Id=operation.Id AND ledger.ModelConfigId=operation.ModelConfigId AND "+"ledger.ScopeKey=@ScopeKey"+" WHERE run.Id=@RunId AND operation.CaseId=@CaseId AND operation.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("run")+" AND "+Scope("operation")+" ORDER BY operation.CreatedAtUtc,operation.Id");
            Add("review_replay", "SELECT review.Id,review.RunId,review.CaseId,review.ResultHash,review.ReviewerUserId,review.BaselineSupports,review.CandidateSupports,review.RequestKey,review.RequestHash,review.CreatedAtUtc FROM fn_ai_knowledge_evaluation_case_review AS review INNER JOIN fn_ai_knowledge_evaluation_run AS run ON run.Id=review.RunId WHERE run.Id=@RunId AND run.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("run")+" AND review.CaseId=@CaseId AND review.RequestKey=@RequestKey");
            Add("insert_review", "INSERT INTO fn_ai_knowledge_evaluation_case_review (Id,RunId,CaseId,ResultHash,ReviewerUserId,BaselineSupports,CandidateSupports,RequestKey,RequestHash,CreatedAtUtc) SELECT @ReviewId,run.Id,c.Id,@ResultHash,@Actor,@BaselineSupports,@CandidateSupports,@RequestKey,@RequestHash,@Now FROM fn_ai_knowledge_evaluation_case AS c INNER JOIN fn_ai_knowledge_evaluation_run AS run ON run.Id=c.RunId WHERE c.Id=@CaseId AND run.Id=@RunId AND run.KnowledgeBaseId=@KnowledgeBaseId AND c.StateKey='succeeded' AND c.ResultHash=@ResultHash AND c.ReviewId=@ReviewId AND "+Scope("run"));
            Add("review", "UPDATE fn_ai_knowledge_evaluation_case SET ReviewId=@ReviewId,Version=Version+1 WHERE Id=@CaseId AND RunId=@RunId AND Version=@ExpectedCaseVersion AND ResultHash=@ResultHash AND StateKey='succeeded' AND EXISTS (SELECT 1 FROM fn_ai_knowledge_evaluation_run AS run WHERE run.Id=@RunId AND run.KnowledgeBaseId=@KnowledgeBaseId AND "+Scope("run")+")");
            foreach(var kind in new[]{"strategy","run"})
            {
                var alias=kind=="strategy"?"s":"run";
                Add(kind+"_entries","SELECT "+(mysql?"":"TOP (9) ")+alias+".Id,"+alias+(kind=="strategy"?".HeadRevisionId":".StrategyRevisionId")+" AS RevisionId,"+alias+".Version,"+alias+".CreatedAtUtc FROM fn_ai_knowledge_evaluation_"+kind+" AS "+alias
                    +" WHERE "+alias+".KnowledgeBaseId=@KnowledgeBaseId AND "+Scope(alias)+" AND (@AfterId IS NULL OR "+alias+".Id>@AfterId)"
                    +" AND EXISTS (SELECT 1 FROM fn_ai_knowledge_base AS k WHERE k.Id=@KnowledgeBaseId AND k.OwnerUserId=@Actor AND k.IsEnabled=1 AND "+Scope("k")+") ORDER BY "+alias+".Id"+(mysql?" LIMIT 9":""));
            }
            // 完整候选、模型目的地以及批准集合及全部保留来源在同一次最终查询核对。
            var current=AiKnowledgeEvaluationSql.CurrentFor(mysql?DatabaseProvider.MySql:DatabaseProvider.SqlServer,scoped).Text
                .Replace("@FaqFeedbackReadAllowed","@EvaluationFeedbackReadAllowed",StringComparison.Ordinal)
                +" AND d.ApprovalId=@SetApprovalId AND a.DecisionKey='approved' AND d.HeadRevisionId=r.Id AND (@RequireOwner=0 OR k.OwnerUserId=@Actor)"
                +" AND k.EmbeddingModelConfigId=@ModelConfigId AND k.EmbeddingModelVersion=@ModelConfigVersion"
                +" AND (@GenerationModelConfigId IS NULL OR (k.GenerationModelConfigId=@GenerationModelConfigId AND k.GenerationModelVersion=@GenerationModelConfigVersion))";
            Add("current",AiKnowledgeRetrievalSql.CombinedSourcesFor(mysql?DatabaseProvider.MySql:DatabaseProvider.SqlServer,scoped,true).Text
                .Replace(" ORDER BY DocumentId"," WHERE EXISTS ("+current+") ORDER BY DocumentId",StringComparison.Ordinal));
            void Add(string purpose,string sql)=>map.Add((mysql,scoped,purpose),new("ai.knowledge_evaluation_run."+purpose+(mysql?"_mysql_":"_sqlserver_")+(scoped?"tenant":"host"),sql,
                scoped?SqlDataScope.TenantRequired:SqlDataScope.HostOnly,scoped?SqlTenantBinding.CurrentTenantId:SqlTenantBinding.None));
        }
        return map;
    }
}
