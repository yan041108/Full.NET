using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Execution;

/// <summary>只在目标信封自己的本地作用域收件，不推进源表单，也不切换消费者租户。</summary>
internal sealed class WorkflowCollaborationControlInbox(IIntegrationEventSerializer serializer, IQueryExecutor query,
    ICommandExecutor command, ICurrentTenant tenant, IClock clock)
{
    internal async Task ReceiveAsync(string kind, IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        if (context is null || payload.Length > 1024) throw Invalid();
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (context.MessageId == Guid.Empty || context.MessageType != WorkflowCollaborationControlPolicy.EventType(kind) || context.SchemaVersion != 1 ||
            context.TenantId == Guid.Empty || scope.TenantId != context.TenantId || scope.TenantScopeKey != WorkflowCollaborationGrantPolicy.ScopeKey(context.TenantId)) throw Invalid();
        var value = serializer.Deserialize<WorkflowCollaborationControlIntegrationEvent>(payload);
        if (value is null || value.CallId == Guid.Empty || value.ControlId == Guid.Empty || !WorkflowCollaborationAcceptancePolicy.Digest(value.BindingHash) ||
            !WorkflowCollaborationAcceptancePolicy.Digest(value.BodyHash)) throw Invalid();
        var parameters = WorkflowSqlParameters.Create(("Id", value.CallId), ("KindKey", kind), ("TenantScopeKey", scope.TenantScopeKey),
            ("ControlId", value.ControlId), ("BodyHash", value.BodyHash), ("EnvelopeMessageId", context.MessageId), ("Now", clock.UtcNow));
        var admission = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission, parameters, token).ConfigureAwait(false);
        var row = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationControlRecord>(WorkflowCollaborationControlSql.Find, parameters, token).ConfigureAwait(false);
        if (admission is null || admission.TargetTenantScopeKey != scope.TenantScopeKey || admission.BindingHash != value.BindingHash || row is null ||
            row.Id != value.ControlId || row.BodyHash != value.BodyHash || !WorkflowCollaborationControlPolicy.TryRead(row, admission, out _)) throw Invalid();
        if (row.StatusKey is "received" or "applied") { if (row.EnvelopeMessageId is null || row.ReceivedAtUtc is null) throw Invalid(); return; }
        if (row.StatusKey != "published") throw Invalid();
        try { if (await command.ExecuteAsync(WorkflowCollaborationControlSql.Receive, parameters, token).ConfigureAwait(false) == 1) return; }
        catch (DataCommandException error) when (error.Kind == DataCommandFailureKind.UniqueConstraint) { }
        // 并发不同物理投递保留首次信封；种类、正文及逻辑身份必须完全一致。
        var received = await query.QuerySingleOrDefaultAsync<WorkflowCollaborationControlRecord>(WorkflowCollaborationControlSql.Find, parameters, token).ConfigureAwait(false);
        if (received is null || received.Id != row.Id || received.BodyHash != row.BodyHash || received.StatusKey is not ("received" or "applied") ||
            received.EnvelopeMessageId is null || received.ReceivedAtUtc is null) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("workflow.collaboration.control_invalid");
}

internal abstract class WorkflowCollaborationControlHandler(WorkflowCollaborationControlInbox inbox, string kind) : IIntegrationEventHandler
{
    public string EventType => WorkflowCollaborationControlPolicy.EventType(kind);
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken token) => inbox.ReceiveAsync(kind, context, payload, token);
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken token) => throw new InvalidDataException("workflow.collaboration.control_invalid");
}
internal sealed class WorkflowCollaborationCreatedHandler(WorkflowCollaborationControlInbox inbox) : WorkflowCollaborationControlHandler(inbox, "created");
internal sealed class WorkflowCollaborationCompletedHandler(WorkflowCollaborationControlInbox inbox) : WorkflowCollaborationControlHandler(inbox, "completed");
internal sealed class WorkflowCollaborationCancelledHandler(WorkflowCollaborationControlInbox inbox) : WorkflowCollaborationControlHandler(inbox, "cancelled");
