using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>原答案事务写最小观测；运营仅能聚合本人库，私人正文不作为返回字段。</summary>
internal static class AiKnowledgeObservationSql
{
    private static readonly SqlStatement InsertHost=new("ai.knowledge_observation.insert_host","INSERT INTO fn_ai_knowledge_answer_observation (Id,TenantId,KnowledgeBaseIdsJson,Refused,DurationMs,CreatedAtUtc) VALUES (@MessageId,NULL,@KnowledgeBaseIdsJson,@Refused,@DurationMs,@Now)",SqlDataScope.HostOnly);
    private static readonly SqlStatement InsertTenant=new("ai.knowledge_observation.insert_tenant","INSERT INTO fn_ai_knowledge_answer_observation (Id,TenantId,KnowledgeBaseIdsJson,Refused,DurationMs,CreatedAtUtc) VALUES (@MessageId,@TenantId,@KnowledgeBaseIdsJson,@Refused,@DurationMs,@Now)",SqlDataScope.TenantRequired,SqlTenantBinding.CurrentTenantId);
    internal static SqlStatement InsertFor(bool scoped)=>scoped?InsertTenant:InsertHost;
    private static readonly IReadOnlyDictionary<(bool Mysql,bool Scoped),SqlStatement> Reads=Build();
    internal static SqlStatement ReadFor(DatabaseProvider provider,bool scoped)=>Reads[(provider==DatabaseProvider.MySql,scoped)];
    private static IReadOnlyDictionary<(bool,bool),SqlStatement> Build()
    {
        var map=new Dictionary<(bool,bool),SqlStatement>();
        foreach(var mysql in new[]{false,true})foreach(var scoped in new[]{false,true})
        {
            string Scope(string alias)=>scoped?alias+".TenantId=@TenantId":alias+".TenantId IS NULL";
            // MySQL直接按闭合对象包含判断，避免嵌套相关JSON_TABLE在反馈聚合中漏掉实际匹配行。
            string Contains(string json,string path,string column)=>mysql
                ?path=="$[*]"?"JSON_CONTAINS("+json+",JSON_QUOTE(BIN_TO_UUID("+column+",0)))=1"
                    :"JSON_CONTAINS("+json+",JSON_OBJECT('knowledgeBaseId',BIN_TO_UUID("+column+",0)),'$.dependencies')=1"
                :"EXISTS (SELECT 1 FROM OPENJSON("+json+",'"+path.Replace("[*]","",StringComparison.Ordinal)+"') WITH (Id uniqueidentifier '$"+(path=="$[*]"?"":".knowledgeBaseId")+"') AS ids WHERE ids.Id="+column+")";
            var eligible=Scope("o")+" AND o.CreatedAtUtc>=@FromUtc AND o.CreatedAtUtc<@ToUtc AND "+Contains("o.KnowledgeBaseIdsJson","$[*]","k.Id");
            var safe=mysql?"CASE WHEN JSON_VALID(message.KnowledgeLineage)=1 THEN message.KnowledgeLineage ELSE '{\"dependencies\":[]}' END"
                :"CASE WHEN ISJSON(message.KnowledgeLineage)=1 THEN message.KnowledgeLineage ELSE N'{\"dependencies\":[]}' END";
            var feedback=" FROM fn_ai_knowledge_answer_feedback AS feedback INNER JOIN fn_ai_chat_message AS message ON message.Id=feedback.MessageId INNER JOIN fn_ai_chat_session AS session ON session.Id=message.SessionId WHERE feedback.RatingKey='unhelpful' AND feedback.StatusKey='pending_review' AND feedback.UpdatedAtUtc>=@FromUtc AND feedback.UpdatedAtUtc<@ToUtc AND "+Scope("feedback")+" AND "+Scope("session")+" AND "+Contains(safe,"$.dependencies[*]","k.Id");
            var sql="SELECT k.Id AS KnowledgeBaseId,(SELECT COUNT(*) FROM fn_ai_knowledge_answer_observation AS o WHERE "+eligible+") AS RecordedAnswers,"
                +"(SELECT COUNT(*) FROM fn_ai_knowledge_answer_observation AS o WHERE "+eligible+" AND o.Refused=1) AS Refused,"
                +"(SELECT COUNT(*) FROM fn_ai_knowledge_answer_observation AS o WHERE "+eligible+" AND o.DurationMs IS NOT NULL) AS Measured,"
                +"(SELECT AVG(o.DurationMs) FROM fn_ai_knowledge_answer_observation AS o WHERE "+eligible+") AS MeanPipelineMs,"
                +"(SELECT MIN(o.CreatedAtUtc) FROM fn_ai_knowledge_answer_observation AS o WHERE "+eligible+") AS FirstRecordedAtUtc,"
                +"(SELECT COUNT(*)"+feedback+") AS CurrentNegativeFeedback FROM fn_ai_knowledge_base AS k WHERE k.Id=@KnowledgeBaseId AND k.IsEnabled=1 AND k.OwnerUserId=@Actor AND "+Scope("k");
            map[(mysql,scoped)]=new("ai.knowledge_observation.read"+(mysql?"_mysql_":"_sqlserver_")+(scoped?"tenant":"host"),sql,scoped?SqlDataScope.TenantRequired:SqlDataScope.HostOnly,scoped?SqlTenantBinding.CurrentTenantId:SqlTenantBinding.None);
        }
        return map;
    }
}

/// <summary>最小聚合投影，不包含可供私人正文读取的消息或会话身份。</summary>
internal sealed record AiKnowledgeQualityObservationRow
{
    public Guid KnowledgeBaseId {get;init;}
    public long RecordedAnswers {get;init;}
    public long Refused {get;init;}
    public long Measured {get;init;}
    public decimal? MeanPipelineMs {get;init;}
    public DateTimeOffset? FirstRecordedAtUtc {get;init;}
    public long CurrentNegativeFeedback {get;init;}
}
