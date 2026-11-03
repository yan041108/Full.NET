using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>策略只保存准确批准输入和不含凭据的模型绑定，运行请求不能覆盖问题或提示词。</summary>
internal sealed record AiKnowledgeEvaluationStrategyDefinition(int Version, Guid KnowledgeBaseId, string Name,
    string Baseline, string Candidate, Guid SetId, Guid SetRevisionId, int SetVersion, Guid SetApprovalId,
    string SetContentHash, string SetSourceHash, AiKnowledgeEvaluationModel EmbeddingModel,
    AiKnowledgeEvaluationModel? ProcessingModel, IReadOnlyList<Guid> DocumentIds,
    IReadOnlyList<AiKnowledgeSearchSource> Sources, string PromptHash, string MeasurementProtocol);

/// <summary>所有模型阶段身份先于领取和调用持久化，未使用阶段不能伪造账本费用。</summary>
internal sealed record AiKnowledgeEvaluationStage(Guid Id, string Stage);
internal sealed record AiKnowledgeEvaluationPlannedCase(Guid Id, Guid SampleId, int Sequence,
    AiFeedbackEvaluationSample Sample, IReadOnlyList<AiKnowledgeEvaluationStage> Operations);
internal sealed record AiKnowledgeEvaluationRunPlan(int Version, Guid StrategyId, Guid StrategyRevisionId,
    string DefinitionHash, IReadOnlyList<AiKnowledgeEvaluationPlannedCase> Cases);
internal sealed record AiKnowledgeEvaluationObservedResult(int Version, AiKnowledgeSearchComparisonRanking? Baseline,
    AiKnowledgeSearchComparisonRanking? Candidate, bool? RefinementRefused, string MeasurementProtocol)
{
    /// <summary>模型实际采用的有界改写问题，原人工问题仍保留在原计划。</summary>
    public string? EffectiveQuery {get;init;}
}

/// <summary>冻结策略、原问题和预算身份的闭合协议；失败和未运行不从统计分母移除。</summary>
internal static class AiKnowledgeEvaluationRunPolicy
{
    internal const string MeasurementProtocol = "retrieval_pair_observed_v1";
    internal const int DefinitionBudget = 256 * 1024;
    internal const int PlanBudget = 1024 * 1024;
    internal const int ResultBudget = 4 * 1024 * 1024;
    internal const int HitLimit = 5;
    internal static string PromptHash => AiKnowledgeEvaluationPolicy.Hash(AiKnowledgeSearchRefinementProtocol.RewriteInstruction
        + "\n" + AiKnowledgeSearchRefinementProtocol.RerankInstruction);
    internal static bool Mode(string baseline, string candidate) => (baseline, candidate) is
        ("keyword", "vector") or ("keyword", "rewrite_rerank") or ("vector", "rewrite_rerank");
    internal static int CallsPerCase(string candidate) => candidate == "vector" ? 1 : candidate == "rewrite_rerank" ? 5 : throw Invalid();
    internal static IReadOnlyList<string> Stages(string candidate) => candidate == "vector" ? ["comparison_embedding"]
        : candidate == "rewrite_rerank" ? ["comparison_embedding", "refinement_original_embedding", "refinement_rewrite",
            "refinement_rewritten_embedding", "refinement_rerank"] : throw Invalid();

    internal static string Encode(AiKnowledgeEvaluationStrategyDefinition value)
    {
        Validate(value); return Write(value, AiKnowledgeEvaluationRunJsonContext.Default.AiKnowledgeEvaluationStrategyDefinition, DefinitionBudget);
    }
    internal static AiKnowledgeEvaluationStrategyDefinition Definition(string json)
    {
        var value=Read(json,AiKnowledgeEvaluationRunJsonContext.Default.AiKnowledgeEvaluationStrategyDefinition,DefinitionBudget);
        Validate(value);return value;
    }
    internal static string Encode(AiKnowledgeEvaluationRunPlan value)
    {
        Validate(value);return Write(value,AiKnowledgeEvaluationRunJsonContext.Default.AiKnowledgeEvaluationRunPlan,PlanBudget);
    }
    internal static AiKnowledgeEvaluationRunPlan Plan(string json)
    {
        var value=Read(json,AiKnowledgeEvaluationRunJsonContext.Default.AiKnowledgeEvaluationRunPlan,PlanBudget);Validate(value);return value;
    }
    internal static string Encode(AiKnowledgeEvaluationObservedResult value)
    {
        if(value.Version!=1||value.MeasurementProtocol!=MeasurementProtocol)throw Invalid();
        return Write(value,AiKnowledgeEvaluationRunJsonContext.Default.AiKnowledgeEvaluationObservedResult,ResultBudget);
    }
    internal static AiKnowledgeEvaluationObservedResult Result(string json)
    {
        var value=Read(json,AiKnowledgeEvaluationRunJsonContext.Default.AiKnowledgeEvaluationObservedResult,ResultBudget);
        _=Encode(value);return value;
    }
    internal static void Validate(AiKnowledgeEvaluationStrategyDefinition value)
    {
        if(value.Version!=1||!Mode(value.Baseline,value.Candidate)||value.SetVersion<1||value.MeasurementProtocol!=MeasurementProtocol
            ||value.Name is not {Length:>0 and <=160}||string.IsNullOrWhiteSpace(value.Name)||!Hash(value.SetContentHash)
            ||!Hash(value.SetSourceHash)||!Hash(value.PromptHash)||value.EmbeddingModel is null||value.DocumentIds is null||value.Sources is null)throw Invalid();
        foreach(var id in new[]{value.KnowledgeBaseId,value.SetId,value.SetRevisionId,value.SetApprovalId})Key(id);
        Model(value.EmbeddingModel,"embedding");
        if(value.Candidate=="rewrite_rerank"){if(value.ProcessingModel is null)throw Invalid();Model(value.ProcessingModel,"processing");}
        else if(value.ProcessingModel is not null)throw Invalid();
        _=AiKnowledgeSearchAnswerSelection.Freeze(new(value.DocumentIds,value.EmbeddingModel.Version,value.Sources),value.Sources.Select(source=>source.KnowledgeBaseId).Distinct().ToArray(),default)
            ??throw Invalid();
    }
    internal static void Validate(AiKnowledgeEvaluationRunPlan value)
    {
        if(value.Version!=1||!Hash(value.DefinitionHash)||value.Cases is not {Count:>0 and <=8})throw Invalid();
        Key(value.StrategyId);Key(value.StrategyRevisionId);var ids=new HashSet<Guid>();var samples=new HashSet<Guid>();
        for(var index=0;index<value.Cases.Count;index++)
        {
            var item=value.Cases[index];Key(item.Id);Key(item.SampleId);
            if(item.Sequence!=index||!ids.Add(item.Id)||!samples.Add(item.SampleId)||!AiFeedbackEvaluationPolicy.Valid(item.Sample)
                ||item.Operations is not {Count:1 or 5})throw Invalid();
            var expected=Stages(item.Operations.Count==1?"vector":"rewrite_rerank");
            if(!item.Operations.Select(stage=>stage.Stage).SequenceEqual(expected,StringComparer.Ordinal))throw Invalid();
            foreach(var stage in item.Operations){Key(stage.Id);if(!ids.Add(stage.Id))throw Invalid();}
        }
    }
    internal static (int Measured,double? Mean,double? P95) Duration(IEnumerable<AiKnowledgeEvaluationCaseState> cases)
    {
        var values=cases.Where(item=>item.DurationMs.HasValue).Select(item=>item.DurationMs!.Value).Order().ToArray();
        if(values.Any(value=>!double.IsFinite(value)||value<0||value>TimeSpan.FromDays(1).TotalMilliseconds))throw Invalid();
        return values.Length==0?(0,null,null):(values.Length,values.Average(),values[(int)Math.Ceiling(values.Length*.95)-1]);
    }
    private static string Write<T>(T value,System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type,int budget)
    {
        var json=JsonSerializer.Serialize(value,type);if(Encoding.UTF8.GetByteCount(json)>budget)throw Invalid();return json;
    }
    private static T Read<T>(string json,System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type,int budget) where T:class
    {
        if(json is null||Encoding.UTF8.GetByteCount(json)>budget)throw Invalid();
        using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=64});Unique(document.RootElement);
        return JsonSerializer.Deserialize(json,type)??throw Invalid();
    }
    private static void Unique(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object){var names=new HashSet<string>(StringComparer.Ordinal);foreach(var p in value.EnumerateObject()){if(!names.Add(p.Name))throw Invalid();Unique(p.Value);}}
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())Unique(item);
    }
    private static void Model(AiKnowledgeEvaluationModel value,string purpose)
    {Key(value.Id);if(value.Version<1||value.Purpose!=purpose||!Hash(value.DestinationHash))throw Invalid();}
    private static bool Hash(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Key(Guid value){if(value==Guid.Empty||value.Version!=7)throw Invalid();}
    private static InvalidDataException Invalid()=>new("评估策略、原输入或原操作计划无效。");
}
