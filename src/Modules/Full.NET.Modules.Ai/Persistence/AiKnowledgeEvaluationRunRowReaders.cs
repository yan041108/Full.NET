#if FULLNET_AOT_COMPILE || FULLNET_AI_ROW_READER_TESTS
using System.Data.Common;
using System.Globalization;
using Full.NET.Modules.Ai.Contracts;
namespace Full.NET.Modules.Ai.Persistence;

/// <summary>评估实际运行的静态读取器；空费用、时间和人工判断不能变成默认零。</summary>
internal static partial class AiBudgetRowReaders
{
    internal static AiKnowledgeFinanceRow ReadKnowledgeFinance(DbDataReader reader)=>new()
    {
        KnowledgeBaseId=Null(reader,"KnowledgeBaseId")?null:reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")),Month=Text(reader,"Month"),
        PriceVersionId=Null(reader,"PriceVersionId")?null:reader.GetGuid(reader.GetOrdinal("PriceVersionId")),Currency=Null(reader,"Currency")?null:Text(reader,"Currency"),
        Requests=Integer(reader,"Requests"),Known=Integer(reader,"Known"),Unknown=Integer(reader,"Unknown"),Reserved=Integer(reader,"Reserved"),MissingCost=Integer(reader,"MissingCost"),
        RecordedCost=Null(reader,"RecordedCost")?null:Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("RecordedCost")),CultureInfo.InvariantCulture),
        ConservativeReservation=Null(reader,"ConservativeReservation")?null:Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("ConservativeReservation")),CultureInfo.InvariantCulture)
    };
    internal static AiKnowledgeQualityObservationRow ReadKnowledgeQualityObservation(DbDataReader reader)=>new()
    {
        KnowledgeBaseId=reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")),RecordedAnswers=Integer(reader,"RecordedAnswers"),Refused=Integer(reader,"Refused"),Measured=Integer(reader,"Measured"),
        MeanPipelineMs=Null(reader,"MeanPipelineMs")?null:Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("MeanPipelineMs")),CultureInfo.InvariantCulture),
        FirstRecordedAtUtc=Null(reader,"FirstRecordedAtUtc")?null:Timestamp(reader,"FirstRecordedAtUtc"),CurrentNegativeFeedback=Integer(reader,"CurrentNegativeFeedback")
    };
    internal static AiKnowledgeEvaluationEntry ReadEvaluationEntry(DbDataReader reader)=>new(reader.GetGuid(reader.GetOrdinal("Id")),
        reader.GetGuid(reader.GetOrdinal("RevisionId")),checked((int)Integer(reader,"Version")),Timestamp(reader,"CreatedAtUtc"));
    internal static AiKnowledgeEvaluationStrategyRow ReadEvaluationStrategy(DbDataReader reader)=>new()
    {
        Id=reader.GetGuid(reader.GetOrdinal("Id")),
        KnowledgeBaseId=reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")),
        TenantId=Null(reader,"TenantId")?null:reader.GetGuid(reader.GetOrdinal("TenantId")),
        CreatedById=reader.GetGuid(reader.GetOrdinal("CreatedById")),
        Name=Text(reader,"Name"),
        HeadRevisionId=reader.GetGuid(reader.GetOrdinal("HeadRevisionId")),
        HeadRevisionNo=checked((int)Integer(reader,"HeadRevisionNo")),
        Version=checked((int)Integer(reader,"Version")),
        RevisionId=reader.GetGuid(reader.GetOrdinal("RevisionId")),
        RevisionNo=checked((int)Integer(reader,"RevisionNo")),
        RequestKey=reader.GetGuid(reader.GetOrdinal("RequestKey")),
        RequestHash=Text(reader,"RequestHash"),
        DefinitionJson=Text(reader,"DefinitionJson"),
        DefinitionHash=Text(reader,"DefinitionHash"),
        CreatedAtUtc=Timestamp(reader,"CreatedAtUtc"),
    };
    internal static AiKnowledgeEvaluationRunRow ReadEvaluationRun(DbDataReader reader)=>new()
    {
        Id=reader.GetGuid(reader.GetOrdinal("Id")),
        KnowledgeBaseId=reader.GetGuid(reader.GetOrdinal("KnowledgeBaseId")),
        TenantId=Null(reader,"TenantId")?null:reader.GetGuid(reader.GetOrdinal("TenantId")),
        StrategyId=reader.GetGuid(reader.GetOrdinal("StrategyId")),
        StrategyRevisionId=reader.GetGuid(reader.GetOrdinal("StrategyRevisionId")),
        CreatedById=reader.GetGuid(reader.GetOrdinal("CreatedById")),
        SessionId=reader.GetGuid(reader.GetOrdinal("SessionId")),
        RequestKey=reader.GetGuid(reader.GetOrdinal("RequestKey")),
        RequestHash=Text(reader,"RequestHash"),
        PlanJson=Text(reader,"PlanJson"),
        PlanHash=Text(reader,"PlanHash"),
        Version=checked((int)Integer(reader,"Version")),
        CreatedAtUtc=Timestamp(reader,"CreatedAtUtc"),
    };
    internal static AiKnowledgeEvaluationCaseRow ReadEvaluationCase(DbDataReader reader)=>new()
    {
        Id=reader.GetGuid(reader.GetOrdinal("Id")),
        RunId=reader.GetGuid(reader.GetOrdinal("RunId")),
        SampleId=reader.GetGuid(reader.GetOrdinal("SampleId")),
        Sequence=checked((int)Integer(reader,"Sequence")),
        Version=checked((int)Integer(reader,"Version")),
        StateKey=Text(reader,"StateKey"),
        ReasonCode=Null(reader,"ReasonCode")?null:Text(reader,"ReasonCode"),
        ExecutionToken=Null(reader,"ExecutionToken")?null:reader.GetGuid(reader.GetOrdinal("ExecutionToken")),
        CloseRequestKey=Null(reader,"CloseRequestKey")?null:reader.GetGuid(reader.GetOrdinal("CloseRequestKey")),
        CloseRequestHash=Null(reader,"CloseRequestHash")?null:Text(reader,"CloseRequestHash"),
        RequestKey=Null(reader,"RequestKey")?null:reader.GetGuid(reader.GetOrdinal("RequestKey")),
        RequestHash=Null(reader,"RequestHash")?null:Text(reader,"RequestHash"),
        ResultJson=Null(reader,"ResultJson")?null:Text(reader,"ResultJson"),
        ResultHash=Null(reader,"ResultHash")?null:Text(reader,"ResultHash"),
        DurationMs=Null(reader,"DurationMs")?null:Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("DurationMs")),CultureInfo.InvariantCulture),
        StartedAtUtc=Null(reader,"StartedAtUtc")?null:Timestamp(reader,"StartedAtUtc"),
        FinishedAtUtc=Null(reader,"FinishedAtUtc")?null:Timestamp(reader,"FinishedAtUtc"),
        ReviewId=Null(reader,"ReviewId")?null:reader.GetGuid(reader.GetOrdinal("ReviewId")),
        BaselineSupports=Null(reader,"BaselineSupports")?null:Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("BaselineSupports")),CultureInfo.InvariantCulture),
        CandidateSupports=Null(reader,"CandidateSupports")?null:Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("CandidateSupports")),CultureInfo.InvariantCulture),
        CreatedAtUtc=Timestamp(reader,"CreatedAtUtc"),
    };
    internal static AiKnowledgeEvaluationOperationRow ReadEvaluationOperation(DbDataReader reader)=>new()
    {
        Id=reader.GetGuid(reader.GetOrdinal("Id")),
        StageKey=Text(reader,"StageKey"),
        ModelConfigId=reader.GetGuid(reader.GetOrdinal("ModelConfigId")),
        ModelConfigVersion=checked((int)Integer(reader,"ModelConfigVersion")),
        LedgerId=Null(reader,"LedgerId")?null:reader.GetGuid(reader.GetOrdinal("LedgerId")),
        UsageStatus=Null(reader,"UsageStatus")?null:Text(reader,"UsageStatus"),
        Outcome=Null(reader,"Outcome")?null:Text(reader,"Outcome"),
        QuotaMonthKey=Null(reader,"QuotaMonthKey")?null:Text(reader,"QuotaMonthKey"),
        PriceVersionId=Null(reader,"PriceVersionId")?null:reader.GetGuid(reader.GetOrdinal("PriceVersionId")),
        Currency=Null(reader,"Currency")?null:Text(reader,"Currency"),
        ReservedCost=Null(reader,"ReservedCost")?null:Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("ReservedCost")),CultureInfo.InvariantCulture),
        ChargedCost=Null(reader,"ChargedCost")?null:Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("ChargedCost")),CultureInfo.InvariantCulture),
        SettledAtUtc=Null(reader,"SettledAtUtc")?null:Timestamp(reader,"SettledAtUtc"),
    };
    internal static AiKnowledgeEvaluationCaseReviewRow ReadEvaluationCaseReview(DbDataReader reader)=>new()
    {
        Id=reader.GetGuid(reader.GetOrdinal("Id")),
        RunId=reader.GetGuid(reader.GetOrdinal("RunId")),
        CaseId=reader.GetGuid(reader.GetOrdinal("CaseId")),
        ResultHash=Text(reader,"ResultHash"),
        ReviewerUserId=reader.GetGuid(reader.GetOrdinal("ReviewerUserId")),
        BaselineSupports=Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("BaselineSupports")),CultureInfo.InvariantCulture),
        CandidateSupports=Convert.ToBoolean(reader.GetValue(reader.GetOrdinal("CandidateSupports")),CultureInfo.InvariantCulture),
        RequestKey=reader.GetGuid(reader.GetOrdinal("RequestKey")),
        RequestHash=Text(reader,"RequestHash"),
        CreatedAtUtc=Timestamp(reader,"CreatedAtUtc"),
    };
}
#endif
