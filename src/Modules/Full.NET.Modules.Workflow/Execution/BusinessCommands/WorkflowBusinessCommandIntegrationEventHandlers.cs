using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Execution.BusinessCommands;

/// <summary>按已注册真实所有者路由；不持有Workflow事务，不把无人消费的命令当作成功。</summary>
internal sealed class WorkflowBusinessCommandRequestedHandler(IIntegrationEventSerializer serializer,
    IEnumerable<IWorkflowBusinessCommandSink> sinks) : IIntegrationEventHandler
{
    public string EventType => WorkflowBusinessCommandIntegrationEventTypes.Requested;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        var value = serializer.Deserialize<WorkflowBusinessCommandRequestedIntegrationEvent>(payload);
        var sink = sinks.SingleOrDefault(item => item.OwnerKey == value.Binding.OwnerKey) ?? throw new InvalidOperationException("workflow.business_command.owner_unavailable");
        return sink.HandleAsync(context, value, token);
    }
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken token) => throw new InvalidOperationException("workflow.business_command.context_required");
}

/// <summary>所有者结果只通过固定协议和可信消息作用域接纳。</summary>
internal sealed class WorkflowBusinessCommandResultHandler(IIntegrationEventSerializer serializer,
    WorkflowBusinessCommandResultService results) : IIntegrationEventHandler
{
    public string EventType => WorkflowBusinessCommandIntegrationEventTypes.Result;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken token) =>
        results.AcceptAsync(context.TenantId, serializer.Deserialize<WorkflowBusinessCommandResultIntegrationEvent>(payload), token);
    public Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken token) => throw new InvalidOperationException("workflow.business_command.context_required");
}
