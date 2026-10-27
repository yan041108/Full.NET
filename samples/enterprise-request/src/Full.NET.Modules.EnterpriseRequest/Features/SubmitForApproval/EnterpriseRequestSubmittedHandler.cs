using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using MemoryPack;

namespace Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;

/// <summary>提交时冻结目标流程版本，重投递不能换用后来发布的定义。</summary>
[MemoryPackable]
internal sealed partial record EnterpriseRequestSubmitted(Guid RequestId, Guid ActorUserId,
    Guid DefinitionVersionId, long Version, string Title);

/// <summary>使用稳定业务幂等键启动 Workflow；失败抛出交由 Outbox 重试及死信恢复。</summary>
internal sealed class EnterpriseRequestSubmittedHandler(IIntegrationEventSerializer serializer,
    IWorkflowInstanceStarter starter, ICurrentTenant tenant) : IIntegrationEventHandler
{
    internal const string MessageType = "demo.enterprise_request.request.submitted";
    public string EventType => MessageType;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public async Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (context.TenantId is null || tenant.IsHost || tenant.Id != context.TenantId)
            throw new InvalidOperationException("enterprise_request.tenant_context_required");
        var intent = serializer.Deserialize<EnterpriseRequestSubmitted>(payload);
        var result = await starter.StartAsync(intent.ActorUserId,
            new StartWorkflowInstanceCommand(intent.DefinitionVersionId, EnterpriseRequestWorkflowConstants.BusinessType,
                intent.RequestId.ToString("D"), "{}", $"submit:{intent.RequestId:D}:{intent.Version}", intent.Title), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess) throw new InvalidOperationException(EnterpriseRequestWorkflowErrorCodes.WorkflowStartFailed);
    }
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("enterprise_request.message_context_required");
}
