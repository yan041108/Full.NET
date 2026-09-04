using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.CreateNotificationIntents;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>在可信事件作用域内调用既有通知 Intent 管道。</summary>
/// <param name="templateProvisioner">工作流内建模板幂等补齐服务。</param>
/// <param name="intentService">通知意图受理服务。</param>
internal sealed class WorkflowNotificationProjectionService(
    WorkflowNotificationTemplateProvisioner templateProvisioner,
    NotificationIntentService intentService)
{
    /// <summary>投影 Workflow 提醒；业务失败转为消息处理失败以进入 Outbox 重试或死信。</summary>
    /// <param name="tenantId">Envelope 中的可信租户标识；为空表示 Host。</param>
    /// <param name="actorUserId">作为通知创建主体的受信 Workflow 用户。</param>
    /// <param name="request">已闭合映射的通知意图。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    public async Task ProjectAsync(
        Guid? tenantId,
        Guid actorUserId,
        CreateNotificationIntentRequest request,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.FromTrustedTenantId(tenantId);
        // 内建模板先在独立本地事务中形成完整发布版本；成功后才允许 Intent 固定该不可变版本。
        await templateProvisioner.EnsurePublishedAsync(
            scope,
            actorUserId,
            request.TemplateKey,
            cancellationToken).ConfigureAwait(false);
        var result = await intentService.CreateForTrustedEventAsync(
            scope,
            actorUserId,
            request,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error!.Code);
        }
    }
}
