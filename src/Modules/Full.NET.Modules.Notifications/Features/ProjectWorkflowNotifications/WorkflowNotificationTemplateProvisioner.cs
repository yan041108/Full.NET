using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;

namespace Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;

/// <summary>在可信事件作用域内幂等补齐工作流内建模板及其首个不可变发布版本。</summary>
/// <param name="queryExecutor">受治理的查询执行器。</param>
/// <param name="commandExecutor">受治理的命令执行器。</param>
/// <param name="transaction">Notifications 本地事务协调器。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">UUID v7 标识生成器。</param>
internal sealed class WorkflowNotificationTemplateProvisioner(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>
    /// 自动模板预置的稳定系统审计主体；该标识不代表真实用户，也不得参与授权判断。
    /// </summary>
    internal static readonly Guid AutomaticProvisionerActorId =
        new("019911b0-7a4d-7d3c-8e5f-607182930001");

    /// <summary>确保指定内建模板存在已发布版本；已存在的人工模板永不被覆盖。</summary>
    /// <param name="scope">由消息 Envelope 派生的可信通知作用域。</param>
    /// <param name="templateKey">闭合目录中的稳定模板键。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    public async Task EnsurePublishedAsync(
        NotificationInboxScope scope,
        string templateKey,
        CancellationToken cancellationToken)
    {
        if (!WorkflowNotificationTemplateCatalog.TryGet(templateKey, out var definition))
        {
            throw new InvalidOperationException(NotificationsErrorCodes.TemplateNotFound);
        }

        var existing = await FindAsync(scope, templateKey, cancellationToken).ConfigureAwait(false);
        if (existing?.LatestPublishedVersionId is not null)
        {
            return;
        }

        // 已有人工草稿时立即失败关闭；常态已发布路径也不会为每条通知额外开启事务。
        if (existing is not null)
        {
            throw new InvalidOperationException(NotificationsErrorCodes.TemplateNotPublished);
        }

        await transaction.ExecuteAsync(
            token => EnsureCoreAsync(scope, definition!, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>在单个本地事务中创建模板、首版快照并更新发布指针。</summary>
    /// <param name="scope">可信通知作用域。</param>
    /// <param name="definition">内建模板定义。</param>
    /// <param name="cancellationToken">事务取消令牌。</param>
    /// <returns>事务完成标记。</returns>
    private async Task<bool> EnsureCoreAsync(
        NotificationInboxScope scope,
        WorkflowNotificationTemplateDefinition definition,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(scope, definition.TemplateKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing?.LatestPublishedVersionId is not null)
        {
            return true;
        }

        // 同名未发布模板可能包含管理员正在编辑的内容，系统不得擅自覆盖或发布。
        if (existing is not null)
        {
            throw new InvalidOperationException(NotificationsErrorCodes.TemplateNotPublished);
        }

        var draft = NotificationTemplateCompiler.NormalizeDraft(
            definition.Subject,
            definition.Body,
            definition.ParameterSchema);
        if (!draft.IsSuccess)
        {
            throw new InvalidOperationException(draft.Error!.Code);
        }

        var now = clock.UtcNow;
        var templateId = idGenerator.NewId();
        var insertTemplate = scope.IsHost
            ? NotificationPlatformSql.InsertTemplateHost
            : NotificationPlatformSql.InsertTemplateTenant;
        var inserted = await commandExecutor.ExecuteAsync(
                insertTemplate,
                NotificationPlatformSqlParameters.Create(
                    ("Id", templateId),
                    ("TenantId", scope.TenantId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("TemplateKey", definition.TemplateKey),
                    ("ChannelKey", NotificationTemplateCompiler.InboxChannelKey),
                    ("ContentCategoryKey", "transactional"),
                    ("DraftSubject", draft.Value!.Subject),
                    ("DraftBodyJson", draft.Value.BodyJson),
                    ("DraftParameterSchemaJson", draft.Value.ParameterSchemaJson),
                    ("CreatedById", AutomaticProvisionerActorId),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (inserted == 0)
        {
            var concurrent = await FindAsync(scope, definition.TemplateKey, cancellationToken)
                .ConfigureAwait(false);
            return concurrent?.LatestPublishedVersionId is not null
                ? true
                : throw new InvalidOperationException(NotificationsErrorCodes.TemplateNotPublished);
        }

        const string classification = "c1";
        var versionId = idGenerator.NewId();
        var contentHash = NotificationTemplateCompiler.ComputeContentHash(
            draft.Value.Subject,
            draft.Value.BodyJson,
            draft.Value.ParameterSchemaJson,
            classification);
        var versionInserted = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.InsertTemplateVersion,
                NotificationPlatformSqlParameters.Create(
                    ("Id", versionId),
                    ("TemplateId", templateId),
                    ("VersionNumber", 1),
                    ("SchemaVersion", NotificationTemplateCompiler.SchemaVersion),
                    ("Subject", draft.Value.Subject),
                    ("BodyJson", draft.Value.BodyJson),
                    ("ParameterSchemaJson", draft.Value.ParameterSchemaJson),
                    ("ContentClassificationKey", classification),
                    ("ContentHash", contentHash),
                    ("PublishedById", AutomaticProvisionerActorId),
                    ("PublishedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (versionInserted == 0)
        {
            throw new InvalidOperationException(NotificationsErrorCodes.TemplateConcurrencyConflict);
        }

        // 发布指针与不可变版本必须原子提交，避免重试观察到系统创建的半成品草稿。
        var published = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.PublishTemplate,
                NotificationPlatformSqlParameters.Create(
                    ("Id", templateId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("LatestPublishedVersionId", versionId),
                    ("UpdatedAtUtc", now),
                    ("NextVersion", 2L),
                    ("Version", 1L)),
                cancellationToken)
            .ConfigureAwait(false);
        if (published == 0)
        {
            throw new InvalidOperationException(NotificationsErrorCodes.TemplateConcurrencyConflict);
        }

        return true;
    }

    /// <summary>按可信作用域和稳定键查询模板。</summary>
    /// <param name="scope">可信通知作用域。</param>
    /// <param name="templateKey">稳定模板键。</param>
    /// <param name="cancellationToken">查询取消令牌。</param>
    /// <returns>模板记录；不存在时返回 <see langword="null"/>。</returns>
    private Task<NotificationTemplateRecord?> FindAsync(
        NotificationInboxScope scope,
        string templateKey,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateRecord>(
            NotificationPlatformSql.FindTemplateByKey,
            NotificationPlatformSqlParameters.Create(
                ("TenantScopeKey", scope.TenantScopeKey),
                ("TemplateKey", templateKey)),
            cancellationToken);
}
