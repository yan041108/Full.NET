using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Data.Dapper;

namespace Full.NET.UnitTests.Ai;

/// <summary>冻结身份、未知语义和晚到操作边界；纯规则不冒充实际模型运行。</summary>
[TestClass]
public sealed class AiKnowledgeEvaluationRunTests
{
    [TestMethod]
    public void Plan_rejects_duplicate_or_reordered_stage_identity_and_preserves_question()
    {
        var plan=Plan();var json=AiKnowledgeEvaluationRunPolicy.Encode(plan);var saved=AiKnowledgeEvaluationRunPolicy.Plan(json);
        Assert.AreEqual(plan.Cases[0].Sample.Question,saved.Cases[0].Sample.Question);
        var item=plan.Cases[0];var stages=item.Operations.ToArray();stages[1]=stages[1] with{Id=stages[0].Id};
        Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeEvaluationRunPolicy.Encode(plan with{Cases=[item with{Operations=stages}]}));
        Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeEvaluationRunPolicy.Encode(plan with{Cases=[item with{Operations=item.Operations.Reverse().ToArray()}]}));
        Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeEvaluationRunPolicy.Plan(json.Replace("\"version\":1","\"version\":1,\"version\":1",StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Missing_measurement_is_null_and_failure_measurements_remain_in_nearest_rank_distribution()
    {
        var cases=new[]{State("not_run",null),State("failed",100),State("succeeded",10),State("unknown",1000),State("skipped",null)};
        var result=AiKnowledgeEvaluationRunPolicy.Duration(cases);Assert.AreEqual(3,result.Measured);Assert.AreEqual(370,result.Mean);Assert.AreEqual(1000,result.P95);
        var empty=AiKnowledgeEvaluationRunPolicy.Duration([State("not_run",null),State("skipped",null)]);Assert.AreEqual(0,empty.Measured);Assert.IsNull(empty.Mean);Assert.IsNull(empty.P95);
    }

    [TestMethod]
    public async Task Original_identity_cannot_be_reused_or_dispatched_from_a_late_child_flow()
    {
        var scope=new AiKnowledgeEvaluationOperationScope(new Host());var actor=Guid.CreateVersion7();var session=Guid.CreateVersion7();
        var source=new AiKnowledgeRetrievalSnapshot(actor,null,false,new AiModelConfigRecord(),[],[],[]);
        var id=Guid.CreateVersion7();var checks=0;var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease=scope.Enter(actor,session,null,false,new Dictionary<string,Guid>{{"comparison_embedding",id}},_=>{checks++;return Task.CompletedTask;});
        Assert.AreEqual(id,scope.Next("comparison_embedding",source));
        Assert.ThrowsExactly<InvalidDataException>(()=>scope.Next("comparison_embedding",source));
        await scope.RevalidateAsync(source,default);Assert.AreEqual(1,checks);
        var child=Task.Run(async()=>{await release.Task;await scope.RevalidateAsync(source,default);});
        lease.Dispose();release.SetResult();await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(()=>child);Assert.AreEqual(1,checks);
    }

    [TestMethod]
    public void Final_sql_keeps_the_approved_set_full_sources_models_and_trusted_scope_in_one_query()
    {
        foreach(var mysql in new[]{false,true})foreach(var scoped in new[]{false,true})
        {
            var text=AiKnowledgeEvaluationRunSql.For(mysql?Full.NET.Data.Abstractions.DatabaseProvider.MySql:Full.NET.Data.Abstractions.DatabaseProvider.SqlServer,scoped,"current").Text;
            foreach(var required in new[]{"fn_ai_knowledge_evaluation_set","@SetApprovalId","@FootprintJson","@FaqParentBindingsJson","ExpectedApiKeyProtected","GenerationApiKeyProtected","@RequireOwner"})StringAssert.Contains(text,required);
            StringAssert.Contains(text,"@EvaluationFeedbackReadAllowed");StringAssert.Contains(text,"@FaqFeedbackReadAllowed");
            StringAssert.Contains(text,scoped?"d.TenantId=@TenantId":"d.TenantId IS NULL");
        }
    }
    [TestMethod]
    public void Dataset_owner_and_corpus_remain_distinct_exact_frozen_identities()
    {
        var knowledge=Guid.CreateVersion7();var corpus=Guid.CreateVersion7();var document=Guid.CreateVersion7();var source=new AiKnowledgeSearchSource(corpus,1,document,1,Guid.CreateVersion7(),Guid.CreateVersion7(),Guid.CreateVersion7(),new('a',64));
        var value=new AiKnowledgeEvaluationStrategyDefinition(1,knowledge,"跨库授权候选","keyword","vector",Guid.CreateVersion7(),Guid.CreateVersion7(),2,Guid.CreateVersion7(),new('b',64),new('c',64),new(Guid.CreateVersion7(),1,"embedding",new('d',64)),null,[document],[source],AiKnowledgeEvaluationRunPolicy.PromptHash,AiKnowledgeEvaluationRunPolicy.MeasurementProtocol);
        var restored=AiKnowledgeEvaluationRunPolicy.Definition(AiKnowledgeEvaluationRunPolicy.Encode(value));Assert.AreEqual(knowledge,restored.KnowledgeBaseId);Assert.AreEqual(corpus,restored.Sources.Single().KnowledgeBaseId);
    }
    [TestMethod]
    public void Every_run_statement_preserves_the_actual_data_scope_guard_for_both_providers()
    {
        string[] purposes=["strategy","strategy_head","strategy_replay","strategies","insert_strategy","append_strategy","advance_strategy","run","run_replay","runs","insert_run","cases","case","insert_case","insert_operation","claim","complete","close_unknown","touch_run","operations","review_replay","insert_review","review","strategy_entries","run_entries","current"];
        foreach(var mysql in new[]{false,true})foreach(var scoped in new[]{false,true})foreach(var purpose in purposes)
        {
            var statement=AiKnowledgeEvaluationRunSql.For(mysql?Full.NET.Data.Abstractions.DatabaseProvider.MySql:Full.NET.Data.Abstractions.DatabaseProvider.SqlServer,scoped,purpose);
            SqlScopeGuard.Validate(statement,new Scope(scoped?Guid.CreateVersion7():null));
        }
    }
    private static AiKnowledgeEvaluationRunPlan Plan()=>new(1,Guid.CreateVersion7(),Guid.CreateVersion7(),new('a',64),
        [new(Guid.CreateVersion7(),Guid.CreateVersion7(),0,new("zh-CN","原问题","参考答案","人工证据",false),AiKnowledgeEvaluationRunPolicy.Stages("rewrite_rerank").Select(stage=>new AiKnowledgeEvaluationStage(Guid.CreateVersion7(),stage)).ToArray())]);
    private static AiKnowledgeEvaluationCaseState State(string state,double? duration)=>new(Guid.CreateVersion7(),Guid.CreateVersion7(),0,1,state,null,duration,null,null,null,null);
    private sealed class Host:ICurrentTenant
    {public Guid? Id=>null;public string? Identifier=>null;public string? Name=>null;public bool IsAvailable=>false;public bool IsHost=>true;}
    private sealed class Scope(Guid? id):ICurrentTenant
    {public Guid? Id=>id;public string? Identifier=>id.HasValue?"scope":null;public string? Name=>Identifier;public bool IsAvailable=>true;public bool IsHost=>!id.HasValue;}
}
