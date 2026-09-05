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
using Full.NET.Modules.Notifications.Providers;
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
        var prepared = await PrepareAsync(scope, request, cancellationToken).ConfigureAwait(false);
        if (!prepared.IsSuccess)
        {
            return Result<NotificationIntentCreateResult>.Failure(prepared.Error!);
        }

        var result = await transaction.ExecuteResultAsync(
                token => CreateCoreAsync(scope, actorUserId, prepared.Value!, token),
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
        CreateNotificationIntentRequest request,
        CancellationToken cancellationToken)
    {
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

        var route = await ResolveRouteAsync(scope, producer.Value!, scene.Value!, template.ChannelKey, cancellationToken)
            .ConfigureAwait(false);
        if (!route.IsSuccess)
        {
            return Result<PreparedIntent>.Failure(route.Error!);
        }

        return Result<PreparedIntent>.Success(new PreparedIntent(
            producer.Value!,
            scene.Value!,
            idempotency.Value!,
            template,
            version,
            snapshot.Value!,
            normalizedRecipients,
            resolved,
            route.Value!));
    }

    private async Task<Result<NotificationIntentCreateResult>> CreateCoreAsync(
        NotificationInboxScope scope,
        Guid actorUserId,
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
            return await ReplayOrConflictAsync(existing, prepared, cancellationToken).ConfigureAwait(false);
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
                    ("PolicyCategoryKey", prepared.Template.ContentCategoryKey),
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
            return await ReplayOrConflictAsync(record, prepared, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var isInbox = string.Equals(
            prepared.Template.ChannelKey,
            NotificationTemplateCompiler.InboxChannelKey,
            StringComparison.Ordinal);
        IReadOnlyList<Guid> deliveryProfileVersionIds = [];
        if (!isInbox)
        {
            var parsed = ParseRouteProfileVersionIds(prepared.Route.RouteSnapshotJson);
            if (!parsed.IsSuccess)
            {
                return Result<NotificationIntentCreateResult>.Failure(parsed.Error!);
            }

            deliveryProfileVersionIds = parsed.Value!;
        }

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
                            ("StatusKey", "accepted"),
                            ("NextAttemptAtUtc", now),
                            ("CreatedAtUtc", now)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
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

    private async Task<Result<NotificationIntentCreateResult>> ReplayOrConflictAsync(
        NotificationIntentRecord existing,
        PreparedIntent prepared,
        CancellationToken cancellationToken)
    {
        var recipients = await queryExecutor.QueryAsync<NotificationRecipientRecord>(
                NotificationPlatformSql.ListRecipientsByIntent,
                NotificationPlatformSqlParameters.Create(("IntentId", existing.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        var snapshot = new NotificationIntentRecordSnapshot(
            existing.TemplateVersionId,
            existing.SceneKey,
            existing.ParameterSnapshotJson,
            recipients.Select(item => new NotificationRecipientInput(item.RecipientTypeKey, item.RecipientKey))
                .ToArray());
        if (!NotificationTemplateCompiler.PayloadsMatch(
                prepared.Version.Id,
                prepared.SceneKey,
                prepared.ParameterSnapshotJson,
                prepared.Recipients,
                snapshot))
        {
            return Result<NotificationIntentCreateResult>.Failure(new Error(
                NotificationsErrorCodes.IntentIdempotencyConflict,
                "The intent idempotency key conflicts with a different payload.",
                ErrorType.Conflict));
        }

        var mapped = await MapAsync(existing, cancellationToken).ConfigureAwait(false);
        return Result<NotificationIntentCreateResult>.Success(
            new NotificationIntentCreateResult(mapped, Created: false, []));
    }

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
            record.CreatedAtUtc);
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
        CancellationToken cancellationToken)
    {
        if (string.Equals(channelKey, NotificationTemplateCompiler.InboxChannelKey, StringComparison.Ordinal))
        {
            return Result<ResolvedRoute>.Success(new ResolvedRoute(
                null,
                NotificationTemplateCompiler.DispatchModeSingle,
                NotificationTemplateCompiler.EmptyRouteSnapshotJson));
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
        using var document = System.Text.Json.JsonDocument.Parse(binding.BindingTargetsJson);
        var candidates = new List<NotificationRouteCandidate>();
        foreach (var target in document.RootElement.EnumerateArray())
        {
            var profileKey = target.GetProperty("profileKey").GetString() ?? string.Empty;
            var profile = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileRecord>(
                    NotificationPlatformSql.FindProfileByKey,
                    NotificationPlatformSqlParameters.Create(
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("ProfileKey", profileKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (profile is null || !profile.IsEnabled)
            {
                return Result<ResolvedRoute>.Failure(new Error(
                    NotificationsErrorCodes.RouteProfileUnavailable,
                    "Exactly one available provider profile is required.",
                    ErrorType.BusinessRule));
            }

            candidates.Add(new NotificationRouteCandidate(
                profileKey,
                channelKey,
                target.GetProperty("order").GetInt32(),
                profile.IsEnabled,
                MatchesCondition: true));
        }

        var mode = binding.DispatchModeKey switch
        {
            "fan_out" => NotificationDispatchMode.FanOut,
            "failover" => NotificationDispatchMode.Failover,
            "match" => NotificationDispatchMode.Match,
            _ => NotificationDispatchMode.Single,
        };
        var plan = NotificationRoutePlanner.Plan(mode, candidates);
        if (!plan.IsSuccess)
        {
            return Result<ResolvedRoute>.Failure(new Error(
                plan.ErrorCode ?? NotificationsErrorCodes.RouteProfileUnavailable,
                "The binding route is invalid.",
                ErrorType.BusinessRule));
        }

        return Result<ResolvedRoute>.Success(new ResolvedRoute(
            binding.Id,
            binding.DispatchModeKey,
            binding.BindingTargetsJson));
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
    /// 从已发布 Binding 快照取出 ProfileVersion；缺字段失败关闭，避免 Worker 领到无渠道的 Delivery。
    /// </summary>
    private static Result<IReadOnlyList<Guid>> ParseRouteProfileVersionIds(string routeSnapshotJson)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(routeSnapshotJson);
            var ids = new List<Guid>();
            foreach (var target in document.RootElement.EnumerateArray())
            {
                if (!target.TryGetProperty("profileVersionId", out var versionElement)
                    || !Guid.TryParse(versionElement.GetString(), out var profileVersionId))
                {
                    return Result<IReadOnlyList<Guid>>.Failure(new Error(
                        NotificationsErrorCodes.RouteProfileUnavailable,
                        "Exactly one available provider profile is required.",
                        ErrorType.BusinessRule));
                }

                ids.Add(profileVersionId);
            }

            if (ids.Count == 0)
            {
                return Result<IReadOnlyList<Guid>>.Failure(new Error(
                    NotificationsErrorCodes.RouteProfileUnavailable,
                    "Exactly one available provider profile is required.",
                    ErrorType.BusinessRule));
            }

            return Result<IReadOnlyList<Guid>>.Success(ids);
        }
        catch (System.Text.Json.JsonException)
        {
            return Result<IReadOnlyList<Guid>>.Failure(new Error(
                NotificationsErrorCodes.RouteProfileUnavailable,
                "Exactly one available provider profile is required.",
                ErrorType.BusinessRule));
        }
    }

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
        NotificationTemplateRecord Template,
        NotificationTemplateVersionRecord Version,
        string ParameterSnapshotJson,
        IReadOnlyList<NotificationRecipientInput> Recipients,
        IReadOnlyList<ResolvedNotificationRecipient> ResolvedRecipients,
        ResolvedRoute Route);

    private sealed record ResolvedRoute(
        Guid? BindingVersionId,
        string DispatchModeKey,
        string RouteSnapshotJson);
}

/// <summary>区分首次受理与幂等回放，避免把 201/200 决策泄漏到持久化层。</summary>
internal sealed record NotificationIntentCreateResult(
    NotificationIntentResponse Intent,
    bool Created,
    IReadOnlyList<InboxMessageReceivedIntegrationEvent> InboxEvents);
