using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution.Connectors;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Workflow.Features.ManageConnectorCalls;

/// <summary>连接器运维只读取当前作用域；对账权限与普通实例读取/恢复权限分别声明。</summary>
internal sealed partial class WorkflowConnectorManagementService(IQueryExecutor query,ICommandExecutor commands,ICommandTransaction transaction,
    ICurrentTenant tenant,IClock clock,IIdGenerator ids,IOptions<DatabaseOptions> database,WorkflowConnectorCatalog catalog,
    ITenantFeatureEntitlementPort entitlements)
{
    public WorkflowConnectorCatalogResponse Catalog()
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        static WorkflowConnectorParameterResponse[] Parameters(WorkflowFormSchema schema) => schema.Sections.SelectMany(section => section.Fields)
            .Select(field => new WorkflowConnectorParameterResponse(field.FieldKey,field.FieldTypeKey,field.Required,field.Constraints)).ToArray();
        return new(catalog.ForScope(scope.TenantScopeKey).Select(item => new WorkflowConnectorCatalogItemResponse(item.Definition.ConnectorKey,
            item.Definition.Version,item.Definition.SupportsIdempotency,Parameters(item.Definition.RequestSchema),Parameters(item.Definition.ResponseSchema))).ToArray());
    }

    public async Task<Result<WorkflowConnectorCallPageResponse>> ListAsync(Guid instanceId,int page,int pageSize,CancellationToken token)
    {
        if (instanceId == Guid.Empty || page < 1 || pageSize is < 1 or > 20 || (long)(page-1)*pageSize > int.MaxValue) return Failure<WorkflowConnectorCallPageResponse>(WorkflowErrorCodes.SchemaInvalid,ErrorType.Validation);
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (await InstanceAsync(instanceId,scope.TenantScopeKey,token).ConfigureAwait(false) is null) return NotFound<WorkflowConnectorCallPageResponse>();
        var parameters = Params(("InstanceId",instanceId),("TenantScopeKey",scope.TenantScopeKey),("Offset",(page-1)*pageSize),("Take",pageSize));
        var rows = await query.QueryAsync<WorkflowConnectorCallRecord>(database.Value.Provider == DatabaseProvider.SqlServer ? WorkflowConnectorSql.ListForInstanceSqlServer : WorkflowConnectorSql.ListForInstanceMySql,parameters,token).ConfigureAwait(false);
        var total = await query.QuerySingleOrDefaultAsync<int>(WorkflowConnectorSql.CountForInstance,parameters,token).ConfigureAwait(false);
        if (rows.Count == 0) return Result<WorkflowConnectorCallPageResponse>.Success(new([],page,pageSize,total));
        var batch = Params(("CallIds",rows.Select(row => row.Id).ToArray()),("TenantScopeKey",scope.TenantScopeKey));
        var attempts = await query.QueryAsync<WorkflowConnectorAttemptSummaryRecord>(WorkflowConnectorSql.ListAttemptSummaries,batch,token).ConfigureAwait(false);
        var resolutions = await query.QueryAsync<WorkflowConnectorLatestResolutionRecord>(WorkflowConnectorSql.LatestResolutions,batch,token).ConfigureAwait(false);
        return Result<WorkflowConnectorCallPageResponse>.Success(new(rows.Select(row => Map(row,attempts.Where(item => item.CallId==row.Id).ToArray(),
            resolutions.FirstOrDefault(item=>item.CallId==row.Id))).ToArray(),page,pageSize,total));
    }

    public async Task<Result<WorkflowConnectorCallResponse>> GetAsync(Guid instanceId,Guid id,CancellationToken token)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        var call = await CallAsync(id,scope.TenantScopeKey,token).ConfigureAwait(false);
        if (call?.InstanceId != instanceId || await InstanceAsync(instanceId,scope.TenantScopeKey,token).ConfigureAwait(false) is null) return NotFound<WorkflowConnectorCallResponse>();
        var batch = Params(("CallIds",new[]{id}),("TenantScopeKey",scope.TenantScopeKey));
        var attempts = await query.QueryAsync<WorkflowConnectorAttemptSummaryRecord>(WorkflowConnectorSql.ListAttemptSummaries,batch,token).ConfigureAwait(false);
        var resolution = (await query.QueryAsync<WorkflowConnectorLatestResolutionRecord>(WorkflowConnectorSql.LatestResolutions,batch,token).ConfigureAwait(false)).SingleOrDefault();
        return Result<WorkflowConnectorCallResponse>.Success(Map(call,attempts,resolution));
    }

    private static WorkflowConnectorCallResponse Map(WorkflowConnectorCallRecord call,IReadOnlyList<WorkflowConnectorAttemptSummaryRecord> attempts,
        WorkflowConnectorLatestResolutionRecord? resolution) => new(call.Id,call.InstanceId,call.NodeKey,call.ConnectorKey,call.ConnectorVersion,call.ModeKey,
        call.StatusKey,call.Revision,call.AttemptCount,call.ApplyAttemptCount,call.LastErrorKey,call.InputFormRevision,call.AppliedFormRevision,
        call.CancelRequestedAtUtc is not null,RequiresReconciliation(call,attempts,resolution),call.CreatedAtUtc,call.CompletedAtUtc,
        attempts.Select(item=>new WorkflowConnectorAttemptResponse(item.Id,item.LeaseGeneration,item.StatusKey,item.OutcomeKey,item.HttpStatusCode,item.ErrorKey,
            item.ResponseHash,item.StartedAtUtc,item.CompletedAtUtc)).ToArray());
    private static bool RequiresReconciliation(WorkflowConnectorCallRecord call,IReadOnlyList<WorkflowConnectorAttemptSummaryRecord> attempts,
        WorkflowConnectorLatestResolutionRecord? resolution)
    {
        if (call.StatusKey is "outcome_unknown" or "failed" or "rejected") return true;
        if (call.CancelRequestedAtUtc is null || call.AttemptCount==0) return false;
        if (attempts.Any(item=>item.OutcomeKey=="succeeded" || item.HttpStatusCode is >=200 and <300)) return true;
        return resolution?.ActionKey != "confirm_no_effect" && attempts.Any(item=>item.StatusKey=="inflight" || item.OutcomeKey is "outcome_unknown" or "retryable");
    }
    private Task<WorkflowInstanceRecord?> InstanceAsync(Guid id,string scope,CancellationToken token) => query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById,Params(("Id",id),("TenantScopeKey",scope)),token);
    private Task<WorkflowConnectorCallRecord?> CallAsync(Guid id,string scope,CancellationToken token) => query.QuerySingleOrDefaultAsync<WorkflowConnectorCallRecord>(WorkflowConnectorSql.Find,Params(("Id",id),("TenantScopeKey",scope)),token);
    private static IReadOnlyDictionary<string,object?> Params(params (string Key,object? Value)[] values) => WorkflowSqlParameters.Create(values);
    private static Result<T> Failure<T>(string code,ErrorType type) => Result<T>.Failure(new Error(code,"Workflow connector operation failed.",type));
    private static Result<T> NotFound<T>() => Failure<T>(WorkflowErrorCodes.InstanceNotFound,ErrorType.NotFound);
}
