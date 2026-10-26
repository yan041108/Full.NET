using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Observability;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Features.ManageConnectorCalls;

/// <summary>对账只选择原始回执或在明确安全条件下重排原请求，不构造远端响应或修改旧输入。</summary>
internal sealed partial class WorkflowConnectorManagementService
{
    public async Task<Result<WorkflowConnectorResolutionResponse>> ReconcileAsync(Guid instanceId,Guid id,Guid actorId,
        ReconcileWorkflowConnectorRequest request,CancellationToken token)
    {
        if (instanceId==Guid.Empty || id==Guid.Empty || actorId==Guid.Empty || request.ExpectedInstanceRevision<1 || request.ExpectedRevision<1 ||
            request.ActionKey is not ("accept_result" or "retry" or "confirm_no_effect") || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length>512 ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) || !System.Text.RegularExpressions.Regex.IsMatch(request.IdempotencyKey,"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$") ||
            request.EvidenceReference?.Length>256 || request.EvidenceReference?.Any(char.IsControl)==true ||
            (request.ActionKey=="accept_result" ? request.AttemptId is null || request.AttemptId==Guid.Empty : request.AttemptId is not null) ||
            request.ActionKey=="confirm_no_effect" && string.IsNullOrWhiteSpace(request.EvidenceReference))
            return Failure<WorkflowConnectorResolutionResponse>(WorkflowErrorCodes.SchemaInvalid,ErrorType.Validation);
        request=request with { Reason=request.Reason.Trim(),EvidenceReference=request.EvidenceReference?.Trim() };
        var scope=WorkflowManagementScope.Resolve(tenant); var hash=RequestHash(instanceId,id,request);
        var instance=await InstanceAsync(instanceId,scope.TenantScopeKey,token).ConfigureAwait(false);
        var call=await CallAsync(id,scope.TenantScopeKey,token).ConfigureAwait(false);
        if (instance is null || call?.InstanceId!=instanceId) return NotFound<WorkflowConnectorResolutionResponse>();
        if (await ReplayAsync(call,actorId,request.IdempotencyKey,hash,token).ConfigureAwait(false) is { } replay) return replay;
        if (await WorkflowTenantFeatureEntitlementGate.TryGetDenialAsync(scope,entitlements,token).ConfigureAwait(false) is { } denial)
            return WorkflowTenantFeatureEntitlementGate.Deny<WorkflowConnectorResolutionResponse>(denial);
        var result=await transaction.ExecuteResultAsync(async inner=>
        {
            var current=await InstanceAsync(instanceId,scope.TenantScopeKey,inner).ConfigureAwait(false);
            if (current is null || await commands.ExecuteAsync(WorkflowConnectorSql.LockInstance,Params(("Id",instanceId),("TenantScopeKey",scope.TenantScopeKey),("Revision",current.Revision)),inner).ConfigureAwait(false)!=1)
                return Conflict();
            var active=await CallAsync(id,scope.TenantScopeKey,inner).ConfigureAwait(false);
            if (active?.InstanceId!=instanceId) return NotFound<WorkflowConnectorResolutionResponse>();
            if (await ReplayAsync(active,actorId,request.IdempotencyKey,hash,inner).ConfigureAwait(false) is { } committed) return committed;
            if (current.Revision!=request.ExpectedInstanceRevision || active.Revision!=request.ExpectedRevision) return Conflict();
            var terminal=current.StatusKey is "completed" or "rejected" or "cancelled";
            if (active.StatusKey is not ("outcome_unknown" or "failed" or "rejected" or "cancelled")) return InvalidResolution();
            var attempts=await query.QueryAsync<WorkflowConnectorAttemptRecord>(WorkflowConnectorSql.ListAttempts,
                Params(("CallId",id),("TenantScopeKey",scope.TenantScopeKey)),inner).ConfigureAwait(false);
            Guid? selected=null; string next;
            if (request.ActionKey=="accept_result")
            {
                if (!terminal && active.CancelRequestedAtUtc is not null) return InvalidResolution();
                var proof=attempts.SingleOrDefault(item=>item.Id==request.AttemptId);
                if (proof is not { StatusKey:"received",OutcomeKey:"succeeded",ResponseJson:{ } body } || proof.RequestHash!=active.RequestHash ||
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)))!=proof.ResponseHash ||
                    terminal && active.SelectedAttemptId==proof.Id) return InvalidResolution();
                selected=proof.Id; next=terminal?"cancelled":"result_ready";
            }
            else if (request.ActionKey=="retry")
            {
                var binding=catalog.Find(scope.TenantScopeKey,active.ConnectorKey,active.ConnectorVersion,active.ConnectorHash);
                var last=attempts.SingleOrDefault(item=>item.Id==active.CurrentAttemptId);
                var knownNotSent=last?.ErrorKey is "workflow.connector.secret_unavailable" or "workflow.connector.input_invalid" or "workflow.connector.destination_denied" or "workflow.connector.cancelled_before_send";
                if (terminal || active.CancelRequestedAtUtc is not null || binding is null || active.AttemptCount>=binding.Definition.MaximumAttempts ||
                    !binding.Definition.SupportsIdempotency && !knownNotSent || string.IsNullOrWhiteSpace(request.EvidenceReference)) return InvalidResolution();
                next="pending";
            }
            else
            {
                if (!terminal || active.AttemptCount==0 || attempts.Any(item=>item.OutcomeKey=="succeeded" || item.HttpStatusCode is >=200 and <300)) return InvalidResolution();
                var latest=(await query.QueryAsync<WorkflowConnectorLatestResolutionRecord>(WorkflowConnectorSql.LatestResolutions,
                    Params(("CallIds",new[]{id}),("TenantScopeKey",scope.TenantScopeKey)),inner).ConfigureAwait(false)).SingleOrDefault();
                if (latest?.ActionKey=="confirm_no_effect") return InvalidResolution();
                next="cancelled";
            }
            var now=clock.UtcNow; var resolutionId=ids.NewId();
            if (await commands.ExecuteAsync(WorkflowConnectorSql.Resolve,Params(("Id",id),("InstanceId",instanceId),("TenantScopeKey",scope.TenantScopeKey),
                    ("Revision",active.Revision),("StatusKey",next),("AttemptId",selected),("Now",now)),inner).ConfigureAwait(false)!=1 ||
                await commands.ExecuteAsync(WorkflowConnectorSql.AdvanceForResolution,Params(("Id",instanceId),("TenantScopeKey",scope.TenantScopeKey),("Revision",current.Revision)),inner).ConfigureAwait(false)!=1) return Conflict();
            var response=new WorkflowConnectorResolutionResponse(resolutionId,id,instanceId,request.ActionKey,next,active.Revision+1,current.StatusKey,current.Revision+1,now);
            await commands.ExecuteAsync(WorkflowConnectorSql.InsertResolution,Params(("Id",resolutionId),("CallId",id),("InstanceId",instanceId),("TenantScopeKey",scope.TenantScopeKey),
                ("ActorUserId",actorId),("ActionKey",request.ActionKey),("AttemptId",selected),("EvidenceReference",request.EvidenceReference),("Reason",request.Reason),
                ("IdempotencyKey",request.IdempotencyKey),("RequestHash",hash),("ResponseJson",JsonSerializer.Serialize(response,WorkflowJsonSerializerContext.Default.WorkflowConnectorResolutionResponse)),("Now",now)),inner).ConfigureAwait(false);
            var detail=WorkflowJsonCanonicalizer.Compile(writer=>{ writer.WriteStartObject(); writer.WriteString("actionKey",request.ActionKey); writer.WriteString("resolutionId",resolutionId);
                writer.WriteString("evidenceReference",request.EvidenceReference); writer.WriteString("reason",request.Reason); writer.WriteString("requestHash",hash); writer.WriteEndObject(); }).CanonicalJson;
            await commands.ExecuteAsync(WorkflowSql.InsertDomainAudit,Params(("Id",ids.NewId()),("TenantId",scope.TenantId),("ScopeKey",scope.ScopeKey),("InstanceId",instanceId),
                ("OperationKey","connector.reconcile"),("ActorUserId",actorId),("ResourceTypeKey","step"),("ResourceId",id),("OutcomeKey","succeeded"),("DetailJson",detail),
                ("CreatedAtUtc",now),("CorrelationJson",LogCorrelationContext.Capture().ToJson())),inner).ConfigureAwait(false);
            return Result<WorkflowConnectorResolutionResponse>.Success(response);
        },token).ConfigureAwait(false);
        if (!result.IsSuccess && await ReplayAsync(call,actorId,request.IdempotencyKey,hash,token).ConfigureAwait(false) is { } raced) return raced;
        return result;
    }

    private async Task<Result<WorkflowConnectorResolutionResponse>?> ReplayAsync(WorkflowConnectorCallRecord call,Guid actor,string key,string hash,CancellationToken token)
    {
        var receipt=await query.QuerySingleOrDefaultAsync<WorkflowConnectorResolutionRecord>(WorkflowConnectorSql.FindResolution,
            Params(("CallId",call.Id),("TenantScopeKey",call.TenantScopeKey),("ActorUserId",actor),("IdempotencyKey",key)),token).ConfigureAwait(false);
        if (receipt is null) return null;
        if (receipt.RequestHash!=hash) return Conflict();
        try { return JsonSerializer.Deserialize(receipt.ResponseJson,WorkflowJsonSerializerContext.Default.WorkflowConnectorResolutionResponse) is { } response
            ? Result<WorkflowConnectorResolutionResponse>.Success(response) : Conflict(); }
        catch (JsonException) { return Conflict(); }
    }
    private static string RequestHash(Guid instance,Guid call,ReconcileWorkflowConnectorRequest request)=>WorkflowJsonCanonicalizer.Compile(writer=>{
        writer.WriteStartObject(); writer.WriteString("instanceId",instance); writer.WriteString("callId",call); writer.WriteNumber("instanceRevision",request.ExpectedInstanceRevision);
        writer.WriteNumber("revision",request.ExpectedRevision); writer.WriteString("actionKey",request.ActionKey); if(request.AttemptId is { } attempt)writer.WriteString("attemptId",attempt);
        writer.WriteString("evidenceReference",request.EvidenceReference); writer.WriteString("reason",request.Reason); writer.WriteEndObject(); }).ContentHash;
    private static Result<WorkflowConnectorResolutionResponse> Conflict()=>Failure<WorkflowConnectorResolutionResponse>(WorkflowErrorCodes.RevisionConflict,ErrorType.Conflict);
    private static Result<WorkflowConnectorResolutionResponse> InvalidResolution()=>Failure<WorkflowConnectorResolutionResponse>(WorkflowErrorCodes.InvalidTransition,ErrorType.Conflict);
}
