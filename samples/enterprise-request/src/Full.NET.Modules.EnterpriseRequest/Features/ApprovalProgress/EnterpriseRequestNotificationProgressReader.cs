using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.ApprovalProgress;

/// <summary>只能在单据及提交回执已获授权之后读取对应终态提醒。</summary>
internal sealed class EnterpriseRequestNotificationProgressReader(
    INotificationIntentDeliveryDirectory notifications, ICurrentTenant tenant)
{
    public async Task<NotificationIntentDeliverySnapshot?> GetAsync(
        EnterpriseRequestApprovalSubmission submission, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tenant.IsHost || !tenant.IsAvailable || tenant.Id is not { } tenantId ||
            tenantId == Guid.Empty || submission.TenantId != tenantId)
            throw new InvalidOperationException("The trusted request scope is unavailable.");
        if (submission.FinalStatus is null) return null;
        if (submission.FinalStatus is not ("Approved" or "Rejected" or "Cancelled") ||
            submission.CompletedAtUtc is null || submission.LastMessageId is not { } messageId || messageId == Guid.Empty)
            throw new InvalidOperationException("The request outcome receipt is incomplete.");
        // 只追踪业务模块已经封存的终态事件，不猜测启动事件，也不读通知表。
        return await notifications.FindByIdempotencyAsync("workflow", $"workflow-{messageId:N}", cancellationToken).ConfigureAwait(false);
    }
}
