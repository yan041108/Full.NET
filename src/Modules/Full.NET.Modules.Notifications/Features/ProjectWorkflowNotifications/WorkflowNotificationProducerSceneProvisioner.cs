using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.ManageProducerScenes;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>
/// 在可信事件作用域内幂等补齐工作流内建 Producer/Scene 目录声明。
/// </summary>
/// <remarks>
/// 受理链路对“目录缺失或停用”失败关闭，因此系统内建生产者必须随内建模板一起预置声明，
/// 否则 Workflow 可靠提醒在全新环境会因缺少运维手工登记而整体停摆。
/// 预置只创建缺失行，已存在的人工声明（含被停用的声明）永不覆盖或重新启用。
/// </remarks>
/// <param name="queryExecutor">受治理的查询执行器。</param>
/// <param name="commandExecutor">受治理的命令执行器。</param>
/// <param name="transaction">Notifications 本地事务协调器。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
internal sealed class WorkflowNotificationProducerSceneProvisioner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>系统内建生产者稳定键，与 <see cref="WorkflowNotificationRequestFactory"/> 保持一致。</summary>
    private const string ProducerKey = "workflow";

    /// <summary>内建提醒只投递站内信，外部渠道必须由运维显式声明后才会被目录允许。</summary>
    private static readonly string[] AllowedChannels = [Domain.NotificationTemplateCompiler.InboxChannelKey];

    /// <summary>确保指定内建场景存在目录声明；已存在的声明不做任何修改。</summary>
    /// <param name="scope">由消息 Envelope 派生的可信通知作用域。</param>
    /// <param name="sceneKey">闭合目录中的稳定场景键（与内建模板键一致）。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    public async Task EnsureAsync(
        NotificationInboxScope scope,
        string sceneKey,
        CancellationToken cancellationToken)
    {
        if (!WorkflowNotificationTemplateCatalog.TryGet(sceneKey, out _))
        {
            throw new InvalidOperationException(NotificationsErrorCodes.ProducerSceneMissing);
        }

        var existing = await FindAsync(scope, sceneKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        await transaction.ExecuteAsync(
            token => EnsureCoreAsync(scope, sceneKey, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>在单个本地事务中按条件插入缺失的目录声明。</summary>
    /// <param name="scope">可信通知作用域。</param>
    /// <param name="sceneKey">稳定场景键。</param>
    /// <param name="cancellationToken">事务取消令牌。</param>
    /// <returns>事务完成标记。</returns>
    private async Task<bool> EnsureCoreAsync(
        NotificationInboxScope scope,
        string sceneKey,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(scope, sceneKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return true;
        }

        var now = clock.UtcNow;
        var insert = scope.IsHost
            ? NotificationPlatformSql.InsertProducerSceneHost
            : NotificationPlatformSql.InsertProducerSceneTenant;
        var inserted = await commandExecutor.ExecuteAsync(
                insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("TenantId", scope.TenantId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ProducerKey", ProducerKey),
                    ("SceneKey", sceneKey),
                    // 审批待办属于交易类业务通知：不绕过渠道与费用政策，但也不会被普通营销偏好提升或降级。
                    ("PolicyCategoryKey", NotificationProducerSceneCatalog.CategoryTransactional),
                    ("AllowedChannelKeysJson", NotificationProducerSceneCatalog.SerializeChannels(AllowedChannels)),
                    ("EmergencyOverrideAllowed", false),
                    ("CreatedById", WorkflowNotificationTemplateProvisioner.AutomaticProvisionerActorId),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (inserted == 0)
        {
            // 并发预置由 NOT EXISTS 条件插入兜底，这里只需确认对手已写入有效声明。
            var concurrent = await FindAsync(scope, sceneKey, cancellationToken).ConfigureAwait(false);
            if (concurrent is null)
            {
                throw new InvalidOperationException(NotificationsErrorCodes.ProducerSceneKeyConflict);
            }
        }

        return true;
    }

    /// <summary>按可信作用域、内建生产者与场景键查询目录声明。</summary>
    /// <param name="scope">可信通知作用域。</param>
    /// <param name="sceneKey">稳定场景键。</param>
    /// <param name="cancellationToken">查询取消令牌。</param>
    /// <returns>目录记录；不存在时返回 <see langword="null"/>。</returns>
    private Task<NotificationProducerSceneRecord?> FindAsync(
        NotificationInboxScope scope,
        string sceneKey,
        CancellationToken cancellationToken) =>
        NotificationProducerSceneCatalog.FindAsync(
            queryExecutor,
            scope.TenantScopeKey,
            ProducerKey,
            sceneKey,
            cancellationToken);
}
