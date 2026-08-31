using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageBindings;

/// <summary>当前作用域场景绑定的草稿与不可变发布；Enabled Profile 不会自动进入目标列表。</summary>
internal sealed class NotificationBindingService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    INotificationProviderTypeCatalog catalog,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<NotificationBindingResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountBindingsForScope,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? NotificationPlatformSql.ListBindingsMySql
            : NotificationPlatformSql.ListBindingsSqlServer;
        var rows = await queryExecutor.QueryAsync<NotificationBindingRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = new List<NotificationBindingResponse>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(await MapAsync(row, includeLatestVersion: false, cancellationToken).ConfigureAwait(false));
        }

        return Result<PagedResult<NotificationBindingResponse>>.Success(
            new PagedResult<NotificationBindingResponse>(items, page, pageSize, total));
    }

    public async Task<Result<NotificationBindingResponse>> GetByIdAsync(
        Guid bindingId,
        CancellationToken cancellationToken)
    {
        var record = await FindAsync(bindingId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<NotificationBindingResponse>.Success(
                await MapAsync(record, includeLatestVersion: true, cancellationToken).ConfigureAwait(false));
    }

    public Task<Result<NotificationBindingResponse>> CreateAsync(
        Guid actorUserId,
        CreateNotificationBindingRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<NotificationBindingResponse>> UpdateAsync(
        Guid bindingId,
        UpdateNotificationBindingRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(bindingId, request, token),
            cancellationToken);

    public Task<Result<NotificationBindingResponse>> PublishAsync(
        Guid actorUserId,
        Guid bindingId,
        PublishNotificationBindingRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => PublishCoreAsync(actorUserId, bindingId, request, token),
            cancellationToken);

    private async Task<Result<NotificationBindingResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateNotificationBindingRequest request,
        CancellationToken cancellationToken)
    {
        var draft = PrepareDraft(
            request.BindingKey,
            request.DispatchModeKey,
            request.ProducerKey,
            request.SceneKey,
            request.ChannelKey,
            request.Targets);
        if (!draft.IsSuccess)
        {
            return Result<NotificationBindingResponse>.Failure(draft.Error!);
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var bindingId = idGenerator.NewId();
        var insert = scope.IsHost
            ? NotificationPlatformSql.InsertBindingHost
            : NotificationPlatformSql.InsertBindingTenant;
        var affected = await commandExecutor.ExecuteAsync(
                insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", bindingId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("BindingKey", draft.Value!.BindingKey),
                    ("DraftDispatchModeKey", draft.Value.DispatchModeKey),
                    ("DraftJson", draft.Value.DraftJson),
                    ("CreatedById", actorUserId),
                    ("CreatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? Result<NotificationBindingResponse>.Failure(new Error(
                NotificationsErrorCodes.BindingKeyConflict,
                "A binding with this key already exists.",
                ErrorType.Conflict))
            : await GetByIdAsync(bindingId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationBindingResponse>> UpdateCoreAsync(
        Guid bindingId,
        UpdateNotificationBindingRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(bindingId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var draft = PrepareDraft(
            existing.BindingKey,
            request.DispatchModeKey,
            request.ProducerKey,
            request.SceneKey,
            request.ChannelKey,
            request.Targets);
        if (!draft.IsSuccess)
        {
            return Result<NotificationBindingResponse>.Failure(draft.Error!);
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.UpdateBindingDraft,
                NotificationPlatformSqlParameters.Create(
                    ("Id", bindingId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("DraftDispatchModeKey", draft.Value!.DispatchModeKey),
                    ("DraftJson", draft.Value.DraftJson),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("NextVersion", request.Version + 1),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? ConcurrencyConflict()
            : await GetByIdAsync(bindingId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationBindingResponse>> PublishCoreAsync(
        Guid actorUserId,
        Guid bindingId,
        PublishNotificationBindingRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(bindingId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var parsed = ParseDraft(existing.DraftJson);
        if (!parsed.IsSuccess)
        {
            return Result<NotificationBindingResponse>.Failure(parsed.Error!);
        }

        var targetsJson = await ResolvePublishedTargetsAsync(
                parsed.Value!,
                existing.DraftDispatchModeKey,
                cancellationToken)
            .ConfigureAwait(false);
        if (!targetsJson.IsSuccess)
        {
            return Result<NotificationBindingResponse>.Failure(targetsJson.Error!);
        }

        var duplicates = await queryExecutor.QueryAsync<NotificationBindingVersionRecord>(
                NotificationPlatformSql.ListPublishedBindingsByScene,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", NotificationInboxScope.Resolve(currentTenant).TenantScopeKey),
                    ("ProducerKey", parsed.Value!.ProducerKey),
                    ("SceneKey", parsed.Value.SceneKey),
                    ("ChannelKey", parsed.Value.ChannelKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (duplicates.Any(item => item.BindingId != bindingId))
        {
            return Result<NotificationBindingResponse>.Failure(new Error(
                NotificationsErrorCodes.BindingSceneConflict,
                "A published binding already exists for this producer, scene and channel.",
                ErrorType.Conflict));
        }

        var hash = NotificationProfileCompiler.ComputeBindingHash(
            parsed.Value.ProducerKey,
            parsed.Value.SceneKey,
            parsed.Value.ChannelKey,
            existing.DraftDispatchModeKey,
            targetsJson.Value!);
        var versionRecord = await queryExecutor.QuerySingleOrDefaultAsync<NotificationBindingVersionRecord>(
                NotificationPlatformSql.FindBindingVersionByHash,
                NotificationPlatformSqlParameters.Create(
                    ("BindingId", bindingId),
                    ("ContentHash", hash)),
                cancellationToken)
            .ConfigureAwait(false);
        if (versionRecord is null)
        {
            var nextNumber = (int)await queryExecutor.QuerySingleOrDefaultAsync<long>(
                    NotificationPlatformSql.CountBindingVersions,
                    NotificationPlatformSqlParameters.Create(("BindingId", bindingId)),
                    cancellationToken)
                .ConfigureAwait(false) + 1;
            var versionId = idGenerator.NewId();
            var inserted = await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.InsertBindingVersion,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", versionId),
                        ("BindingId", bindingId),
                        ("VersionNumber", nextNumber),
                        ("ProducerKey", parsed.Value.ProducerKey),
                        ("SceneKey", parsed.Value.SceneKey),
                        ("ChannelKey", parsed.Value.ChannelKey),
                        ("DispatchModeKey", existing.DraftDispatchModeKey),
                        ("BindingTargetsJson", targetsJson.Value!),
                        ("ContentHash", hash),
                        ("PublishedById", actorUserId),
                        ("PublishedAtUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false);
            versionRecord = await queryExecutor.QuerySingleOrDefaultAsync<NotificationBindingVersionRecord>(
                    NotificationPlatformSql.FindBindingVersionByHash,
                    NotificationPlatformSqlParameters.Create(
                        ("BindingId", bindingId),
                        ("ContentHash", hash)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (inserted == 0 || versionRecord is null)
            {
                return ConcurrencyConflict();
            }
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.PublishBinding,
                NotificationPlatformSqlParameters.Create(
                    ("Id", bindingId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("LatestPublishedVersionId", versionRecord.Id),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("NextVersion", request.Version + 1),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyConflict();
        }

        await commandExecutor.ExecuteAsync(
                scope.IsHost
                    ? NotificationPlatformSql.InsertDomainAuditHost
                    : NotificationPlatformSql.InsertDomainAuditTenant,
                NotificationPlatformSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("OperationKey", "binding.publish"),
                    ("ActorUserId", actorUserId),
                    ("ResourceTypeKey", "binding"),
                    ("ResourceId", bindingId),
                    ("OutcomeKey", "succeeded"),
                    ("DetailJson", "{}"),
                    ("CreatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        return await GetByIdAsync(bindingId, cancellationToken).ConfigureAwait(false);
    }

    private Result<PreparedBindingDraft> PrepareDraft(
        string bindingKey,
        string dispatchModeKey,
        string producerKey,
        string sceneKey,
        string channelKey,
        IReadOnlyList<NotificationBindingTargetInput> targets)
    {
        var key = NotificationTemplateCompiler.NormalizeStableKey(bindingKey, "BindingKey");
        var producer = NotificationTemplateCompiler.NormalizeStableKey(producerKey, "ProducerKey");
        var scene = NotificationTemplateCompiler.NormalizeStableKey(sceneKey, "SceneKey");
        var mode = NotificationProfileCompiler.NormalizeDispatchMode(dispatchModeKey);
        if (!key.IsSuccess)
        {
            return Result<PreparedBindingDraft>.Failure(key.Error!);
        }

        if (!producer.IsSuccess)
        {
            return Result<PreparedBindingDraft>.Failure(producer.Error!);
        }

        if (!scene.IsSuccess)
        {
            return Result<PreparedBindingDraft>.Failure(scene.Error!);
        }

        if (!mode.IsSuccess)
        {
            return Result<PreparedBindingDraft>.Failure(mode.Error!);
        }

        var channel = channelKey?.Trim() ?? string.Empty;
        if (!catalog.SupportsChannel(channel))
        {
            return Result<PreparedBindingDraft>.Failure(new Error(
                NotificationsErrorCodes.IntentChannelUnsupported,
                "Only registered provider channels can be bound.",
                ErrorType.Validation));
        }

        var json = NotificationProfileCompiler.WriteBindingDraftJson(
            producer.Value!,
            scene.Value!,
            channel,
            targets);
        return !json.IsSuccess
            ? Result<PreparedBindingDraft>.Failure(json.Error!)
            : Result<PreparedBindingDraft>.Success(
                new PreparedBindingDraft(key.Value!, mode.Value!, json.Value!));
    }

    private static Result<ParsedBindingDraft> ParseDraft(string draftJson)
    {
        using var document = JsonDocument.Parse(draftJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("producerKey", out var producer)
            || !root.TryGetProperty("sceneKey", out var scene)
            || !root.TryGetProperty("channelKey", out var channel)
            || !root.TryGetProperty("targets", out var targets)
            || targets.ValueKind != JsonValueKind.Array)
        {
            return Result<ParsedBindingDraft>.Failure(new Error(
                NotificationsErrorCodes.BindingValidationFailed,
                "The binding draft is invalid.",
                ErrorType.Validation));
        }

        var items = new List<NotificationBindingTargetInput>();
        foreach (var target in targets.EnumerateArray())
        {
            items.Add(new NotificationBindingTargetInput(
                target.GetProperty("profileKey").GetString() ?? string.Empty,
                target.GetProperty("order").GetInt32()));
        }

        return Result<ParsedBindingDraft>.Success(
            new ParsedBindingDraft(
                producer.GetString() ?? string.Empty,
                scene.GetString() ?? string.Empty,
                channel.GetString() ?? string.Empty,
                items));
    }

    private async Task<Result<string>> ResolvePublishedTargetsAsync(
        ParsedBindingDraft draft,
        string dispatchModeKey,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var resolved = new List<(string ProfileKey, Guid ProfileVersionId, int Order, bool IsEnabled)>(
            draft.Targets.Count);
        foreach (var target in draft.Targets)
        {
            var profile = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileRecord>(
                    NotificationPlatformSql.FindProfileByKey,
                    NotificationPlatformSqlParameters.Create(
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("ProfileKey", target.ProfileKey)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (profile is null)
            {
                return Result<string>.Failure(new Error(
                    NotificationsErrorCodes.ProviderProfileNotFound,
                    "The provider profile was not found.",
                    ErrorType.NotFound));
            }

            if (!profile.IsEnabled)
            {
                return Result<string>.Failure(new Error(
                    NotificationsErrorCodes.RouteProfileUnavailable,
                    "Exactly one available provider profile is required.",
                    ErrorType.BusinessRule));
            }

            if (profile.LatestPublishedVersionId is null)
            {
                return Result<string>.Failure(new Error(
                    NotificationsErrorCodes.ProviderProfileNotPublished,
                    "The provider profile has not been published.",
                    ErrorType.BusinessRule));
            }

            if (!catalog.TryGet(profile.ProviderTypeKey, out var descriptor)
                || !descriptor.SupportedChannelKeys.Contains(draft.ChannelKey, StringComparer.Ordinal))
            {
                return Result<string>.Failure(new Error(
                    NotificationsErrorCodes.BindingValidationFailed,
                    "The binding target cannot serve this channel.",
                    ErrorType.Validation));
            }

            resolved.Add((profile.ProfileKey, profile.LatestPublishedVersionId.Value, target.Order, profile.IsEnabled));
        }

        var mode = dispatchModeKey switch
        {
            "fan_out" => NotificationDispatchMode.FanOut,
            "failover" => NotificationDispatchMode.Failover,
            "match" => NotificationDispatchMode.Match,
            _ => NotificationDispatchMode.Single,
        };
        var plan = NotificationRoutePlanner.Plan(
            mode,
            resolved.Select(item => new NotificationRouteCandidate(
                    item.ProfileKey,
                    draft.ChannelKey,
                    item.Order,
                    item.IsEnabled,
                    MatchesCondition: true))
                .ToArray());
        if (!plan.IsSuccess)
        {
            return Result<string>.Failure(new Error(
                plan.ErrorCode ?? NotificationsErrorCodes.RouteProfileUnavailable,
                "The binding route is invalid.",
                ErrorType.BusinessRule));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartArray();
            foreach (var item in resolved.OrderBy(value => value.Order))
            {
                writer.WriteStartObject();
                writer.WriteString("profileKey", item.ProfileKey);
                writer.WriteString("profileVersionId", item.ProfileVersionId);
                writer.WriteNumber("order", item.Order);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Result<string>.Success(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    private Task<NotificationBindingRecord?> FindAsync(
        Guid bindingId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        return queryExecutor.QuerySingleOrDefaultAsync<NotificationBindingRecord>(
            NotificationPlatformSql.FindBindingById,
            NotificationPlatformSqlParameters.Create(
                ("Id", bindingId),
                ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken);
    }

    private async Task<NotificationBindingResponse> MapAsync(
        NotificationBindingRecord record,
        bool includeLatestVersion,
        CancellationToken cancellationToken)
    {
        NotificationBindingVersionRecord? version = null;
        if (includeLatestVersion && record.LatestPublishedVersionId is { } versionId)
        {
            version = await queryExecutor.QuerySingleOrDefaultAsync<NotificationBindingVersionRecord>(
                    NotificationPlatformSql.FindBindingVersionById,
                    NotificationPlatformSqlParameters.Create(("Id", versionId)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new NotificationBindingResponse(
            record.Id,
            record.BindingKey,
            record.DraftDispatchModeKey,
            record.DraftJson,
            record.DraftRevision,
            record.LatestPublishedVersionId,
            version?.VersionNumber,
            version?.ProducerKey,
            version?.SceneKey,
            version?.ChannelKey,
            version?.DispatchModeKey,
            version?.BindingTargetsJson,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);
    }

    private static Result<NotificationBindingResponse> NotFound() =>
        Result<NotificationBindingResponse>.Failure(new Error(
            NotificationsErrorCodes.BindingNotFound,
            "The notification binding was not found.",
            ErrorType.NotFound));

    private static Result<NotificationBindingResponse> ConcurrencyConflict() =>
        Result<NotificationBindingResponse>.Failure(new Error(
            NotificationsErrorCodes.BindingConcurrencyConflict,
            "The binding changed concurrently. Refresh and try again.",
            ErrorType.Conflict));

    private sealed record PreparedBindingDraft(string BindingKey, string DispatchModeKey, string DraftJson);

    private sealed record ParsedBindingDraft(
        string ProducerKey,
        string SceneKey,
        string ChannelKey,
        IReadOnlyList<NotificationBindingTargetInput> Targets);
}
