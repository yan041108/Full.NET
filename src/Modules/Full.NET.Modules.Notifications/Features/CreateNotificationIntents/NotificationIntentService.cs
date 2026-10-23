using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Localization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Features.ManageTemplates;
using Full.NET.Modules.Notifications.Features.ProjectInboxFromIntent;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Notifications.Features.CreateNotificationIntents;

/// <summary>
/// 受理通知意图：事务外解析用户目录，事务内只写 Notifications 表并投影内建 Inbox。
/// </summary>
/// <remarks>
/// 本切片在 inbox 渠道不要求 Binding；外部渠道必须固定已发布 BindingVersion 与 ProfileVersion。
/// 没有重要跨模块事实时不为 Intent 本身写 Outbox；Inbox 实时与手工发信同类。
/// </remarks>
internal sealed class NotificationIntentService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOutboxWriter outboxWriter,
    NotificationRecipientDirectoryResolver recipientDirectory,
    NotificationTemplateSelector templateSelector,
    Features.IntentAttachments.NotificationIntentAttachmentCoordinator attachmentCoordinator,
    ICurrentTenant currentTenant,
    InboxIntentProjectionService inboxProjection,
    NotificationRealtimeDelivery realtimeDelivery,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<NotificationIntentService> logger)
{
    public async Task<Result<NotificationIntentResponse>> GetByIdAsync(
        Guid intentId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var record = await FindIntentAsync(scope, intentId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<NotificationIntentResponse>.Success(
                await MapAsync(record, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>使用当前可信请求作用域创建通知意图。</summary>
    /// <param name="actorUserId">当前操作用户标识。</param>
    /// <param name="request">模板、场景、收件人和幂等请求。</param>
    /// <param name="cancellationToken">取消当前异步操作的令牌。</param>
    /// <returns>首次创建或幂等回放结果。</returns>
    public async Task<Result<NotificationIntentCreateResult>> CreateAsync(
        Guid actorUserId,
        CreateNotificationIntentRequest request,
        CancellationToken cancellationToken)
    {
        return await CreateForTrustedEventAsync(
            NotificationInboxScope.Resolve(currentTenant),
            actorUserId,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>在调用方提供的可信事件作用域内创建通知意图。</summary>
    /// <param name="scope">由消息 Envelope 构造的可信通知作用域。</param>
    /// <param name="actorUserId">创建通知的受信业务主体。</param>
    /// <param name="request">模板化通知意图请求。</param>
    /// <param name="cancellationToken">消息租约取消令牌。</param>
    /// <returns>首次创建或幂等回放结果。</returns>
    internal async Task<Result<NotificationIntentCreateResult>> CreateForTrustedEventAsync(
        NotificationInboxScope scope,
        Guid actorUserId,
        CreateNotificationIntentRequest request,
        CancellationToken cancellationToken)
    {
        var producer = NotificationTemplateCompiler.NormalizeStableKey(request.ProducerKey, "ProducerKey");
        var idempotency = NotificationTemplateCompiler.NormalizeStableKey(request.IdempotencyKey, "IdempotencyKey");
        if (!producer.IsSuccess || !idempotency.IsSuccess)
        {
            return Result<NotificationIntentCreateResult>.Failure(producer.Error ?? idempotency.Error!);
        }

        // 幂等请求先读取已受理快照，当前目录、模板或路由变化不得重新解释历史请求。
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<NotificationIntentRecord>(
            NotificationPlatformSql.FindIntentByIdempotency,
            NotificationPlatformSqlParameters.Create(
                ("TenantScopeKey", scope.TenantScopeKey), ("ProducerKey", producer.Value!),
                ("IdempotencyKey", idempotency.Value!)), cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return await ReplayOrConflictAsync(existing, request, cancellationToken).ConfigureAwait(false);
        }

        var prepared = await PrepareAsync(scope, actorUserId, request, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return Result<NotificationIntentCreateResult>.Failure(prepared.Error!);
        }

        var result = await transaction.ExecuteResultAsync(
                token => CreateCoreAsync(scope, actorUserId, request, prepared.Value!, token),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await TryPublishInboxAsync(result.Value!.InboxEvents, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task<Result<PreparedIntent>> PrepareAsync(
        NotificationInboxScope scope,
        Guid actorUserId,
        CreateNotificationIntentRequest request,
        CancellationToken cancellationToken)
    {
        if (!NotificationMessageMetadata.IsValid(request.DeepLink)
            || request.ExpiresAtUtc is { } expiry && (expiry <= clock.UtcNow || expiry > clock.UtcNow.AddDays(30)))
            return Result<PreparedIntent>.Failure(new Error(NotificationsErrorCodes.TemplateValidationFailed,
                "The notification expiry or deep link is invalid.", ErrorType.Validation));
        var producer = NotificationTemplateCompiler.NormalizeStableKey(request.ProducerKey, "ProducerKey");
        var scene = NotificationTemplateCompiler.NormalizeStableKey(request.SceneKey, "SceneKey");
        var templateKey = NotificationTemplateCompiler.NormalizeStableKey(request.TemplateKey, "TemplateKey");
        var idempotency = NotificationTemplateCompiler.NormalizeStableKey(request.IdempotencyKey, "IdempotencyKey");
        if (!producer.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(producer.Error!);
        }

        if (!scene.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(scene.Error!);
        }

        if (!templateKey.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(templateKey.Error!);
        }

        if (!idempotency.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(idempotency.Error!);
        }

        var recipients = NormalizeRecipients(request.Recipients);
        if (!recipients.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(recipients.Error!);
        }

        var templateSelection = await templateSelector
            .ResolvePublishedAsync(
                scope,
                templateKey.Value!,
                LocaleCatalog.DefaultLocale,
                cancellationToken)
            .ConfigureAwait(false);
        if (!templateSelection.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(templateSelection.Error!);
        }

        var template = templateSelection.Value!.Template;
        var version = templateSelection.Value.Version;

        var schema = NotificationTemplateCompiler.NormalizeSchema(
            DeserializeSchema(version.ParameterSchemaJson));
        if (!schema.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(schema.Error!);
        }

        var snapshot = NotificationTemplateCompiler.ValidateAndSnapshotParameters(
            schema.Value!,
            request.Parameters);
        if (!snapshot.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(snapshot.Error!);
        }

        var normalizedRecipients = recipients.Value!;
        var recipientUsers = await recipientDirectory
            .ResolveAsync(scope, normalizedRecipients, cancellationToken)
            .ConfigureAwait(false);
        if (!recipientUsers.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(recipientUsers.Error!);
        }

        var resolved = recipientUsers.Value!;

        var route = await ResolveRouteAsync(
                scope,
                producer.Value!,
                scene.Value!,
                template.ChannelKey,
                snapshot.Value!,
                cancellationToken)
            .ConfigureAwait(false);
        if (!route.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(route.Error!);
        }

        // 分类、合法渠道与紧急规则只能来自受控目录；缺失或停用时失败关闭，禁止由可编辑模板提升为强制通知。
        var catalog = await ManageProducerScenes.NotificationProducerSceneCatalog.FindAsync(
                queryExecutor,
                scope.TenantScopeKey,
                producer.Value!,
                scene.Value!,
                cancellationToken)
            .ConfigureAwait(false);
        if (catalog is null || !catalog.IsEnabled)
        {
            return Result<PreparedIntent>.Failure(new Error(
                NotificationsErrorCodes.ProducerSceneMissing,
                "The producer/scene is not declared or is disabled in the controlled catalog.",
                ErrorType.BusinessRule));
        }

        if (!ManageProducerScenes.NotificationProducerSceneCatalog.IsChannelAllowed(catalog, template.ChannelKey))
        {
            return Result<PreparedIntent>.Failure(new Error(
                NotificationsErrorCodes.ProducerSceneChannelNotAllowed,
                "The channel is not allowed for this producer/scene.",
                ErrorType.BusinessRule));
        }

        var attachments = await attachmentCoordinator
            .ValidateAsync(actorUserId, template.ChannelKey, request.AttachmentFileIds, cancellationToken)
            .ConfigureAwait(false);
        if (!attachments.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(attachments.Error!);
        }

        return Result<PreparedIntent>.Success(new PreparedIntent(
            producer.Value!,
            scene.Value!,
            idempotency.Value!,
            catalog.PolicyCategoryKey,
            catalog.EmergencyOverrideAllowed,
            template,
            version,
            snapshot.Value!,
            normalizedRecipients,
            resolved,
            route.Value! with { RouteSnapshotJson = NotificationMessageMetadata.Freeze(
                route.Value!.RouteSnapshotJson, request.ExpiresAtUtc, request.DeepLink, catalog.Version) },
            attachments.Value!));
    }

    /// <summary>短事务内受理意图，并以原始请求核对并发获胜者的快照。</summary>
    /// <param name="scope">可信租户作用域。</param>
    /// <param name="actorUserId">创建主体。</param>
    /// <param name="request">用于并发重放比较的原始请求。</param>
    /// <param name="prepared">首次受理所需的解析结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<Result<NotificationIntentCreateResult>> CreateCoreAsync(
        NotificationInboxScope scope,
        Guid actorUserId,
        CreateNotificationIntentRequest request,
        PreparedIntent prepared,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<NotificationIntentRecord>(
                NotificationPlatformSql.FindIntentByIdempotency,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ProducerKey", prepared.ProducerKey),
                    ("IdempotencyKey", prepared.IdempotencyKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return await ReplayOrConflictAsync(existing, request, cancellationToken).ConfigureAwait(false);
        }

        var intentId = idGenerator.NewId();
        var insert = scope.IsHost
            ? NotificationPlatformSql.InsertIntentHost
            : NotificationPlatformSql.InsertIntentTenant;
        var affected = await commandExecutor.ExecuteAsync(
                insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", intentId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ProducerKey", prepared.ProducerKey),
                    ("SceneKey", prepared.SceneKey),
                    ("IdempotencyKey", prepared.IdempotencyKey),
                    ("TemplateVersionId", prepared.Version.Id),
                    ("BindingVersionId", prepared.Route.BindingVersionId),
                    ("PolicyCategoryKey", prepared.PolicyCategoryKey),
                    ("DispatchModeKey", prepared.Route.DispatchModeKey),
                    ("RouteSnapshotJson", prepared.Route.RouteSnapshotJson),
                    ("ParameterSnapshotJson", prepared.ParameterSnapshotJson),
                    ("StatusKey", NotificationTemplateCompiler.IntentStatusAccepted),
                    ("CreatedById", actorUserId),
                    ("CreatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        var record = await queryExecutor.QuerySingleOrDefaultAsync<NotificationIntentRecord>(
                NotificationPlatformSql.FindIntentByIdempotency,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ProducerKey", prepared.ProducerKey),
                    ("IdempotencyKey", prepared.IdempotencyKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFoundCreate();
        }

        if (affected == 0)
        {
            return await ReplayOrConflictAsync(record, request, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var isInbox = string.Equals(
            prepared.Template.ChannelKey,
            NotificationTemplateCompiler.InboxChannelKey,
            StringComparison.Ordinal);
        IReadOnlyList<Guid> deliveryProfileVersionIds = isInbox
            ? []
            : prepared.Route.ActiveProfileVersionIds;

        foreach (var recipient in prepared.ResolvedRecipients)
        {
            var recipientId = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.InsertRecipient,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", recipientId),
                        ("IntentId", record.Id),
                        ("RecipientTypeKey", recipient.Input.RecipientTypeKey),
                        ("RecipientKey", recipient.Input.RecipientKey),
                        ("UserId", recipient.UserId),
                        ("ResolutionStatusKey", "resolved"),
                        ("CreatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);

            var deliveryPolicy = isInbox
                ? null
                : await ResolveDeliveryPolicyAsync(
                        scope,
                        recipient.UserId,
                        prepared.Template.ChannelKey,
                        prepared.PolicyCategoryKey,
                        prepared.EmergencyOverrideAllowed,
                        now,
                        cancellationToken)
                    .ConfigureAwait(false);
            var contentSnapshot = isInbox
                ? null
                : await RenderDeliveryContentSnapshotAsync(scope, prepared, recipient, cancellationToken)
                    .ConfigureAwait(false);
            if (contentSnapshot is { IsSuccess: false })
            {
                return Result<NotificationIntentCreateResult>.Failure(contentSnapshot.Error!);
            }

            foreach (var profileVersionId in deliveryProfileVersionIds)
            {
                await commandExecutor.ExecuteAsync(
                        NotificationPlatformSql.InsertDelivery,
                        NotificationPlatformSqlParameters.Create(
                            ("Id", idGenerator.NewId()),
                            ("IntentId", record.Id),
                            ("RecipientId", recipientId),
                            ("ChannelKey", prepared.Template.ChannelKey),
                            ("ProviderProfileVersionId", profileVersionId),
                            ("BindingVersionId", prepared.Route.BindingVersionId),
                            ("StatusKey", deliveryPolicy?.StatusKey ?? "accepted"),
                            ("NextAttemptAtUtc", deliveryPolicy?.NextAttemptAtUtc ?? now),
                            ("CreatedAtUtc", now),
                            ("ContentSnapshotJson", contentSnapshot?.Value),
                            ("PreferenceVersion", deliveryPolicy?.PreferenceVersion),
                            ("PolicyDecisionKey", deliveryPolicy?.DecisionKey),
                            ("SuppressionReasonCode", deliveryPolicy?.SuppressionReasonCode)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var attachmentSync = await attachmentCoordinator
            .SynchronizeAsync(record.Id, prepared.AttachmentFileIds, cancellationToken)
            .ConfigureAwait(false);
        if (!attachmentSync.IsSuccess)
        {
            return Result<NotificationIntentCreateResult>.Failure(attachmentSync.Error!);
        }

        var inboxEvents = new List<InboxMessageReceivedIntegrationEvent>(prepared.ResolvedRecipients.Count);
        if (isInbox)
        {
            foreach (var recipient in prepared.ResolvedRecipients)
            {
                var selected = await templateSelector
                    .ResolvePublishedAsync(
                        scope,
                        prepared.Template.TemplateKey,
                        recipient.PreferredLocale,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!selected.IsSuccess)
                {
                    return Result<NotificationIntentCreateResult>.Failure(selected.Error!);
                }

                var rendered = NotificationTemplateCompiler.Render(
                    selected.Value!.Version.Subject,
                    selected.Value.Version.BodyJson,
                    prepared.ParameterSnapshotJson);
                if (!rendered.IsSuccess)
                {
                    return Result<NotificationIntentCreateResult>.Failure(rendered.Error!);
                }

                var projected = await inboxProjection.ProjectInAmbientTransactionAsync(
                        scope,
                        actorUserId,
                        record.Id,
                        recipient.UserId,
                        rendered.Value!.Title,
                        rendered.Value.Content,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!projected.IsSuccess)
                {
                    return Result<NotificationIntentCreateResult>.Failure(projected.Error!);
                }

                await commandExecutor.ExecuteAsync(NotificationLifecycleSql.SetInboxMetadata,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", projected.Value!.Id), ("TenantScopeKey", scope.TenantScopeKey),
                        ("ProducerKey", prepared.ProducerKey), ("SceneKey", prepared.SceneKey),
                        ("DeepLinkJson", request.DeepLink is null ? null : System.Text.Json.JsonSerializer.Serialize(
                            request.DeepLink, Serialization.NotificationsJsonSerializerContext.Default.NotificationDeepLink))),
                    cancellationToken).ConfigureAwait(false);

                var inboxEvent = new InboxMessageReceivedIntegrationEvent(
                    recipient.UserId,
                    projected.Value!.Id,
                    projected.Value.Title,
                    scope.TenantScopeKey);
                await outboxWriter.AddAsync(
                        NotificationRealtimeEventTypes.InboxMessageReceived,
                        1,
                        inboxEvent,
                        cancellationToken)
                    .ConfigureAwait(false);
                inboxEvents.Add(inboxEvent);
            }
        }

        logger.LogInformation(
            "Accepted notification intent {IntentId} for template {TemplateKey}.",
            record.Id,
            prepared.Template.TemplateKey);
        var mapped = await MapAsync(record, cancellationToken).ConfigureAwait(false);
        return Result<NotificationIntentCreateResult>.Success(
            new NotificationIntentCreateResult(mapped, Created: true, inboxEvents));
    }

    /// <summary>按首次发布版本验证重放负载，忽略当前模板是否仍可发布或投递。</summary>
    /// <param name="existing">当前作用域内已受理的意图。</param>
    /// <param name="request">重试请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task<Result<NotificationIntentCreateResult>> ReplayOrConflictAsync(
        NotificationIntentRecord existing,
        CreateNotificationIntentRequest request,
        CancellationToken cancellationToken)
    {
        var metadata = NotificationMessageMetadata.Read(existing.RouteSnapshotJson);
        if (metadata.ExpiresAtUtc != request.ExpiresAtUtc || metadata.DeepLink != request.DeepLink)
            return IdempotencyConflict();
        var version = await queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateVersionRecord>(
            NotificationPlatformSql.FindTemplateVersionById,
            NotificationPlatformSqlParameters.Create(("Id", existing.TemplateVersionId)), cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return IdempotencyConflict();
        }

        var template = await queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateRecord>(
            NotificationPlatformSql.FindTemplateById,
            NotificationPlatformSqlParameters.Create(("Id", version.TemplateId),
                ("TenantScopeKey", existing.TenantScopeKey)), cancellationToken).ConfigureAwait(false);
        var templateKey = NotificationTemplateCompiler.NormalizeStableKey(request.TemplateKey, "TemplateKey");
        var scene = NotificationTemplateCompiler.NormalizeStableKey(request.SceneKey, "SceneKey");
        var normalizedRecipients = NormalizeRecipients(request.Recipients);
        if (template is null || !templateKey.IsSuccess || !scene.IsSuccess || !normalizedRecipients.IsSuccess
            || !string.Equals(template.TemplateKey, templateKey.Value, StringComparison.Ordinal))
        {
            return IdempotencyConflict();
        }

        var schema = NotificationTemplateCompiler.NormalizeSchema(DeserializeSchema(version.ParameterSchemaJson));
        if (!schema.IsSuccess)
        {
            return IdempotencyConflict();
        }

        var parameters = NotificationTemplateCompiler.ValidateAndSnapshotParameters(schema.Value!, request.Parameters);
        if (!parameters.IsSuccess)
        {
            return IdempotencyConflict();
        }

        var recipients = await queryExecutor.QueryAsync<NotificationRecipientRecord>(
                NotificationPlatformSql.ListRecipientsByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", existing.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        var attachments = await queryExecutor.QueryAsync<NotificationIntentAttachmentRecord>(
                NotificationIntentAttachmentSql.ListByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", existing.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        var snapshot = new NotificationIntentRecordSnapshot(
            existing.TemplateVersionId,
            existing.SceneKey,
            existing.ParameterSnapshotJson,
            recipients.Select(item => new NotificationRecipientInput(item.RecipientTypeKey, item.RecipientKey))
                .ToArray(),
            attachments.Select(item => item.FileId).ToArray());
        if (!NotificationTemplateCompiler.PayloadsMatch(
                version.Id,
                scene.Value!,
                parameters.Value!,
                normalizedRecipients.Value!,
                NotificationIntentAttachmentRules.NormalizeFileIds(request.AttachmentFileIds),
                snapshot))
        {
            return IdempotencyConflict();
        }

        var mapped = await MapAsync(existing, cancellationToken).ConfigureAwait(false);
        return Result<NotificationIntentCreateResult>.Success(
            new NotificationIntentCreateResult(mapped, Created: false, []));
    }

    /// <summary>同一幂等键不得受理不同的业务负载。</summary>
    private static Result<NotificationIntentCreateResult> IdempotencyConflict() =>
        Result<NotificationIntentCreateResult>.Failure(new Error(
            NotificationsErrorCodes.IntentIdempotencyConflict,
            "The intent idempotency key conflicts with a different payload.", ErrorType.Conflict));

    private Task<NotificationIntentRecord?> FindIntentAsync(
        NotificationInboxScope scope,
        Guid intentId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<NotificationIntentRecord>(
            NotificationPlatformSql.FindIntentById,
            NotificationPlatformSqlParameters.Create(
                ("Id", intentId),
                ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken);

    private async Task<NotificationIntentResponse> MapAsync(
        NotificationIntentRecord record,
        CancellationToken cancellationToken)
    {
        var recipients = await queryExecutor.QueryAsync<NotificationRecipientRecord>(
                NotificationPlatformSql.ListRecipientsByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", record.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        var attachments = await queryExecutor.QueryAsync<NotificationIntentAttachmentRecord>(
                NotificationIntentAttachmentSql.ListByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", record.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        return new NotificationIntentResponse(
            record.Id,
            record.ProducerKey,
            record.SceneKey,
            record.IdempotencyKey,
            record.TemplateVersionId,
            record.BindingVersionId,
            record.PolicyCategoryKey,
            record.DispatchModeKey,
            record.StatusKey,
            record.RouteSnapshotJson,
            record.ParameterSnapshotJson,
            recipients.Select(item => new NotificationRecipientResponse(
                    item.Id,
                    item.RecipientTypeKey,
                    item.RecipientKey,
                    item.UserId,
                    item.ResolutionStatusKey))
                .ToArray(),
            attachments.Select(item => new NotificationIntentAttachmentResponse(item.FileId, item.SortOrder))
                .ToArray(),
            record.CreatedAtUtc, record.Revision);
    }

    private async Task TryPublishInboxAsync(
        IReadOnlyList<InboxMessageReceivedIntegrationEvent> events,
        CancellationToken cancellationToken)
    {
        foreach (var inboxEvent in events)
        {
            try
            {
                await realtimeDelivery.PublishInboxMessageAsync(inboxEvent, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to publish inbox message {MessageId} after the database commit.",
                    inboxEvent.MessageId);
            }
        }
    }

    private async Task<Result<string>> RenderDeliveryContentSnapshotAsync(
        NotificationInboxScope scope,
        PreparedIntent prepared,
        ResolvedNotificationRecipient recipient,
        CancellationToken cancellationToken)
    {
        var selected = await templateSelector
            .ResolvePublishedAsync(
                scope,
                prepared.Template.TemplateKey,
                recipient.PreferredLocale,
                cancellationToken)
            .ConfigureAwait(false);
        if (!selected.IsSuccess)
        {
            return Result<string>.Failure(selected.Error!);
        }

        var rendered = NotificationTemplateCompiler.Render(
            selected.Value!.Version.Subject,
            selected.Value.Version.BodyJson,
            prepared.ParameterSnapshotJson);
        if (!rendered.IsSuccess)
        {
            return Result<string>.Failure(rendered.Error!);
        }

        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            // 按收件人与渠道冻结实际生效的语言、模板版本与已发布内容哈希；
            // 重试一律复用该快照，模板后续发布不会改变在途内容。
            writer.WriteString("localeTag", selected.Value.Version.LocaleTag);
            writer.WriteString("templateVersionId", selected.Value.Version.Id);
            writer.WriteString("contentHash", selected.Value.Version.ContentHash);
            writer.WriteString("subject", rendered.Value!.Title);
            writer.WriteString("body", rendered.Value.Content);
            writer.WriteEndObject();
        }

        return Result<string>.Success(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    private async Task<DeliveryPolicyDecision> ResolveDeliveryPolicyAsync(
        NotificationInboxScope scope,
        Guid userId,
        string channelKey,
        string categoryKey,
        bool emergencyOverrideAllowed,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var preference = await queryExecutor.QuerySingleOrDefaultAsync<NotificationPreferenceRecord>(
                NotificationPlatformSql.FindPreferenceByUserChannel,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId),
                    ("ChannelKey", channelKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var category = categoryKey switch
        {
            "mandatory" => NotificationPolicyCategory.Mandatory,
            "transactional" => NotificationPolicyCategory.Transactional,
            "marketing" => NotificationPolicyCategory.Marketing,
            _ => NotificationPolicyCategory.Informational,
        };
        var inQuiet = preference?.QuietHoursJson is { } json
            && Domain.NotificationQuietHours.IsInQuietHours(json, now);
        var evaluation = NotificationPolicy.Evaluate(
            category,
            new NotificationPreferenceSnapshot(
                preference?.ChannelOptedOut ?? false,
                preference?.MarketingConsentGranted ?? false,
                inQuiet),
            emergencyOverride: emergencyOverrideAllowed);
        if (evaluation.IsSuppressed)
        {
            return new DeliveryPolicyDecision(
                "suppressed",
                null,
                preference?.Version,
                "suppressed",
                evaluation.SuppressionReasonCode);
        }

        if (evaluation.ShouldDelayForQuietHours && preference?.QuietHoursJson is { } quietHours)
        {
            // 静默时段只延迟可延迟的投递：把下一可发送时间（跨午夜、夏令时按声明时区计算）写入
            // NextAttemptAtUtc，领取查询在到期前不会拉取该投递。
            return new DeliveryPolicyDecision(
                "accepted",
                Domain.NotificationQuietHours.NextAllowedAt(quietHours, now) ?? now,
                preference.Version,
                "delayed_quiet_hours",
                null);
        }

        return new DeliveryPolicyDecision("accepted", now, preference?.Version, "dispatch", null);
    }

    private static Result<IReadOnlyList<NotificationRecipientInput>> NormalizeRecipients(
        IReadOnlyList<NotificationRecipientInput>? recipients)
    {
        if (recipients is null
            || recipients.Count is < 1 or > NotificationTemplateCompiler.MaxRecipients)
        {
            return Result<IReadOnlyList<NotificationRecipientInput>>.Failure(RecipientLimit());
        }

        var normalized = new List<NotificationRecipientInput>(recipients.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipient in recipients)
        {
            var typeKey = recipient.RecipientTypeKey?.Trim() ?? string.Empty;
            if (!string.Equals(typeKey, NotificationTemplateCompiler.RecipientTypeUser, StringComparison.Ordinal))
            {
                return Result<IReadOnlyList<NotificationRecipientInput>>.Failure(RecipientLimit());
            }

            if (!Guid.TryParse(recipient.RecipientKey, out var userId) || !seen.Add(userId.ToString("N")))
            {
                return Result<IReadOnlyList<NotificationRecipientInput>>.Failure(RecipientLimit());
            }

            normalized.Add(new NotificationRecipientInput(typeKey, userId.ToString("N")));
        }

        return Result<IReadOnlyList<NotificationRecipientInput>>.Success(normalized);
    }

    private async Task<Result<ResolvedRoute>> ResolveRouteAsync(
        NotificationInboxScope scope,
        string producerKey,
        string sceneKey,
        string channelKey,
        string parameterSnapshotJson,
        CancellationToken cancellationToken)
    {
        if (string.Equals(channelKey, NotificationTemplateCompiler.InboxChannelKey, StringComparison.Ordinal))
        {
            return Result<ResolvedRoute>.Success(new ResolvedRoute(
                null,
                NotificationTemplateCompiler.DispatchModeSingle,
                NotificationTemplateCompiler.EmptyRouteTargetsJson,
                []));
        }

        var published = await queryExecutor.QueryAsync<NotificationBindingVersionRecord>(
                NotificationPlatformSql.ListPublishedBindingsByScene,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ProducerKey", producerKey),
                    ("SceneKey", sceneKey),
                    ("ChannelKey", channelKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (published.Count == 0)
        {
            return Result<ResolvedRoute>.Failure(new Error(
                NotificationsErrorCodes.BindingNotPublished,
                "The scene has no published binding.",
                ErrorType.BusinessRule));
        }

        if (published.Count > 1)
        {
            return Result<ResolvedRoute>.Failure(new Error(
                NotificationsErrorCodes.BindingSceneConflict,
                "A published binding already exists for this producer, scene and channel.",
                ErrorType.Conflict));
        }

        var binding = published[0];
        var mode = binding.DispatchModeKey switch
        {
            "fan_out" => NotificationDispatchMode.FanOut,
            "failover" => NotificationDispatchMode.Failover,
            "match" => NotificationDispatchMode.Match,
            _ => NotificationDispatchMode.Single,
        };
        var snapshotTargets = ParseRouteSnapshotTargets(binding.BindingTargetsJson);
        if (!snapshotTargets.IsSuccess)
        {
            return Result<ResolvedRoute>.Failure(snapshotTargets.Error!);
        }

        using var parameters = System.Text.Json.JsonDocument.Parse(parameterSnapshotJson);
        var candidates = new List<NotificationRouteCandidate>(snapshotTargets.Value!.Count);
        foreach (var target in snapshotTargets.Value)
        {
            var profile = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileRecord>(
                    NotificationPlatformSql.FindProfileByKey,
                    NotificationPlatformSqlParameters.Create(
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("ProfileKey", target.ProfileKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (profile is null || !profile.IsEnabled)
            {
                return Result<ResolvedRoute>.Failure(new Error(
                    NotificationsErrorCodes.RouteProfileUnavailable,
                    "Exactly one available provider profile is required.",
                    ErrorType.BusinessRule));
            }

            var matches = mode != NotificationDispatchMode.Match
                || EvaluateMatchCondition(target.Condition, parameters.RootElement);
            candidates.Add(new NotificationRouteCandidate(
                target.ProfileKey,
                channelKey,
                target.Order,
                profile.IsEnabled,
                matches));
        }

        var plan = NotificationRoutePlanner.Plan(mode, candidates);
        if (!plan.IsSuccess)
        {
            return Result<ResolvedRoute>.Failure(new Error(
                plan.ErrorCode ?? NotificationsErrorCodes.RouteProfileUnavailable,
                "The binding route is invalid.",
                ErrorType.BusinessRule));
        }

        var selectedProfileKeys = plan.Targets.Select(item => item.ProfileKey).ToHashSet(StringComparer.Ordinal);
        var activeProfileVersionIds = snapshotTargets.Value
            .Where(item => selectedProfileKeys.Contains(item.ProfileKey))
            .OrderBy(item => item.Order)
            .Select(item => item.ProfileVersionId)
            .ToArray();
        return Result<ResolvedRoute>.Success(new ResolvedRoute(
            binding.Id,
            binding.DispatchModeKey,
            binding.BindingTargetsJson,
            activeProfileVersionIds));
    }

    private static NotificationTemplateParameterSchema DeserializeSchema(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        var parameters = new List<NotificationTemplateParameterDefinition>();
        foreach (var item in root.GetProperty("parameters").EnumerateArray())
        {
            parameters.Add(new NotificationTemplateParameterDefinition(
                item.GetProperty("name").GetString() ?? string.Empty,
                item.GetProperty("typeKey").GetString() ?? string.Empty,
                item.GetProperty("required").GetBoolean(),
                item.TryGetProperty("maxLength", out var maxLength)
                    ? maxLength.GetInt32()
                    : null));
        }

        return new NotificationTemplateParameterSchema(
            root.GetProperty("schemaVersion").GetInt32(),
            parameters);
    }

    /// <summary>
    /// 从不可变 Binding 快照读取候选目标；缺字段失败关闭，避免 Worker 领到未物化的渠道。
    /// </summary>
    private static Result<IReadOnlyList<RouteSnapshotTarget>> ParseRouteSnapshotTargets(string routeSnapshotJson)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(routeSnapshotJson);
            var targets = new List<RouteSnapshotTarget>();
            foreach (var target in document.RootElement.EnumerateArray())
            {
                if (!target.TryGetProperty("profileVersionId", out var versionElement)
                    || !Guid.TryParse(versionElement.GetString(), out var profileVersionId))
                {
                    return RouteUnavailable();
                }

                var condition = ParseRouteCondition(target);
                targets.Add(new RouteSnapshotTarget(
                    target.GetProperty("profileKey").GetString() ?? string.Empty,
                    profileVersionId,
                    target.GetProperty("order").GetInt32(),
                    condition));
            }

            return targets.Count == 0 ? RouteUnavailable() : Result<IReadOnlyList<RouteSnapshotTarget>>.Success(targets);
        }
        catch (System.Text.Json.JsonException)
        {
            return RouteUnavailable();
        }
    }

    private static NotificationBindingTargetConditionInput? ParseRouteCondition(System.Text.Json.JsonElement target)
    {
        if (!target.TryGetProperty("condition", out var condition) || condition.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        return new NotificationBindingTargetConditionInput(
            condition.GetProperty("parameterName").GetString() ?? string.Empty,
            condition.GetProperty("operatorKey").GetString() ?? string.Empty,
            condition.TryGetProperty("expectedValue", out var expected) ? expected.GetString() : null);
    }

    private static bool EvaluateMatchCondition(
        NotificationBindingTargetConditionInput? condition,
        System.Text.Json.JsonElement parameters)
    {
        if (condition is null || !parameters.TryGetProperty(condition.ParameterName, out var value))
        {
            return false;
        }

        if (string.Equals(condition.OperatorKey, "exists", StringComparison.Ordinal))
        {
            return value.ValueKind is not System.Text.Json.JsonValueKind.Undefined and not System.Text.Json.JsonValueKind.Null;
        }

        var actual = value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => value.GetString() ?? string.Empty,
            System.Text.Json.JsonValueKind.Number when value.TryGetInt64(out var number) =>
                number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            _ => string.Empty,
        };
        return string.Equals(condition.OperatorKey, "equals", StringComparison.Ordinal)
            ? string.Equals(actual, condition.ExpectedValue, StringComparison.Ordinal)
            : string.Equals(condition.OperatorKey, "not_equals", StringComparison.Ordinal)
              && !string.Equals(actual, condition.ExpectedValue, StringComparison.Ordinal);
    }

    private static Result<IReadOnlyList<RouteSnapshotTarget>> RouteUnavailable() =>
        Result<IReadOnlyList<RouteSnapshotTarget>>.Failure(new Error(
            NotificationsErrorCodes.RouteProfileUnavailable,
            "Exactly one available provider profile is required.",
            ErrorType.BusinessRule));

    private static Error RecipientLimit() =>
        new(
            NotificationsErrorCodes.IntentRecipientLimit,
            "The recipient list exceeds the allowed size.",
            ErrorType.Validation);

    private static Result<NotificationIntentResponse> NotFound() =>
        Result<NotificationIntentResponse>.Failure(new Error(
            NotificationsErrorCodes.IntentNotFound,
            "The notification intent was not found.",
            ErrorType.NotFound));

    private static Result<NotificationIntentCreateResult> NotFoundCreate() =>
        Result<NotificationIntentCreateResult>.Failure(new Error(
            NotificationsErrorCodes.IntentNotFound,
            "The notification intent was not found.",
            ErrorType.NotFound));

    private sealed record PreparedIntent(
        string ProducerKey,
        string SceneKey,
        string IdempotencyKey,
        string PolicyCategoryKey,
        bool EmergencyOverrideAllowed,
        NotificationTemplateRecord Template,
        NotificationTemplateVersionRecord Version,
        string ParameterSnapshotJson,
        IReadOnlyList<NotificationRecipientInput> Recipients,
        IReadOnlyList<ResolvedNotificationRecipient> ResolvedRecipients,
        ResolvedRoute Route,
        IReadOnlyList<Guid> AttachmentFileIds);

    private sealed record ResolvedRoute(
        Guid? BindingVersionId,
        string DispatchModeKey,
        string RouteSnapshotJson,
        IReadOnlyList<Guid> ActiveProfileVersionIds);

    private sealed record RouteSnapshotTarget(
        string ProfileKey,
        Guid ProfileVersionId,
        int Order,
        NotificationBindingTargetConditionInput? Condition);

    private sealed record DeliveryPolicyDecision(
        string StatusKey,
        DateTimeOffset? NextAttemptAtUtc,
        long? PreferenceVersion,
        string DecisionKey,
        string? SuppressionReasonCode);
}

/// <summary>区分首次受理与幂等回放，避免把 201/200 决策泄漏到持久化层。</summary>
internal sealed record NotificationIntentCreateResult(
    NotificationIntentResponse Intent,
    bool Created,
    IReadOnlyList<InboxMessageReceivedIntegrationEvent> InboxEvents);
