using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageProducerScenes;

/// <summary>
/// 维护受控 Producer/Scene 目录：声明政策类别、合法渠道与静默时段紧急规则。
/// </summary>
/// <remarks>
/// 分类只来自目录，不能通过可编辑模板提升为强制通知；目录缺失或停用时发送链路失败关闭。
/// 所有写操作以 Version 做 CAS，读操作按可信租户作用域过滤。
/// </remarks>
internal sealed class NotificationProducerSceneService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    Providers.INotificationProviderTypeCatalog providerTypeCatalog,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<NotificationProducerSceneResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountProducerScenesForScope,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? NotificationPlatformSql.ListProducerScenesMySql
            : NotificationPlatformSql.ListProducerScenesSqlServer;
        var rows = await queryExecutor.QueryAsync<NotificationProducerSceneRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<NotificationProducerSceneResponse>>.Success(
            new PagedResult<NotificationProducerSceneResponse>(items, page, pageSize, total));
    }

    public async Task<Result<NotificationProducerSceneResponse>> GetByIdAsync(
        Guid producerSceneId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var record = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProducerSceneRecord>(
                NotificationPlatformSql.FindProducerSceneById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", producerSceneId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<NotificationProducerSceneResponse>.Success(Map(record));
    }

    public Task<Result<NotificationProducerSceneResponse>> CreateAsync(
        Guid actorUserId,
        CreateNotificationProducerSceneRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<NotificationProducerSceneResponse>> UpdateAsync(
        Guid producerSceneId,
        UpdateNotificationProducerSceneRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(producerSceneId, request, token),
            cancellationToken);

    public Task<Result<NotificationProducerSceneResponse>> SetEnabledAsync(
        Guid producerSceneId,
        SetNotificationProducerSceneEnabledRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => SetEnabledCoreAsync(producerSceneId, request, token),
            cancellationToken);

    private async Task<Result<NotificationProducerSceneResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateNotificationProducerSceneRequest request,
        CancellationToken cancellationToken)
    {
        var producer = NotificationTemplateCompiler.NormalizeStableKey(request.ProducerKey, "ProducerKey");
        var scene = NotificationTemplateCompiler.NormalizeStableKey(request.SceneKey, "SceneKey");
        if (!producer.IsSuccess || !scene.IsSuccess)
        {
            return ValidationFailed();
        }

        var declared = NormalizeDeclaration(
            request.PolicyCategoryKey,
            request.AllowedChannelKeys,
            request.EmergencyOverrideAllowed);
        if (!declared.IsSuccess)
        {
            return Result<NotificationProducerSceneResponse>.Failure(declared.Error!);
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var producerSceneId = idGenerator.NewId();
        var insert = scope.IsHost
            ? NotificationPlatformSql.InsertProducerSceneHost
            : NotificationPlatformSql.InsertProducerSceneTenant;
        var affected = await commandExecutor.ExecuteAsync(
                insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", producerSceneId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ProducerKey", producer.Value!),
                    ("SceneKey", scene.Value!),
                    ("PolicyCategoryKey", declared.Value!.PolicyCategoryKey),
                    ("AllowedChannelKeysJson", declared.Value.AllowedChannelKeysJson),
                    ("EmergencyOverrideAllowed", declared.Value.EmergencyOverrideAllowed),
                    ("CreatedById", actorUserId),
                    ("CreatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? Result<NotificationProducerSceneResponse>.Failure(new Error(
                NotificationsErrorCodes.ProducerSceneKeyConflict,
                "A catalog entry for this producer and scene already exists.",
                ErrorType.Conflict))
            : await GetByIdAsync(producerSceneId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationProducerSceneResponse>> UpdateCoreAsync(
        Guid producerSceneId,
        UpdateNotificationProducerSceneRequest request,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProducerSceneRecord>(
                NotificationPlatformSql.FindProducerSceneById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", producerSceneId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var declared = NormalizeDeclaration(
            request.PolicyCategoryKey,
            request.AllowedChannelKeys,
            request.EmergencyOverrideAllowed);
        if (!declared.IsSuccess)
        {
            return Result<NotificationProducerSceneResponse>.Failure(declared.Error!);
        }

        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.UpdateProducerScene,
                NotificationPlatformSqlParameters.Create(
                    ("Id", producerSceneId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("PolicyCategoryKey", declared.Value!.PolicyCategoryKey),
                    ("AllowedChannelKeysJson", declared.Value.AllowedChannelKeysJson),
                    ("EmergencyOverrideAllowed", declared.Value.EmergencyOverrideAllowed),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? ConcurrencyConflict()
            : await GetByIdAsync(producerSceneId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationProducerSceneResponse>> SetEnabledCoreAsync(
        Guid producerSceneId,
        SetNotificationProducerSceneEnabledRequest request,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProducerSceneRecord>(
                NotificationPlatformSql.FindProducerSceneById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", producerSceneId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.SetProducerSceneEnabled,
                NotificationPlatformSqlParameters.Create(
                    ("Id", producerSceneId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? ConcurrencyConflict()
            : await GetByIdAsync(producerSceneId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>校验政策类别与合法渠道声明，并生成确定性的渠道 JSON。</summary>
    private Result<ProducerSceneDeclaration> NormalizeDeclaration(
        string? policyCategoryKey,
        IReadOnlyList<string>? allowedChannelKeys,
        bool emergencyOverrideAllowed)
    {
        var category = policyCategoryKey?.Trim() ?? string.Empty;
        if (!NotificationProducerSceneCatalog.PolicyCategories.Contains(category))
        {
            return Result<ProducerSceneDeclaration>.Failure(new Error(
                NotificationsErrorCodes.ProducerSceneValidationFailed,
                "The policy category is invalid.",
                ErrorType.Validation));
        }

        if (allowedChannelKeys is null || allowedChannelKeys.Count == 0)
        {
            return Result<ProducerSceneDeclaration>.Failure(new Error(
                NotificationsErrorCodes.ProducerSceneValidationFailed,
                "At least one allowed channel is required.",
                ErrorType.Validation));
        }

        var channels = new List<string>(allowedChannelKeys.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var channel in allowedChannelKeys)
        {
            var normalized = channel?.Trim() ?? string.Empty;
            if (!NotificationProducerSceneCatalog.IsChannelDeclarable(normalized, providerTypeCatalog)
                || !seen.Add(normalized))
            {
                return Result<ProducerSceneDeclaration>.Failure(new Error(
                    NotificationsErrorCodes.ProducerSceneValidationFailed,
                    "The allowed channel list is invalid.",
                    ErrorType.Validation));
            }

            channels.Add(normalized);
        }

        channels.Sort(StringComparer.Ordinal);
        return Result<ProducerSceneDeclaration>.Success(
            new ProducerSceneDeclaration(
                category,
                NotificationProducerSceneCatalog.SerializeChannels(channels),
                emergencyOverrideAllowed));
    }

    private static NotificationProducerSceneResponse Map(NotificationProducerSceneRecord record) =>
        new(
            record.Id,
            record.ProducerKey,
            record.SceneKey,
            record.PolicyCategoryKey,
            NotificationProducerSceneCatalog.ParseChannels(record.AllowedChannelKeysJson),
            record.EmergencyOverrideAllowed,
            record.IsEnabled,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<NotificationProducerSceneResponse> NotFound() =>
        Result<NotificationProducerSceneResponse>.Failure(new Error(
            NotificationsErrorCodes.ProducerSceneNotFound,
            "The producer/scene catalog entry was not found.",
            ErrorType.NotFound));

    private static Result<NotificationProducerSceneResponse> ConcurrencyConflict() =>
        Result<NotificationProducerSceneResponse>.Failure(new Error(
            NotificationsErrorCodes.ProducerSceneConcurrencyConflict,
            "The producer/scene catalog entry changed concurrently.",
            ErrorType.Conflict));

    private static Result<NotificationProducerSceneResponse> ValidationFailed() =>
        Result<NotificationProducerSceneResponse>.Failure(new Error(
            NotificationsErrorCodes.ProducerSceneValidationFailed,
            "The producer or scene key is invalid.",
            ErrorType.Validation));

    /// <summary>已校验的目录声明中间值；渠道 JSON 已按序规范化。</summary>
    private sealed record ProducerSceneDeclaration(
        string PolicyCategoryKey,
        string AllowedChannelKeysJson,
        bool EmergencyOverrideAllowed);
}
