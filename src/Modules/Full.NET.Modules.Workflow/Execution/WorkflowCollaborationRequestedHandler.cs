using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>自然幂等收件只写信封自己的源作用域；不调用目标启动，不借用消费者事务切换租户。</summary>
internal sealed class WorkflowCollaborationRequestedHandler(IIntegrationEventSerializer serializer, IQueryExecutor query,
    ICommandExecutor command, IClock clock, ICurrentTenant tenant) : IIntegrationEventHandler
{
    public string EventType => WorkflowCollaborationIntegrationEventTypes.Requested;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        if (context is null || payload.Length > 1024 || context.MessageId == Guid.Empty || context.TenantId == Guid.Empty || context.MessageType != EventType || context.SchemaVersion != SchemaVersion)
            throw Invalid();
        if (tenant.IsHost ? context.TenantId is not null : !tenant.IsAvailable || tenant.Id is null || tenant.Id != context.TenantId) throw Invalid();
        var value = serializer.Deserialize<WorkflowCollaborationRequestedIntegrationEvent>(payload);
        if (value is null || value.CallId == Guid.Empty || value.DeliveryId == Guid.Empty || !WorkflowCollaborationAcceptancePolicy.Digest(value.BindingHash)) throw Invalid();
        var scope = WorkflowCollaborationGrantPolicy.ScopeKey(context.TenantId);
        var parameters = WorkflowSqlParameters.Create(("Id", value.CallId), ("DeliveryId", value.DeliveryId), ("TenantScopeKey", scope),
            ("EnvelopeMessageId", context.MessageId), ("BindingHash", value.BindingHash), ("Now", clock.UtcNow));
        var call = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationCallRecord>(WorkflowCollaborationCallSql.Find, parameters, token).ConfigureAwait(false);
        if (call is null || call.StatusKey == "preparing" || call.DeliveryId != value.DeliveryId || call.BindingHash != value.BindingHash || call.BindingJson.Length > 16_384 ||
            call.InputJson.Length > 131_072 || call.ProjectionJson.Length > 131_072 ||
            WorkflowCollaborationCallPolicy.Hash(call.BindingJson) != call.BindingHash ||
            WorkflowCollaborationCallPolicy.Hash(call.InputJson) != call.InputHash || WorkflowCollaborationCallPolicy.Hash(call.ProjectionJson) != call.ProjectionHash) throw Invalid();
        WorkflowCollaborationCallBinding? binding;
        try
        {
            using var document = JsonDocument.Parse(call.BindingJson);
            if (!WorkflowVotePolicy.UniqueObject(document.RootElement) || document.RootElement.EnumerateObject().Count() != 24) throw Invalid();
            binding = JsonSerializer.Deserialize(document.RootElement, WorkflowJsonSerializerContext.Default.WorkflowCollaborationCallBinding);
        }
        catch (JsonException) { throw Invalid(); }
        if (binding is null || binding.SourceTenantId != context.TenantId || binding.SourceTenantScopeKey != scope ||
            binding.SourceTenantScopeKey == binding.TargetTenantScopeKey || binding.TargetTenantId == Guid.Empty ||
            binding.TargetTenantScopeKey != WorkflowCollaborationGrantPolicy.ScopeKey(binding.TargetTenantId) ||
            binding.SourceDefinitionVersionId == Guid.Empty || binding.SourceFormVersionId == Guid.Empty || binding.TargetDefinitionVersionId == Guid.Empty ||
            binding.GrantRevision != 2 || !WorkflowCollaborationAcceptancePolicy.Digest(binding.PolicyHash) ||
            !WorkflowCollaborationAcceptancePolicy.Digest(binding.TargetSummaryHash) || binding.Depth is < 1 or > 8 || binding.ReservedActivities is < 2 or > 4096 ||
            !WorkflowNodeKeyValidator.IsValid(binding.NodeKey) || !WorkflowNodeKeyValidator.IsValid(binding.ContinuationNodeKey) ||
            binding.ParallelJoinId == Guid.Empty || (binding.ParallelJoinId is null) != (binding.ParallelBranchKey is null) ||
            binding.CallId != call.Id || binding.DeliveryId != call.DeliveryId || binding.SourceInstanceId != call.InstanceId ||
            binding.ExecutionEpochId != call.ExecutionEpochId || binding.NodeKey != call.NodeKey || binding.ContinuationNodeKey != call.ContinuationNodeKey ||
            binding.GrantId != call.GrantId || binding.InputHash != call.InputHash || binding.InputFormRevision != call.InputFormRevision ||
            binding.ProjectionHash != call.ProjectionHash || binding.ReservedActivities != call.ReservedActivities) throw Invalid();
        var existing = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationDeliveryRecord>(WorkflowCollaborationCallSql.FindDelivery, parameters, token).ConfigureAwait(false);
        if (existing is not null) { Verify(existing, value, scope); return; }
        try
        {
            if (await command.ExecuteAsync(WorkflowCollaborationCallSql.InsertDelivery, parameters, token).ConfigureAwait(false) != 1) throw Invalid();
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 竞争接收同一逻辑请求可以收敛；重用物理消息身份指向另一调用必须失败。
            var received = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationDeliveryRecord>(WorkflowCollaborationCallSql.FindDelivery, parameters, token).ConfigureAwait(false);
            if (received is null) throw Invalid();
            Verify(received, value, scope);
        }
    }

    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken token) => throw Invalid();

    private static void Verify(WorkflowCollaborationDeliveryRecord receipt, WorkflowCollaborationRequestedIntegrationEvent value, string scope)
    {
        if (receipt.Id != value.DeliveryId || receipt.CallId != value.CallId || receipt.TenantScopeKey != scope || receipt.BindingHash != value.BindingHash) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("workflow.collaboration.delivery_invalid");
}
