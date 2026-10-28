using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageCollaborationGrants;

/// <summary>控制面明确共享双方同意；外部活动身份检查在事务外，本地只写Workflow自身授权及回执。</summary>
internal sealed class WorkflowCollaborationGrantService(IQueryExecutor query,ICommandExecutor command,ICommandTransaction transaction,
    ICurrentTenant tenant,IActiveTenantContextResolver activeTenants,IClock clock,IIdGenerator ids,
    ITenantFeatureEntitlementPort entitlements,IHostUserBatchSelectionDirectory hostUsers,ITenantMemberSelectionDirectory tenantUsers)
{
    public async Task<Result<WorkflowCollaborationGrantResponse>> GetAsync(Guid id,CancellationToken token)
    {
        var scope=WorkflowManagementScope.Resolve(tenant);
        var row=await FindAsync(id,scope.TenantScopeKey,token).ConfigureAwait(false);
        return row is null ? Fail(ErrorType.NotFound) : Result<WorkflowCollaborationGrantResponse>.Success(Map(row));
    }

    public async Task<Result<WorkflowCollaborationGrantResponse>> ProposeAsync(Guid actor,ProposeWorkflowCollaborationGrantRequest request,CancellationToken token)
    {
        var scope=WorkflowManagementScope.Resolve(tenant);
        if(actor==Guid.Empty || !Key(request.IdempotencyKey) || request.TargetTenantId==Guid.Empty ||
            request.TargetScopeKey!=(request.TargetTenantId is null ? "host" : "tenant") || request.TargetTenantId==scope.TenantId ||
            !Bounded(request.Offer) || !WorkflowCollaborationOfferPolicy.TryRead(request.Offer,out var offer)) return Fail(ErrorType.Validation);
        var hash=Digest("propose\n"+WorkflowJsonCanonicalizer.Compile(JsonSerializer.SerializeToElement(request,WorkflowJsonSerializerContext.Default.ProposeWorkflowCollaborationGrantRequest)).CanonicalJson);
        var replay=await ProposalReplayAsync(actor,scope,request.IdempotencyKey,hash,token).ConfigureAwait(false);
        if(replay is not null) return replay;
        if(await DeniedAsync(scope,token).ConfigureAwait(false) is { } denied) return denied;
        var now=Now();
        if(offer!.ExpiresAtUtc<=now || !await ActiveAsync(request.TargetTenantId,token).ConfigureAwait(false)) return Fail(ErrorType.Validation);
        var definition=await query.QuerySingleOrDefaultAsync<WorkflowDefinitionRecord>(WorkflowSql.FindDefinitionById,P(("Id",offer.SourceDefinitionId),("TenantScopeKey",scope.TenantScopeKey)),token).ConfigureAwait(false);
        var form=await query.QuerySingleOrDefaultAsync<WorkflowFormVersionRecord>(WorkflowSql.FindFormVersionById,P(("Id",offer.SourceFormVersionId),("TenantScopeKey",scope.TenantScopeKey)),token).ConfigureAwait(false);
        if(definition is null || definition.StatusKey!="active" || form is null || !OfferedFieldsExist(offer,form.FormSchemaJson)) return Fail(ErrorType.Validation);
        var id=ids.NewId();
        try
        {
            return await transaction.ExecuteResultAsync(async inner=>
            {
                var prior=await ProposalReplayAsync(actor,scope,request.IdempotencyKey,hash,inner).ConfigureAwait(false); if(prior is not null) return prior;
                var affected=await command.ExecuteAsync(WorkflowCollaborationGrantSql.Insert,P(("Id",id),("SourceTenantId",scope.TenantId),("TargetTenantId",request.TargetTenantId),
                    ("TenantScopeKey",scope.TenantScopeKey),("TargetTenantScopeKey",WorkflowCollaborationGrantPolicy.ScopeKey(request.TargetTenantId)),("SourceDefinitionId",offer.SourceDefinitionId),
                    ("SourceFormVersionId",offer.SourceFormVersionId),("ActorUserId",actor),("OfferJson",JsonSerializer.Serialize(offer,WorkflowJsonSerializerContext.Default.WorkflowCollaborationOfferPolicy)),
                    ("IdempotencyKey",request.IdempotencyKey),("RequestHash",hash),("ExpiresAtUtc",offer.ExpiresAtUtc),("Now",now)),inner).ConfigureAwait(false);
                if(affected!=1) return Fail(ErrorType.Conflict);
                var row=await FindAsync(id,scope.TenantScopeKey,inner).ConfigureAwait(false);
                return row is null ? Fail(ErrorType.Conflict) : await ReceiptAsync(row,actor,scope,"propose",request.IdempotencyKey,hash,now,inner).ConfigureAwait(false);
            },token).ConfigureAwait(false);
        }
        catch(DataCommandException exception) when(exception.Kind==DataCommandFailureKind.UniqueConstraint)
        { return await ProposalReplayAsync(actor,scope,request.IdempotencyKey,hash,token).ConfigureAwait(false) ?? Fail(ErrorType.Conflict); }
    }

    public async Task<Result<WorkflowCollaborationGrantResponse>> AcceptAsync(Guid id,Guid actor,AcceptWorkflowCollaborationGrantRequest request,CancellationToken token)
    {
        var scope=WorkflowManagementScope.Resolve(tenant);
        if(id==Guid.Empty || actor==Guid.Empty || request.ExpectedRevision!=1 || !Key(request.IdempotencyKey) || !Bounded(request.Acceptance) ||
            !WorkflowVotePolicy.UniqueObject(request.Acceptance) || request.Acceptance.EnumerateObject().Count()!=7 || request.Acceptance.TryGetProperty("targetSummaryHash",out _)) return Fail(ErrorType.Validation);
        var hash=Digest($"accept\n{id:D}\n{request.ExpectedRevision}\n"+WorkflowJsonCanonicalizer.Compile(request.Acceptance).CanonicalJson);
        var replay=await ActionReplayAsync(id,actor,scope,request.IdempotencyKey,hash,token).ConfigureAwait(false); if(replay is not null) return replay;
        var grant=await FindAsync(id,scope.TenantScopeKey,token).ConfigureAwait(false);
        if(grant is null || grant.TargetTenantScopeKey!=scope.TenantScopeKey) return Fail(ErrorType.NotFound);
        if(await DeniedAsync(scope,token).ConfigureAwait(false) is { } denied) return denied;
        var now=Now();
        if(grant.StatusKey!="proposed" || grant.Revision!=1 || grant.ExpiresAtUtc<=now) return Fail(ErrorType.Conflict);
        if(!await ActiveAsync(grant.SourceTenantId,token).ConfigureAwait(false)) return Fail(ErrorType.Conflict);
        using var proposal=JsonDocument.Parse(grant.OfferJson);
        if(!WorkflowCollaborationOfferPolicy.TryRead(proposal.RootElement,out var offer)) return Fail(ErrorType.Conflict);
        // 第一次解析仅验证请求结构；安全摘要始终由目标服务器从自己的固定版本生成。
        var tentative=WithSummary(request.Acceptance,new string('0',64));
        if(!WorkflowCollaborationAcceptancePolicy.TryRead(tentative,offer!,out var acceptance)) return Fail(ErrorType.Validation);
        var version=await query.QuerySingleOrDefaultAsync<WorkflowDefinitionVersionRecord>(WorkflowSql.FindDefinitionVersionById,P(("Id",acceptance!.TargetDefinitionVersionId),("TenantScopeKey",scope.TenantScopeKey)),token).ConfigureAwait(false);
        if(version is null) return Fail(ErrorType.Validation);
        var definition=await query.QuerySingleOrDefaultAsync<WorkflowDefinitionRecord>(WorkflowSql.FindDefinitionById,P(("Id",version.DefinitionId),("TenantScopeKey",scope.TenantScopeKey)),token).ConfigureAwait(false);
        var form=await query.QuerySingleOrDefaultAsync<WorkflowFormVersionRecord>(WorkflowSql.FindFormVersionById,P(("Id",version.FormVersionId),("TenantScopeKey",scope.TenantScopeKey)),token).ConfigureAwait(false);
        if(definition is null || definition.StatusKey!="active" || definition.DefinitionKey!=offer!.TargetWorkflowKey || form is null) return Fail(ErrorType.Validation);
        var summary=Summary(version,definition,form,acceptance);
        if(summary is null) return Fail(ErrorType.Validation);
        var members=acceptance.AllowedUserIds.ToArray();
        var valid=scope.TenantId is null ? (await hostUsers.FindActiveHostUsersAsync(members,token).ConfigureAwait(false)).Keys.ToHashSet()
            : (await tenantUsers.FindActiveTenantMembersAsync(members,token).ConfigureAwait(false)).Keys.ToHashSet();
        if(members.Any(member=>!valid.Contains(member))) return Fail(ErrorType.Validation);
        var summaryJson=JsonSerializer.Serialize(summary,WorkflowJsonSerializerContext.Default.WorkflowCollaborationTargetSummary);
        var summaryHash=Digest(summaryJson); acceptance=acceptance with { TargetSummaryHash=summaryHash };
        var confirmed=JsonSerializer.Serialize(acceptance,WorkflowJsonSerializerContext.Default.WorkflowCollaborationAcceptancePolicy);
        var policyHash=WorkflowCollaborationGrantPolicy.Hash(id,grant.SourceTenantId,grant.TargetTenantId,grant.SourceActorId,actor,offer,acceptance);
        return await MutateAsync(id,actor,scope,"accept",request.IdempotencyKey,hash,async inner=>
        {
            var changed=await command.ExecuteAsync(WorkflowCollaborationGrantSql.Accept,P(("Id",id),("ActorUserId",actor),("TenantScopeKey",scope.TenantScopeKey),("Revision",request.ExpectedRevision),
                ("TargetDefinitionVersionId",acceptance.TargetDefinitionVersionId),("TargetWorkflowKey",offer.TargetWorkflowKey),("AcceptanceJson",confirmed),("PolicyHash",policyHash),
                ("TargetSummaryJson",summaryJson),("TargetSummaryHash",summaryHash),("Now",Now())),inner).ConfigureAwait(false);
            return changed;
        },token).ConfigureAwait(false);
    }

    public async Task<Result<WorkflowCollaborationGrantResponse>> RevokeAsync(Guid id,Guid actor,RevokeWorkflowCollaborationGrantRequest request,CancellationToken token)
    {
        var scope=WorkflowManagementScope.Resolve(tenant); var reason=request.Reason?.Trim();
        if(id==Guid.Empty || actor==Guid.Empty || request.ExpectedRevision is <1 or >2 || !Key(request.IdempotencyKey) || string.IsNullOrEmpty(reason) || reason.Length>512) return Fail(ErrorType.Validation);
        var hash=Digest($"revoke\n{id:D}\n{request.ExpectedRevision}\n{reason}");
        var replay=await ActionReplayAsync(id,actor,scope,request.IdempotencyKey,hash,token).ConfigureAwait(false); if(replay is not null) return replay;
        if(await FindAsync(id,scope.TenantScopeKey,token).ConfigureAwait(false) is null) return Fail(ErrorType.NotFound);
        // 撤销是收紧已有许可，即使功能套餐失效也必须允许双方终止自己的同意。
        return await MutateAsync(id,actor,scope,"revoke",request.IdempotencyKey,hash,
            inner=>command.ExecuteAsync(WorkflowCollaborationGrantSql.Revoke,P(("Id",id),("ActorUserId",actor),("TenantScopeKey",scope.TenantScopeKey),
                ("Revision",request.ExpectedRevision),("Reason",reason),("Now",Now())),inner),token).ConfigureAwait(false);
    }

    private async Task<Result<WorkflowCollaborationGrantResponse>> MutateAsync(Guid id,Guid actor,WorkflowManagementScope scope,string operation,string key,string hash,
        Func<CancellationToken,Task<int>> mutate,CancellationToken token)
    {
        try
        {
            return await transaction.ExecuteResultAsync(async inner=>
            {
                var replay=await ActionReplayAsync(id,actor,scope,key,hash,inner).ConfigureAwait(false); if(replay is not null) return replay;
                if(await mutate(inner).ConfigureAwait(false)!=1) return Fail(ErrorType.Conflict);
                var row=await FindAsync(id,scope.TenantScopeKey,inner).ConfigureAwait(false);
                return row is null ? Fail(ErrorType.Conflict) : await ReceiptAsync(row,actor,scope,operation,key,hash,Now(),inner).ConfigureAwait(false);
            },token).ConfigureAwait(false);
        }
        catch(DataCommandException exception) when(exception.Kind==DataCommandFailureKind.UniqueConstraint)
        { return await ActionReplayAsync(id,actor,scope,key,hash,token).ConfigureAwait(false) ?? Fail(ErrorType.Conflict); }
    }
    private async Task<Result<WorkflowCollaborationGrantResponse>> ReceiptAsync(WorkflowCollaborationGrantFact row,Guid actor,WorkflowManagementScope scope,string operation,string key,string hash,DateTimeOffset now,CancellationToken token)
    {
        var response=Map(row); var json=JsonSerializer.Serialize(response,WorkflowJsonSerializerContext.Default.WorkflowCollaborationGrantResponse);
        var changed=await command.ExecuteAsync(WorkflowCollaborationGrantSql.InsertAction,P(("ActionId",ids.NewId()),("Id",row.Id),("TenantScopeKey",scope.TenantScopeKey),
            ("ActorUserId",actor),("OperationKey",operation),("IdempotencyKey",key),("RequestHash",hash),("ResponseJson",json),("Revision",row.Revision),("StatusKey",row.StatusKey),("Now",now)),token).ConfigureAwait(false);
        return changed==1 ? Result<WorkflowCollaborationGrantResponse>.Success(response) : Fail(ErrorType.Conflict);
    }
    private Task<WorkflowCollaborationGrantFact?> FindAsync(Guid id,string scope,CancellationToken token)=>query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,P(("Id",id),("TenantScopeKey",scope)),token);
    private async Task<Result<WorkflowCollaborationGrantResponse>?> ActionReplayAsync(Guid id,Guid actor,WorkflowManagementScope scope,string key,string hash,CancellationToken token)=>
        Replay(await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantActionRecord>(WorkflowCollaborationGrantSql.FindAction,P(("Id",id),("TenantScopeKey",scope.TenantScopeKey),("ActorUserId",actor),("IdempotencyKey",key)),token).ConfigureAwait(false),hash);
    private async Task<Result<WorkflowCollaborationGrantResponse>?> ProposalReplayAsync(Guid actor,WorkflowManagementScope scope,string key,string hash,CancellationToken token)=>
        Replay(await query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantActionRecord>(WorkflowCollaborationGrantSql.FindProposalAction,P(("TenantScopeKey",scope.TenantScopeKey),("ActorUserId",actor),("IdempotencyKey",key)),token).ConfigureAwait(false),hash);
    private static Result<WorkflowCollaborationGrantResponse>? Replay(WorkflowCollaborationGrantActionRecord? row,string hash)=>row is null ? null : row.RequestHash!=hash ? Fail(ErrorType.Conflict)
        : Result<WorkflowCollaborationGrantResponse>.Success(JsonSerializer.Deserialize(row.ResponseJson,WorkflowJsonSerializerContext.Default.WorkflowCollaborationGrantResponse)!);
    private async Task<Result<WorkflowCollaborationGrantResponse>?> DeniedAsync(WorkflowManagementScope scope,CancellationToken token)=>
        await WorkflowTenantFeatureEntitlementGate.TryGetDenialAsync(scope,entitlements,token).ConfigureAwait(false) is { } denial ? WorkflowTenantFeatureEntitlementGate.Deny<WorkflowCollaborationGrantResponse>(denial) : null;
    private async Task<bool> ActiveAsync(Guid? id,CancellationToken token)=>id is null || await activeTenants.ResolveActiveByIdAsync(id.Value,token).ConfigureAwait(false) is not null;
    private DateTimeOffset Now()=>WorkflowCollaborationOfferPolicy.NormalizeTime(clock.UtcNow);
    private static bool Key(string? key)=>key is { Length:>=1 and <=128 } && key.All(character=>char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or ':' or '.');
    private static bool Bounded(JsonElement value)=>value.ValueKind==JsonValueKind.Object && value.GetRawText().Length<=32_768;
    private static string Digest(string value)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static Dictionary<string,object?> P(params (string Name,object? Value)[] values)=>WorkflowSqlParameters.Create(values);
    private static JsonElement WithSummary(JsonElement source,string hash)
    {
        using var stream=new MemoryStream(); using(var writer=new Utf8JsonWriter(stream))
        { writer.WriteStartObject(); foreach(var property in source.EnumerateObject()) property.WriteTo(writer); writer.WriteString("targetSummaryHash",hash); writer.WriteEndObject(); }
        using var document=JsonDocument.Parse(stream.ToArray()); return document.RootElement.Clone();
    }
    private static bool OfferedFieldsExist(WorkflowCollaborationOfferPolicy offer,string schemaJson)
    {
        var schema=JsonSerializer.Deserialize(schemaJson,WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var fields=schema?.Sections.SelectMany(section=>section.Fields).Select(field=>field.FieldKey).ToHashSet(StringComparer.Ordinal);
        return fields is not null && offer.InputFieldKeys.Concat(offer.OutputFieldKeys).All(fields.Contains);
    }
    private static WorkflowCollaborationTargetSummary? Summary(WorkflowDefinitionVersionRecord version,WorkflowDefinitionRecord definition,WorkflowFormVersionRecord form,WorkflowCollaborationAcceptancePolicy acceptance)
    {
        var schema=JsonSerializer.Deserialize(form.FormSchemaJson,WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        if(schema is null) return null;
        var fields=schema.Sections.SelectMany(section=>section.Fields).ToDictionary(field=>field.FieldKey,field=>field.FieldTypeKey,StringComparer.Ordinal);
        if(acceptance.InputFields.Values.Concat(acceptance.OutputFields.Keys).Any(key=>!fields.ContainsKey(key))) return null;
        return new(version.Id,definition.DefinitionKey,version.ContentHash,form.ContentHash,
            acceptance.InputFields.Values.Order(StringComparer.Ordinal).ToDictionary(key=>key,key=>fields[key],StringComparer.Ordinal),
            acceptance.OutputFields.Keys.Order(StringComparer.Ordinal).ToDictionary(key=>key,key=>fields[key],StringComparer.Ordinal));
    }
    private static WorkflowCollaborationGrantResponse Map(WorkflowCollaborationGrantFact row)=>new(row.Id,row.Revision,row.StatusKey,row.SourceTenantScopeKey,row.TargetTenantScopeKey,
        row.SourceActorId,row.TargetActorId,Element(row.OfferJson),row.AcceptanceJson is null ? null : Element(row.AcceptanceJson),row.PolicyHash,
        row.TargetSummaryJson is null ? null : Element(row.TargetSummaryJson),row.ExpiresAtUtc,row.CreatedAtUtc,row.AcceptedAtUtc,row.RevokedAtUtc,row.RevokedById,row.RevokedByScopeKey);
    private static JsonElement Element(string json) { using var document=JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static Result<WorkflowCollaborationGrantResponse> Fail(ErrorType type)=>Result<WorkflowCollaborationGrantResponse>.Failure(new Error(type switch
    { ErrorType.Validation=>WorkflowErrorCodes.CollaborationGrantInvalid,ErrorType.NotFound=>WorkflowErrorCodes.CollaborationGrantNotFound,_=>WorkflowErrorCodes.CollaborationGrantConflict },"The collaboration grant operation is not valid.",type));
}
