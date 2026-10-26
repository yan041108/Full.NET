using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>请求和补偿都与Workflow本地调用事实共同提交；租户由当前可信上下文写入Envelope。</summary>
internal sealed class WorkflowBusinessCommandPublisher(IOutboxWriter outbox)
{
    public Task RequestAsync(WorkflowBusinessCommandBinding binding, string hash, string phase, int generation, DateTimeOffset now, CancellationToken token) =>
        outbox.AddAsync(WorkflowBusinessCommandIntegrationEventTypes.Requested, 1,
            new WorkflowBusinessCommandRequestedIntegrationEvent(binding, hash, phase, generation, now),
            IntegrationEventMetadata.Create(binding.InstanceId.ToString("D"), "fullnet.workflow"), token);
}
