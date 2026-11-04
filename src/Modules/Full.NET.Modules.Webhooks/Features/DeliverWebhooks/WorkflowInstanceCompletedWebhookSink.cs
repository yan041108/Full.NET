using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Webhooks.Features.DeliverWebhooks;

/// <summary>通过 Workflow 的单一完成事件路由接收事实，Webhooks 独立事务幂等入队，不争抢 Outbox 处理所有权。</summary>
internal sealed class WorkflowInstanceCompletedWebhookSink(WebhookEventEnqueueService enqueueService,
    ICommandTransaction transaction,ICurrentTenantContextWriter tenantWriter):IWorkflowInstanceCompletedSink
{
    /// <inheritdoc />
    public async Task HandleAsync(IntegrationEventContext context,WorkflowInstanceCompletedIntegrationEvent integrationEvent,CancellationToken token)
    {
        // Host 流程没有企业订阅；空 UUID 和不匹配的既有企业作用域不能被当成可信路由。
        if(context.TenantId is null)return;
        var tenantId=context.TenantId.Value;
        if(tenantId==Guid.Empty||context.MessageId==Guid.Empty||tenantWriter.IsAvailable&&!tenantWriter.IsHost&&tenantWriter.Id!=tenantId)
            throw new InvalidOperationException("webhooks.workflow_tenant_context_invalid");
        var wasHost=tenantWriter.IsHost;
        var previous=!wasHost&&tenantWriter.IsAvailable&&tenantWriter.Id is { } id
            ?new TenantContext(id,tenantWriter.Identifier??id.ToString("D"),tenantWriter.Name??id.ToString("D")):null;
        var envelope=new WebhookEventEnvelope(WorkflowNotificationIntegrationEventTypes.InstanceCompleted,context.MessageId,tenantId,integrationEvent.OccurredAtUtc,
            new(integrationEvent.InstanceId,integrationEvent.RecipientUserId,integrationEvent.BusinessType,integrationEvent.BusinessId));
        try
        {
            // 仅此受审查的后台边界按已验证 Outbox 企业执行现有 Host 队列 SQL；不接收客户端企业参数。
            // 各 Sink 分别维护本地事务，其他模块失败后重投仍以原 MessageId 和订阅唯一键去重。
            tenantWriter.SetHost();
            await transaction.ExecuteAsync(async cancellation=>{await enqueueService.EnqueueAsync(tenantId,envelope.EventType,context.MessageId,envelope,cancellation).ConfigureAwait(false);return 0;},token).ConfigureAwait(false);
        }
        finally{if(wasHost)tenantWriter.SetHost();else if(previous is not null)tenantWriter.SetTenant(previous);else tenantWriter.Clear();}
    }
}
