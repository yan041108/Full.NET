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

namespace Full.NET.Modules.Notifications.Features.ManageProviderProfiles;

/// <summary>当前作用域渠道配置的草稿、发布与启停；密钥只存 Reference，读取只返回状态。</summary>
internal sealed class NotificationProviderProfileService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    INotificationProviderTypeCatalog catalog,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<IReadOnlyList<NotificationProviderTypeDescriptor>>> ListTypesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Result<IReadOnlyList<NotificationProviderTypeDescriptor>>.Success(catalog.All);
    }

    public async Task<Result<PagedResult<NotificationProviderProfileResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountProfilesForScope,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? NotificationPlatformSql.ListProfilesMySql
            : NotificationPlatformSql.ListProfilesSqlServer;
        var rows = await queryExecutor.QueryAsync<NotificationProviderProfileRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = new List<NotificationProviderProfileResponse>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(await MapAsync(row, includeLatestVersion: false, cancellationToken).ConfigureAwait(false));
        }

        return Result<PagedResult<NotificationProviderProfileResponse>>.Success(
            new PagedResult<NotificationProviderProfileResponse>(items, page, pageSize, total));
    }

    public async Task<Result<NotificationProviderProfileResponse>> GetByIdAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var record = await FindAsync(profileId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<NotificationProviderProfileResponse>.Success(
                await MapAsync(record, includeLatestVersion: true, cancellationToken).ConfigureAwait(false));
    }

    public Task<Result<NotificationProviderProfileResponse>> CreateAsync(
        Guid actorUserId,
        CreateNotificationProviderProfileRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<NotificationProviderProfileResponse>> UpdateAsync(
        Guid actorUserId,
        Guid profileId,
        UpdateNotificationProviderProfileRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(profileId, request, token),
            cancellationToken);

    public Task<Result<NotificationProviderProfileResponse>> PublishAsync(
        Guid actorUserId,
        Guid profileId,
        PublishNotificationProviderProfileRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => PublishCoreAsync(actorUserId, profileId, request, token),
            cancellationToken);

    public Task<Result<NotificationProviderProfileResponse>> SetEnabledAsync(
        Guid actorUserId,
        Guid profileId,
        bool isEnabled,
        SetNotificationProviderProfileEnabledRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => SetEnabledCoreAsync(actorUserId, profileId, isEnabled, request, token),
            cancellationToken);

    private async Task<Result<NotificationProviderProfileResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateNotificationProviderProfileRequest request,
        CancellationToken cancellationToken)
    {
        var prepared = PrepareDraft(request.ProfileKey, request.ProviderTypeKey, request.NonSecretConfig, request.SecretReference);
        if (!prepared.IsSuccess)
        {
            return Result<NotificationProviderProfileResponse>.Failure(prepared.Error!);
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var profileId = idGenerator.NewId();
        var insert = scope.IsHost
            ? NotificationPlatformSql.InsertProfileHost
            : NotificationPlatformSql.InsertProfileTenant;
        var affected = await commandExecutor.ExecuteAsync(
                insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", profileId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("ProfileKey", prepared.Value!.ProfileKey),
                    ("ProviderTypeKey", prepared.Value.ProviderTypeKey),
                    ("NonSecretConfigJson", prepared.Value.ConfigJson),
                    ("SecretReference", ToDbSecret(prepared.Value.SecretReference)),
                    ("CreatedById", actorUserId),
                    ("CreatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? Result<NotificationProviderProfileResponse>.Failure(new Error(
                NotificationsErrorCodes.ProviderProfileKeyConflict,
                "A provider profile with this key already exists.",
                ErrorType.Conflict))
            : await GetByIdAsync(profileId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationProviderProfileResponse>> UpdateCoreAsync(
        Guid profileId,
        UpdateNotificationProviderProfileRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        // 客户端读取不到已保存引用；null 必须表示保留，否则普通配置编辑会静默清除密钥。
        var effectiveSecretReference = request.SecretReference is null
            ? existing.SecretReference
            : request.SecretReference;
        var prepared = PrepareDraft(
            existing.ProfileKey,
            existing.ProviderTypeKey,
            request.NonSecretConfig,
            effectiveSecretReference);
        if (!prepared.IsSuccess)
        {
            return Result<NotificationProviderProfileResponse>.Failure(prepared.Error!);
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.UpdateProfileDraft,
                NotificationPlatformSqlParameters.Create(
                    ("Id", profileId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("NonSecretConfigJson", prepared.Value!.ConfigJson),
                    ("SecretReference", ToDbSecret(prepared.Value.SecretReference)),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("NextVersion", request.Version + 1),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? ConcurrencyConflict()
            : await GetByIdAsync(profileId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationProviderProfileResponse>> PublishCoreAsync(
        Guid actorUserId,
        Guid profileId,
        PublishNotificationProviderProfileRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (!catalog.TryGet(existing.ProviderTypeKey, out var descriptor))
        {
            return Result<NotificationProviderProfileResponse>.Failure(new Error(
                NotificationsErrorCodes.ProviderTypeUnknown,
                "The provider type is not registered.",
                ErrorType.Validation));
        }

        var secret = existing.SecretReference ?? string.Empty;
        var hash = NotificationProfileCompiler.ComputeProfileHash(
            existing.ProviderTypeKey,
            descriptor.AdapterVersion,
            existing.NonSecretConfigJson,
            secret);
        var versionRecord = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                NotificationPlatformSql.FindProfileVersionByHash,
                NotificationPlatformSqlParameters.Create(
                    ("ProfileId", profileId),
                    ("ContentHash", hash)),
                cancellationToken)
            .ConfigureAwait(false);
        if (versionRecord is null)
        {
            var nextNumber = (int)await queryExecutor.QuerySingleOrDefaultAsync<long>(
                    NotificationPlatformSql.CountProfileVersions,
                    NotificationPlatformSqlParameters.Create(("ProfileId", profileId)),
                    cancellationToken)
                .ConfigureAwait(false) + 1;
            var versionId = idGenerator.NewId();
            var inserted = await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.InsertProfileVersion,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", versionId),
                        ("ProfileId", profileId),
                        ("VersionNumber", nextNumber),
                        ("ProviderTypeKey", existing.ProviderTypeKey),
                        ("AdapterVersion", descriptor.AdapterVersion),
                        ("NonSecretConfigJson", existing.NonSecretConfigJson),
                        ("SecretReference", ToDbSecret(secret)),
                        ("ContentHash", hash),
                        ("PublishedById", actorUserId),
                        ("PublishedAtUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false);
            versionRecord = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                    NotificationPlatformSql.FindProfileVersionByHash,
                    NotificationPlatformSqlParameters.Create(
                        ("ProfileId", profileId),
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
                NotificationPlatformSql.PublishProfile,
                NotificationPlatformSqlParameters.Create(
                    ("Id", profileId),
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

        await WriteAuditAsync(scope, actorUserId, profileId, "profile.publish", cancellationToken)
            .ConfigureAwait(false);
        return await GetByIdAsync(profileId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationProviderProfileResponse>> SetEnabledCoreAsync(
        Guid actorUserId,
        Guid profileId,
        bool isEnabled,
        SetNotificationProviderProfileEnabledRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.SetProfileEnabled,
                NotificationPlatformSqlParameters.Create(
                    ("Id", profileId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("IsEnabled", isEnabled),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("NextVersion", request.Version + 1),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyConflict();
        }

        await WriteAuditAsync(
                scope,
                actorUserId,
                profileId,
                isEnabled ? "profile.enable" : "profile.disable",
                cancellationToken)
            .ConfigureAwait(false);
        return await GetByIdAsync(profileId, cancellationToken).ConfigureAwait(false);
    }

    private Result<PreparedProfileDraft> PrepareDraft(
        string profileKey,
        string providerTypeKey,
        JsonElement config,
        string? secretReference)
    {
        var key = NotificationTemplateCompiler.NormalizeStableKey(profileKey, "ProfileKey");
        if (!key.IsSuccess)
        {
            return Result<PreparedProfileDraft>.Failure(key.Error!);
        }

        var typeKey = providerTypeKey?.Trim() ?? string.Empty;
        if (!catalog.TryGet(typeKey, out var descriptor))
        {
            return Result<PreparedProfileDraft>.Failure(new Error(
                NotificationsErrorCodes.ProviderTypeUnknown,
                "The provider type is not registered.",
                ErrorType.Validation));
        }

        var configJson = NotificationProfileCompiler.NormalizeNonSecretConfig(descriptor, config);
        if (!configJson.IsSuccess)
        {
            return Result<PreparedProfileDraft>.Failure(configJson.Error!);
        }

        var secret = NotificationProfileCompiler.NormalizeSecretReference(secretReference);
        return !secret.IsSuccess
            ? Result<PreparedProfileDraft>.Failure(secret.Error!)
            : Result<PreparedProfileDraft>.Success(
                new PreparedProfileDraft(key.Value!, typeKey, configJson.Value!, secret.Value!));
    }

    private Task<NotificationProviderProfileRecord?> FindAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        return queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileRecord>(
            NotificationPlatformSql.FindProfileById,
            NotificationPlatformSqlParameters.Create(
                ("Id", profileId),
                ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken);
    }

    private async Task<NotificationProviderProfileResponse> MapAsync(
        NotificationProviderProfileRecord record,
        bool includeLatestVersion,
        CancellationToken cancellationToken)
    {
        NotificationProviderProfileVersionRecord? version = null;
        if (includeLatestVersion && record.LatestPublishedVersionId is { } versionId)
        {
            version = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                    NotificationPlatformSql.FindProfileVersionById,
                    NotificationPlatformSqlParameters.Create(("Id", versionId)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new NotificationProviderProfileResponse(
            record.Id,
            record.ProfileKey,
            record.ProviderTypeKey,
            record.NonSecretConfigJson,
            NotificationProfileCompiler.SecretStatus(record.SecretReference),
            record.IsEnabled,
            record.DraftRevision,
            record.LatestPublishedVersionId,
            version?.VersionNumber,
            version?.AdapterVersion,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);
    }

    private Task WriteAuditAsync(
        NotificationInboxScope scope,
        Guid actorUserId,
        Guid resourceId,
        string operationKey,
        CancellationToken cancellationToken) =>
        commandExecutor.ExecuteAsync(
            scope.IsHost
                ? NotificationPlatformSql.InsertDomainAuditHost
                : NotificationPlatformSql.InsertDomainAuditTenant,
            NotificationPlatformSqlParameters.Create(
                ("Id", idGenerator.NewId()),
                ("OperationKey", operationKey),
                ("ActorUserId", actorUserId),
                ("ResourceTypeKey", "provider_profile"),
                ("ResourceId", resourceId),
                ("OutcomeKey", "succeeded"),
                ("DetailJson", "{}"),
                ("CreatedAtUtc", clock.UtcNow)),
            cancellationToken);

    private static object? ToDbSecret(string secretReference) =>
        string.IsNullOrEmpty(secretReference) ? null : secretReference;

    private static Result<NotificationProviderProfileResponse> NotFound() =>
        Result<NotificationProviderProfileResponse>.Failure(new Error(
            NotificationsErrorCodes.ProviderProfileNotFound,
            "The provider profile was not found.",
            ErrorType.NotFound));

    private static Result<NotificationProviderProfileResponse> ConcurrencyConflict() =>
        Result<NotificationProviderProfileResponse>.Failure(new Error(
            NotificationsErrorCodes.ProviderProfileConcurrencyConflict,
            "The provider profile changed concurrently. Refresh and try again.",
            ErrorType.Conflict));

    private sealed record PreparedProfileDraft(
        string ProfileKey,
        string ProviderTypeKey,
        string ConfigJson,
        string SecretReference);
}
