using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageKnowledgeOperations;

/// <summary>维护诊断与范围财务分别授权，两个入口都不交付私人正文、主体或会话身份。</summary>
internal sealed class AiKnowledgeQualityService(IQueryExecutor query,IDataTransactionState transactions,ICurrentTenant tenant,
    ICurrentSessionAuthorization authorization,IClock clock,IOptions<DatabaseOptions> database)
{
    internal async Task<Result<AiKnowledgeFinanceReport>> FinanceAsync(Guid actor,Guid? knowledge,DateTimeOffset? from,DateTimeOffset? to,CancellationToken token)
    {
        var range=Range(from,to);if(range is null||knowledge==Guid.Empty)return Fail<AiKnowledgeFinanceReport>(ErrorType.Validation);
        var scope=tenant.Id;var scoped=AiKnowledgeScope.IsTenant(tenant);var required=new[]{AiKnowledgeFinancePermissions.Read};
        var original=await Authorize(actor,required,token).ConfigureAwait(false);if(original is null)return Fail<AiKnowledgeFinanceReport>(ErrorType.Forbidden);
        var p=new Dictionary<string,object?>{["ScopeKey"]=scope?.ToString("N")??"host",["ScopeTenantId"]=scope,["KnowledgeBaseId"]=knowledge,
            ["FromUtc"]=range.Value.Start,["ToUtc"]=range.Value.End,["FromMonth"]=range.Value.Start.ToString("yyyy-MM",CultureInfo.InvariantCulture),["ToMonth"]=range.Value.End.ToString("yyyy-MM",CultureInfo.InvariantCulture)};
        var rows=await query.QueryAsync<AiKnowledgeFinanceRow>(AiKnowledgeFinanceSql.ReadFor(database.Value.Provider),p,token).ConfigureAwait(false);
        Outside();if(scope!=tenant.Id||scoped!=AiKnowledgeScope.IsTenant(tenant))return Fail<AiKnowledgeFinanceReport>(ErrorType.Forbidden);
        var current=await Authorize(actor,required,token).ConfigureAwait(false);
        if(current!=original||scope!=tenant.Id||scoped!=AiKnowledgeScope.IsTenant(tenant))return Fail<AiKnowledgeFinanceReport>(ErrorType.Forbidden);
        // 查询等待后的权限必须重新取得；授权等待后的账本也重新读取，不交付旧快照。
        rows=await query.QueryAsync<AiKnowledgeFinanceRow>(AiKnowledgeFinanceSql.ReadFor(database.Value.Provider),p,token).ConfigureAwait(false);
        Outside();token.ThrowIfCancellationRequested();if(scope!=tenant.Id||scoped!=AiKnowledgeScope.IsTenant(tenant))return Fail<AiKnowledgeFinanceReport>(ErrorType.Forbidden);
        if(rows.Count>200||rows.Any(row=>row.Requests<0||row.Known+row.Unknown+row.Reserved!=row.Requests||row.MissingCost<0||row.MissingCost>row.Requests))return Fail<AiKnowledgeFinanceReport>(ErrorType.Conflict);
        return Result<AiKnowledgeFinanceReport>.Success(new(range.Value.Start,range.Value.End,"ledger_latency_not_recorded",rows.Select(row=>
            new AiKnowledgeFinanceBucket(row.KnowledgeBaseId,row.KnowledgeBaseId.HasValue?"explicit_operation":"unattributed",row.Month,row.PriceVersionId,row.Currency,
                row.Requests,row.Known,row.Unknown,row.Reserved,row.MissingCost,row.RecordedCost?.ToString(CultureInfo.InvariantCulture),row.ConservativeReservation?.ToString(CultureInfo.InvariantCulture))).ToArray()));
    }

    internal async Task<Result<AiKnowledgeQualityObservation>> ObservationsAsync(Guid knowledge,Guid actor,DateTimeOffset? from,DateTimeOffset? to,CancellationToken token)
    {
        var range=Range(from,to);if(range is null||knowledge==Guid.Empty)return Fail<AiKnowledgeQualityObservation>(ErrorType.Validation);
        var scope=tenant.Id;var scoped=AiKnowledgeScope.IsTenant(tenant);
        string[] required=[AiKnowledgeOperationsPermissions.Read,AiKnowledgePermissions.Read,AiKnowledgeAnswerFeedbackReviewPermissions.Read];
        var original=await Authorize(actor,required,token).ConfigureAwait(false);if(original is null)return Fail<AiKnowledgeQualityObservation>(ErrorType.Forbidden);
        var p=new Dictionary<string,object?>{["KnowledgeBaseId"]=knowledge,["Actor"]=actor,["FromUtc"]=range.Value.Start,["ToUtc"]=range.Value.End};
        var row=await query.QuerySingleOrDefaultAsync<AiKnowledgeQualityObservationRow>(AiKnowledgeObservationSql.ReadFor(database.Value.Provider,scoped),p,token).ConfigureAwait(false);
        Outside();if(row is null||scope!=tenant.Id||scoped!=AiKnowledgeScope.IsTenant(tenant))return Fail<AiKnowledgeQualityObservation>(ErrorType.NotFound);
        var current=await Authorize(actor,required,token).ConfigureAwait(false);
        if(current!=original||scope!=tenant.Id||scoped!=AiKnowledgeScope.IsTenant(tenant))return Fail<AiKnowledgeQualityObservation>(ErrorType.Forbidden);
        // 最后SQL重新核对所有者、库状态及当前反馈，覆盖等待授权期间的转移和撤回。
        row=await query.QuerySingleOrDefaultAsync<AiKnowledgeQualityObservationRow>(AiKnowledgeObservationSql.ReadFor(database.Value.Provider,scoped),p,token).ConfigureAwait(false);
        Outside();token.ThrowIfCancellationRequested();if(row is null||scope!=tenant.Id||scoped!=AiKnowledgeScope.IsTenant(tenant))return Fail<AiKnowledgeQualityObservation>(ErrorType.NotFound);
        if(row.RecordedAnswers<row.Refused||row.RecordedAnswers<row.Measured||row.Refused<0||row.Measured<0||row.CurrentNegativeFeedback<0)return Fail<AiKnowledgeQualityObservation>(ErrorType.Conflict);
        return Result<AiKnowledgeQualityObservation>.Success(new(knowledge,range.Value.Start,range.Value.End,row.RecordedAnswers,row.Refused,row.Measured,
            row.MeanPipelineMs.HasValue?(double)row.MeanPipelineMs.Value:null,row.FirstRecordedAtUtc,row.CurrentNegativeFeedback,"older_answers_not_recorded","paid_knowledge_pipeline_monotonic_v1"));
    }
    private async Task<Guid?> Authorize(Guid actor,string[] permissions,CancellationToken token)
    {Outside();var session=await authorization.AuthorizeAllAsync(permissions,token).ConfigureAwait(false);Outside();return session?.UserId==actor&&session.TenantId==tenant.Id?session.SessionId:null;}
    private (DateTimeOffset Start,DateTimeOffset End)? Range(DateTimeOffset? from,DateTimeOffset? to)
    {Outside();var end=to?.ToUniversalTime()??clock.UtcNow;if(end.UtcTicks<TimeSpan.FromDays(30).Ticks)return null;var start=from?.ToUniversalTime()??end.AddDays(-30);return start<end&&end<=clock.UtcNow&&end-start<=TimeSpan.FromDays(31)?(start,end):null;}
    private void Outside(){if(transactions.HasTransaction)throw new InvalidOperationException("财务/结果观测不能加入事务。");}
    private static Result<T> Fail<T>(ErrorType type)=>Result<T>.Failure(new(type==ErrorType.Forbidden?"authorization.permission_denied":type==ErrorType.NotFound?"ai.knowledge.not_found":type==ErrorType.Validation?"ai.knowledge.input_invalid":"ai.knowledge.index_unavailable","The quality or finance query is unavailable or invalid.",type));
}
