using System.Text.Json;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageWorkCalendars;

/// <summary>日历草稿使用乐观锁，发布版本与发布头同事务写入，租户权益在事务外验证。</summary>
internal sealed class WorkflowWorkCalendarService(IQueryExecutor query,ICommandExecutor commands,ICommandTransaction transaction,
    ICurrentTenant tenants,IClock clock,IIdGenerator ids,ITenantFeatureEntitlementPort entitlements,IOptions<DatabaseOptions> database)
{
    private const int PageSize=20;
    public async Task<Result<WorkflowWorkCalendarPageResponse>> ListAsync(Guid? afterId,CancellationToken token)
    {
        if(afterId==Guid.Empty)return Invalid<WorkflowWorkCalendarPageResponse>();
        var scope=WorkflowManagementScope.Resolve(tenants);WorkflowCalendarRecord? after=null;
        if(afterId is { } cursor){after=await FindAsync(cursor,scope,token).ConfigureAwait(false);if(after is null)return NotFound<WorkflowWorkCalendarPageResponse>();}
        var sql=database.Value.Provider==DatabaseProvider.SqlServer?WorkflowCalendarSql.ListSqlServer:WorkflowCalendarSql.ListMySql;
        var rows=await query.QueryAsync<WorkflowCalendarSummaryRecord>(sql,Parameters(("TenantScopeKey",scope.TenantScopeKey),("Take",PageSize+1),
            ("HasAfter",after is null?0:1),("AfterCreatedAtUtc",after?.CreatedAtUtc??DateTimeOffset.UnixEpoch),("AfterId",after?.Id)),token).ConfigureAwait(false);
        var selected=rows.Take(PageSize).Select(row=>new WorkflowWorkCalendarSummary(row.Id,row.CalendarKey,row.DisplayName,row.Revision,row.LatestVersionNumber,row.LatestVersionId,row.TimeZoneKey,row.CreatedAtUtc,row.UpdatedAtUtc)).ToArray();
        return Result<WorkflowWorkCalendarPageResponse>.Success(new(selected,rows.Count>PageSize?selected[^1].Id:null));
    }
    public async Task<Result<WorkflowWorkCalendarResponse>> GetAsync(Guid id,CancellationToken token)
    {
        if(id==Guid.Empty)return Invalid<WorkflowWorkCalendarResponse>();
        var row=await FindAsync(id,WorkflowManagementScope.Resolve(tenants),token).ConfigureAwait(false);
        if(row is null)return NotFound<WorkflowWorkCalendarResponse>();
        return Map(row) is { } result?Result<WorkflowWorkCalendarResponse>.Success(result):Invalid<WorkflowWorkCalendarResponse>();
    }
    public async Task<Result<WorkflowWorkCalendarVersionPageResponse>> ListVersionsAsync(Guid id,int? beforeVersion,CancellationToken token)
    {
        if(id==Guid.Empty || beforeVersion is <=0)return Invalid<WorkflowWorkCalendarVersionPageResponse>();
        var scope=WorkflowManagementScope.Resolve(tenants);if(await FindAsync(id,scope,token).ConfigureAwait(false) is null)return NotFound<WorkflowWorkCalendarVersionPageResponse>();
        var sql=database.Value.Provider==DatabaseProvider.SqlServer?WorkflowCalendarSql.ListVersionsSqlServer:WorkflowCalendarSql.ListVersionsMySql;
        var rows=await query.QueryAsync<WorkflowCalendarVersionSummaryRecord>(sql,Parameters(("TenantScopeKey",scope.TenantScopeKey),("CalendarId",id),
            ("BeforeVersionNumber",beforeVersion??int.MaxValue),("Take",PageSize+1)),token).ConfigureAwait(false);
        var selected=rows.Take(PageSize).Select(row=>new WorkflowWorkCalendarVersionSummary(row.Id,row.CalendarId,row.VersionNumber,row.ContentHash,row.TimeZoneKey,row.PublishedAtUtc)).ToArray();
        return Result<WorkflowWorkCalendarVersionPageResponse>.Success(new(selected,rows.Count>PageSize?selected[^1].VersionNumber:null));
    }
    public async Task<Result<WorkflowWorkCalendarVersionResponse>> GetVersionAsync(Guid id,CancellationToken token)
    {
        if(id==Guid.Empty)return Invalid<WorkflowWorkCalendarVersionResponse>();
        var scope=WorkflowManagementScope.Resolve(tenants);
        var row=await query.QuerySingleOrDefaultAsync<WorkflowCalendarVersionRecord>(WorkflowCalendarSql.FindVersion,Parameters(("Id",id),("TenantScopeKey",scope.TenantScopeKey)),token).ConfigureAwait(false);
        if(row is null)return Failure<WorkflowWorkCalendarVersionResponse>(WorkflowErrorCodes.CalendarVersionNotFound,ErrorType.NotFound);
        return MapVersion(row) is { } result?Result<WorkflowWorkCalendarVersionResponse>.Success(result):Invalid<WorkflowWorkCalendarVersionResponse>();
    }
    public async Task<Result<WorkflowWorkCalendarResponse>> CreateAsync(Guid actor,CreateWorkflowWorkCalendarRequest request,CancellationToken token)
    {
        if(actor==Guid.Empty || !ValidKey(request.CalendarKey) || !ValidName(request.DisplayName) || !WorkflowWorkCalendar.TryCreate(request.Draft,out _))return Invalid<WorkflowWorkCalendarResponse>();
        var scope=WorkflowManagementScope.Resolve(tenants);if(await DenialAsync<WorkflowWorkCalendarResponse>(scope,token).ConfigureAwait(false) is { } denied)return denied;
        var id=ids.NewId();var now=clock.UtcNow;var name=request.DisplayName.Trim();var draft=JsonSerializer.Serialize(request.Draft,WorkflowJsonSerializerContext.Default.WorkflowWorkCalendarDraft);
        try
        {
            return await transaction.ExecuteResultAsync(async inner=>{
                if(await commands.ExecuteAsync(WorkflowCalendarSql.Insert,Parameters(("Id",id),("TenantId",scope.TenantId),("ScopeKey",scope.ScopeKey),
                    ("TenantScopeKey",scope.TenantScopeKey),("CalendarKey",request.CalendarKey),("DisplayName",name),("DraftJson",draft),("ActorId",actor),("Now",now)),inner).ConfigureAwait(false)!=1)return Conflict<WorkflowWorkCalendarResponse>();
                var row=await FindAsync(id,scope,inner).ConfigureAwait(false);var response=row is null?null:Map(row);
                return response is null?Invalid<WorkflowWorkCalendarResponse>():Result<WorkflowWorkCalendarResponse>.Success(response);
            },token).ConfigureAwait(false);
        }
        catch(DataCommandException error) when(error.Kind==DataCommandFailureKind.UniqueConstraint)
        {return Failure<WorkflowWorkCalendarResponse>(WorkflowErrorCodes.CalendarKeyExists,ErrorType.Conflict);}
    }
    public async Task<Result<WorkflowWorkCalendarResponse>> UpdateAsync(Guid id,Guid actor,UpdateWorkflowWorkCalendarRequest request,CancellationToken token)
    {
        if(id==Guid.Empty || actor==Guid.Empty || request.ExpectedRevision<1 || !ValidName(request.DisplayName) || !WorkflowWorkCalendar.TryCreate(request.Draft,out _))return Invalid<WorkflowWorkCalendarResponse>();
        var scope=WorkflowManagementScope.Resolve(tenants);if(await DenialAsync<WorkflowWorkCalendarResponse>(scope,token).ConfigureAwait(false) is { } denied)return denied;
        return await transaction.ExecuteResultAsync(async inner=>{
            var current=await FindAsync(id,scope,inner).ConfigureAwait(false);if(current is null)return NotFound<WorkflowWorkCalendarResponse>();
            if(current.Revision!=request.ExpectedRevision)return Conflict<WorkflowWorkCalendarResponse>();
            if(await commands.ExecuteAsync(WorkflowCalendarSql.UpdateDraft,Parameters(("Id",id),("TenantScopeKey",scope.TenantScopeKey),("Revision",request.ExpectedRevision),
                ("DisplayName",request.DisplayName.Trim()),("DraftJson",JsonSerializer.Serialize(request.Draft,WorkflowJsonSerializerContext.Default.WorkflowWorkCalendarDraft)),("ActorId",actor),("Now",clock.UtcNow)),inner).ConfigureAwait(false)!=1)return Conflict<WorkflowWorkCalendarResponse>();
            var row=await FindAsync(id,scope,inner).ConfigureAwait(false);var response=row is null?null:Map(row);
            return response is null?Invalid<WorkflowWorkCalendarResponse>():Result<WorkflowWorkCalendarResponse>.Success(response);
        },token).ConfigureAwait(false);
    }
    public async Task<Result<WorkflowWorkCalendarVersionResponse>> PublishAsync(Guid id,Guid actor,PublishWorkflowWorkCalendarRequest request,CancellationToken token)
    {
        if(id==Guid.Empty || actor==Guid.Empty || request.ExpectedRevision<1)return Invalid<WorkflowWorkCalendarVersionResponse>();
        var scope=WorkflowManagementScope.Resolve(tenants);if(await DenialAsync<WorkflowWorkCalendarVersionResponse>(scope,token).ConfigureAwait(false) is { } denied)return denied;
        try
        {
            return await transaction.ExecuteResultAsync(async inner=>{
                var current=await FindAsync(id,scope,inner).ConfigureAwait(false);if(current is null)return NotFound<WorkflowWorkCalendarVersionResponse>();
                if(current.Revision!=request.ExpectedRevision || current.LatestVersionNumber is >=int.MaxValue-1)return Conflict<WorkflowWorkCalendarVersionResponse>();
                // 先锁发布头再插入带父外键的版本，避免并发发布形成父行锁升级竞争。
                if(await commands.ExecuteAsync(WorkflowCalendarSql.Lock,Parameters(("Id",id),("TenantScopeKey",scope.TenantScopeKey),("Revision",request.ExpectedRevision)),inner).ConfigureAwait(false)!=1)return Conflict<WorkflowWorkCalendarVersionResponse>();
                var draft=ReadDraft(current.DraftJson);if(!WorkflowWorkCalendar.TryCreate(draft,out var calendar))return Invalid<WorkflowWorkCalendarVersionResponse>();
                var versionId=ids.NewId();var number=(current.LatestVersionNumber??0)+1;var now=clock.UtcNow;
                if(await commands.ExecuteAsync(WorkflowCalendarSql.InsertVersion,Parameters(("Id",versionId),("CalendarId",id),("VersionNumber",number),("TenantScopeKey",scope.TenantScopeKey),
                    ("CanonicalJson",calendar!.CanonicalJson),("ContentHash",calendar.ContentHash),("TimeZoneKey",calendar.TimeZoneKey),("ActorId",actor),("Now",now)),inner).ConfigureAwait(false)!=1 ||
                    await commands.ExecuteAsync(WorkflowCalendarSql.Publish,Parameters(("Id",id),("TenantScopeKey",scope.TenantScopeKey),("Revision",request.ExpectedRevision),("VersionNumber",number),("ActorId",actor),("Now",now)),inner).ConfigureAwait(false)!=1)return Conflict<WorkflowWorkCalendarVersionResponse>();
                return Result<WorkflowWorkCalendarVersionResponse>.Success(new(versionId,id,number,draft!,calendar.ContentHash,calendar.TimeZoneKey,now));
            },token).ConfigureAwait(false);
        }
        catch(DataCommandException error) when(error.Kind==DataCommandFailureKind.UniqueConstraint){return Conflict<WorkflowWorkCalendarVersionResponse>();}
    }
    private Task<WorkflowCalendarRecord?> FindAsync(Guid id,WorkflowManagementScope scope,CancellationToken token)=>query.QuerySingleOrDefaultAsync<WorkflowCalendarRecord>(WorkflowCalendarSql.Find,Parameters(("Id",id),("TenantScopeKey",scope.TenantScopeKey)),token);
    private async Task<Result<T>?> DenialAsync<T>(WorkflowManagementScope scope,CancellationToken token)=>await WorkflowTenantFeatureEntitlementGate.TryGetDenialAsync(scope,entitlements,token).ConfigureAwait(false) is { } error?Result<T>.Failure(error):null;
    private static WorkflowWorkCalendarDraft? ReadDraft(string json)
    {try{return JsonSerializer.Deserialize(json,WorkflowJsonSerializerContext.Default.WorkflowWorkCalendarDraft);}catch(JsonException){return null;}}
    private static WorkflowWorkCalendarResponse? Map(WorkflowCalendarRecord row)=>ReadDraft(row.DraftJson) is { } draft
        ?new(row.Id,row.CalendarKey,row.DisplayName,draft,row.Revision,row.LatestVersionNumber,row.LatestVersionId,row.CreatedAtUtc,row.UpdatedAtUtc):null;
    private static WorkflowWorkCalendarVersionResponse? MapVersion(WorkflowCalendarVersionRecord row)
    {
        if(Encoding.UTF8.GetByteCount(row.CanonicalJson)>131072)return null;
        try
        {
            using var json=JsonDocument.Parse(row.CanonicalJson);
            if(!WorkflowWorkCalendar.TryRead(json.RootElement,out var calendar) || calendar!.ContentHash!=row.ContentHash || calendar.TimeZoneKey!=row.TimeZoneKey)return null;
            var snapshot=json.RootElement.Deserialize(WorkflowJsonSerializerContext.Default.WorkflowWorkCalendarSnapshot)!;
            return new(row.Id,row.CalendarId,row.VersionNumber,new(snapshot.SchemaVersion,snapshot.TimeZoneKey,snapshot.Weekdays,snapshot.Exceptions),row.ContentHash,row.TimeZoneKey,row.PublishedAtUtc);
        }
        catch(JsonException){return null;}
    }
    private static bool ValidKey(string? value)=>value is{Length:>=3 and <=128} && char.IsAsciiLetter(value[0]) && value.All(character=>char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');
    private static bool ValidName(string? value)=>value is not null && value.Trim() is{Length:>=1 and <=200} && !value.Any(char.IsControl);
    private static IReadOnlyDictionary<string,object?> Parameters(params(string Key,object? Value)[] values)=>WorkflowSqlParameters.Create(values);
    private static Result<T> NotFound<T>()=>Failure<T>(WorkflowErrorCodes.CalendarNotFound,ErrorType.NotFound);
    private static Result<T> Invalid<T>()=>Failure<T>(WorkflowErrorCodes.CalendarInvalid,ErrorType.Validation);
    private static Result<T> Conflict<T>()=>Failure<T>(WorkflowErrorCodes.RevisionConflict,ErrorType.Conflict);
    private static Result<T> Failure<T>(string code,ErrorType type)=>Result<T>.Failure(new Error(code,"The workflow calendar operation failed.",type));
}
