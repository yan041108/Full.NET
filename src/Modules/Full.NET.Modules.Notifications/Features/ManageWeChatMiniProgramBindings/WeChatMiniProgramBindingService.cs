using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Features;
using Full.NET.Modules.Notifications.Features.ManageRecipientEndpoints;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Full.NET.Modules.Notifications.Providers.WeChatMiniProgram;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.ManageWeChatMiniProgramBindings;

/// <summary>微信小程序 OpenId 绑定与订阅授权服务；订阅投递仍受 Profile 与端点闭包约束。</summary>
internal sealed class WeChatMiniProgramBindingService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    INotificationSecretResolver secretResolver,
    IWeChatMiniProgramTransport transport,
    NotificationRecipientEndpointProtector protector,
    RecipientEndpointStore recipientEndpointStore,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<WeChatMiniProgramBindingResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WeChatMiniProgramBindingSql.CountForScope,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? WeChatMiniProgramBindingSql.ListForScopeMySql
            : WeChatMiniProgramBindingSql.ListForScopeSqlServer;
        var rows = await queryExecutor.QueryAsync<WeChatMiniProgramBindingRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = await MapManyAsync(rows, cancellationToken).ConfigureAwait(false);
        return Result<PagedResult<WeChatMiniProgramBindingResponse>>.Success(
            new PagedResult<WeChatMiniProgramBindingResponse>(items, page, pageSize, total));
    }

    public async Task<Result<IReadOnlyList<WeChatMiniProgramBindingResponse>>> ListMineAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var rows = await queryExecutor.QueryAsync<WeChatMiniProgramBindingRecord>(
                WeChatMiniProgramBindingSql.ListMineForScope,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId),
                    ("Revoked", WeChatMiniProgramBindingStatusKeys.Revoked)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = await MapManyAsync(rows, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<WeChatMiniProgramBindingResponse>>.Success(items);
    }

    public Task<Result<WeChatMiniProgramBindingResponse>> ExchangeCodeAsync(
        Guid userId,
        ExchangeWeChatMiniProgramBindingRequest request,
        CancellationToken cancellationToken) =>
        ExchangeCoreAsync(userId, request, cancellationToken);

    public Task<Result<WeChatMiniProgramBindingResponse>> RecordSubscriptionAsync(
        Guid userId,
        string appId,
        RecordWeChatMiniProgramSubscriptionRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            async token =>
            {
                var scope = NotificationInboxScope.Resolve(currentTenant);
                var normalizedAppId = appId?.Trim() ?? string.Empty;
                var templateId = request.TemplateId?.Trim() ?? string.Empty;
                var statusKey = request.StatusKey?.Trim() ?? string.Empty;
                if (normalizedAppId.Length is < 1 or > 32
                    || templateId.Length is < 1 or > 128
                    || statusKey is not (
                        WeChatMiniProgramSubscriptionStatusKeys.Accepted
                        or WeChatMiniProgramSubscriptionStatusKeys.Rejected))
                {
                    return Result<WeChatMiniProgramBindingResponse>.Failure(ValidationFailed());
                }

                var binding = await queryExecutor.QuerySingleOrDefaultAsync<WeChatMiniProgramBindingRecord>(
                        WeChatMiniProgramBindingSql.FindByUserApp,
                        NotificationPlatformSqlParameters.Create(
                            ("TenantScopeKey", scope.TenantScopeKey),
                            ("UserId", userId),
                            ("AppId", normalizedAppId)),
                        token)
                    .ConfigureAwait(false);
                if (binding is null
                    || binding.VerificationStatusKey == WeChatMiniProgramBindingStatusKeys.Revoked)
                {
                    return Result<WeChatMiniProgramBindingResponse>.Failure(NotFound());
                }

                var upsertStatement = databaseOptions.Value.Provider == DatabaseProvider.MySql
                    ? WeChatMiniProgramBindingSql.UpsertSubscriptionMySql
                    : WeChatMiniProgramBindingSql.UpsertSubscription;
                var now = clock.UtcNow;
                await commandExecutor.ExecuteAsync(
                        upsertStatement,
                        NotificationPlatformSqlParameters.Create(
                            ("BindingId", binding.Id),
                            ("TemplateId", templateId),
                            ("StatusKey", statusKey),
                            ("AuthorizedAtUtc", now)),
                        token)
                    .ConfigureAwait(false);
                return await MapSingleAsync(binding, token).ConfigureAwait(false);
            },
            cancellationToken);

    private async Task<Result<WeChatMiniProgramBindingResponse>> ExchangeCoreAsync(
        Guid userId,
        ExchangeWeChatMiniProgramBindingRequest request,
        CancellationToken cancellationToken)
    {
        var jsCode = request.JsCode?.Trim() ?? string.Empty;
        if (jsCode.Length is < 8 or > 128)
        {
            return Result<WeChatMiniProgramBindingResponse>.Failure(ValidationFailed());
        }

        var profileResult = await ResolvePublishedProfileAsync(
                request.ProviderProfileVersionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!profileResult.IsSuccess || profileResult.Value is null)
        {
            return Result<WeChatMiniProgramBindingResponse>.Failure(profileResult.Error!);
        }

        var profile = profileResult.Value;
        if (!WeChatMiniProgramNotificationProviderAdapter.TryParseConfig(
                profile.NonSecretConfigJson,
                out var config)
            || config is null)
        {
            return Result<WeChatMiniProgramBindingResponse>.Failure(ValidationFailed());
        }

        var appSecret = await secretResolver.ResolveAsync(profile.SecretReference, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(appSecret))
        {
            return Result<WeChatMiniProgramBindingResponse>.Failure(ValidationFailed());
        }

        WeChatMiniProgramSession session;
        try
        {
            session = await transport
                .ExchangeJsCodeAsync(config.AppId, appSecret, jsCode, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WeChatMiniProgramTransportException)
        {
            return Result<WeChatMiniProgramBindingResponse>.Failure(ValidationFailed());
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var existingEndpointId = await queryExecutor.QuerySingleOrDefaultAsync<Guid>(
                NotificationRecipientEndpointSql.FindIdByUserProfileKind,
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId),
                    ("ProviderProfileVersionId", request.ProviderProfileVersionId),
                    ("EndpointKindKey", "wechat_miniprogram")),
                cancellationToken)
            .ConfigureAwait(false);
        Guid recipientEndpointId;
        if (existingEndpointId != Guid.Empty)
        {
            var now = clock.UtcNow;
            var affected = await commandExecutor.ExecuteAsync(
                    NotificationRecipientEndpointSql.RebindVerifiedValue,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", existingEndpointId),
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("UserId", userId),
                        ("EndpointKindKey", "wechat_miniprogram"),
                        ("ProtectedValue", protector.Protect(session.OpenId)),
                        ("MaskedValue", NotificationRecipientEndpointMasker.Mask(session.OpenId, "wechat_miniprogram")),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected == 0)
            {
                return Result<WeChatMiniProgramBindingResponse>.Failure(ValidationFailed());
            }

            recipientEndpointId = existingEndpointId;
        }
        else
        {
            var endpointResult = await recipientEndpointStore.UpsertAsync(
                    userId,
                    request.ProviderProfileVersionId,
                    "wechat_miniprogram",
                    session.OpenId,
                    NotificationRecipientEndpointStatuses.Verified,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!endpointResult.IsSuccess)
            {
                return Result<WeChatMiniProgramBindingResponse>.Failure(endpointResult.Error!);
            }

            recipientEndpointId = endpointResult.Value!.Id;
        }

        return await transaction.ExecuteResultAsync(
            async token =>
            {
                var scope = NotificationInboxScope.Resolve(currentTenant);
                var now = clock.UtcNow;
                var fingerprint = WeChatMiniProgramOpenIdFingerprint.Compute(session.OpenId);
                var existing = await queryExecutor.QuerySingleOrDefaultAsync<WeChatMiniProgramBindingRecord>(
                        WeChatMiniProgramBindingSql.FindByUserApp,
                        NotificationPlatformSqlParameters.Create(
                            ("TenantScopeKey", scope.TenantScopeKey),
                            ("UserId", userId),
                            ("AppId", config.AppId)),
                        token)
                    .ConfigureAwait(false);

                string? unionIdProtected = null;
                string? unionIdMask = null;
                if (!string.IsNullOrEmpty(session.UnionId))
                {
                    unionIdProtected = protector.Protect(session.UnionId);
                    unionIdMask = NotificationRecipientEndpointMasker.Mask(session.UnionId, "wechat_miniprogram");
                }

                if (existing is null)
                {
                    var id = idGenerator.NewId();
                    await commandExecutor.ExecuteAsync(
                            WeChatMiniProgramBindingSql.Insert,
                            NotificationPlatformSqlParameters.Create(
                                ("Id", id),
                                ("TenantScopeKey", scope.TenantScopeKey),
                                ("UserId", userId),
                                ("AppId", config.AppId),
                                ("ProviderProfileVersionId", request.ProviderProfileVersionId),
                                ("OpenIdProtected", protector.Protect(session.OpenId)),
                                ("OpenIdMask", NotificationRecipientEndpointMasker.Mask(session.OpenId, "wechat_miniprogram")),
                                ("OpenIdSha256Hex", fingerprint),
                                ("UnionIdProtected", unionIdProtected),
                                ("UnionIdMask", unionIdMask),
                                ("VerificationStatusKey", WeChatMiniProgramBindingStatusKeys.Verified),
                                ("RecipientEndpointId", recipientEndpointId),
                                ("CreatedAtUtc", now),
                                ("UpdatedAtUtc", now)),
                            token)
                        .ConfigureAwait(false);
                    var created = await queryExecutor.QuerySingleOrDefaultAsync<WeChatMiniProgramBindingRecord>(
                            WeChatMiniProgramBindingSql.FindById,
                            NotificationPlatformSqlParameters.Create(
                                ("Id", id),
                                ("TenantScopeKey", scope.TenantScopeKey)),
                            token)
                        .ConfigureAwait(false);
                    return created is null
                        ? Result<WeChatMiniProgramBindingResponse>.Failure(ValidationFailed())
                        : await MapSingleAsync(created, token).ConfigureAwait(false);
                }

                await commandExecutor.ExecuteAsync(
                        WeChatMiniProgramBindingSql.Update,
                        NotificationPlatformSqlParameters.Create(
                            ("Id", existing.Id),
                            ("TenantScopeKey", scope.TenantScopeKey),
                            ("OpenIdProtected", protector.Protect(session.OpenId)),
                            ("OpenIdMask", NotificationRecipientEndpointMasker.Mask(session.OpenId, "wechat_miniprogram")),
                            ("OpenIdSha256Hex", fingerprint),
                            ("UnionIdProtected", unionIdProtected),
                            ("UnionIdMask", unionIdMask),
                            ("VerificationStatusKey", WeChatMiniProgramBindingStatusKeys.Verified),
                            ("ProviderProfileVersionId", request.ProviderProfileVersionId),
                            ("RecipientEndpointId", recipientEndpointId),
                            ("UpdatedAtUtc", now)),
                        token)
                    .ConfigureAwait(false);
                var updated = await queryExecutor.QuerySingleOrDefaultAsync<WeChatMiniProgramBindingRecord>(
                        WeChatMiniProgramBindingSql.FindById,
                        NotificationPlatformSqlParameters.Create(
                            ("Id", existing.Id),
                            ("TenantScopeKey", scope.TenantScopeKey)),
                        token)
                    .ConfigureAwait(false);
                return updated is null
                    ? Result<WeChatMiniProgramBindingResponse>.Failure(ValidationFailed())
                    : await MapSingleAsync(updated, token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationProviderProfileVersionRecord>> ResolvePublishedProfileAsync(
        Guid providerProfileVersionId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        var providerTypeKey = await queryExecutor.QuerySingleOrDefaultAsync<string>(
                NotificationRecipientEndpointSql.FindPublishedProviderTypeForScope,
                NotificationPlatformSqlParameters.Create(
                    ("ProviderProfileVersionId", providerProfileVersionId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                providerTypeKey,
                WeChatMiniProgramNotificationProviderAdapter.ProviderTypeKeyValue,
                StringComparison.Ordinal))
        {
            return Result<NotificationProviderProfileVersionRecord>.Failure(new Error(
                NotificationsErrorCodes.ProviderProfileNotFound,
                "The provider profile was not found in the current scope.",
                ErrorType.NotFound));
        }

        var profile = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                NotificationPlatformSql.FindProfileVersionById,
                NotificationPlatformSqlParameters.Create(("Id", providerProfileVersionId)),
                cancellationToken)
            .ConfigureAwait(false);
        return profile is null
            ? Result<NotificationProviderProfileVersionRecord>.Failure(new Error(
                NotificationsErrorCodes.ProviderProfileNotFound,
                "The provider profile was not found.",
                ErrorType.NotFound))
            : Result<NotificationProviderProfileVersionRecord>.Success(profile);
    }

    private async Task<IReadOnlyList<WeChatMiniProgramBindingResponse>> MapManyAsync(
        IEnumerable<WeChatMiniProgramBindingRecord> rows,
        CancellationToken cancellationToken)
    {
        var list = new List<WeChatMiniProgramBindingResponse>();
        foreach (var row in rows)
        {
            var mapped = await MapSingleAsync(row, cancellationToken).ConfigureAwait(false);
            if (mapped.IsSuccess && mapped.Value is not null)
            {
                list.Add(mapped.Value);
            }
        }

        return list;
    }

    private async Task<Result<WeChatMiniProgramBindingResponse>> MapSingleAsync(
        WeChatMiniProgramBindingRecord row,
        CancellationToken cancellationToken)
    {
        var subscriptions = await queryExecutor.QueryAsync<WeChatMiniProgramSubscriptionRecord>(
                WeChatMiniProgramBindingSql.ListSubscriptionsByBinding,
                NotificationPlatformSqlParameters.Create(("BindingId", row.Id)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<WeChatMiniProgramBindingResponse>.Success(new WeChatMiniProgramBindingResponse(
            row.Id,
            row.UserId,
            row.AppId,
            row.ProviderProfileVersionId,
            row.OpenIdMask,
            row.VerificationStatusKey,
            row.RecipientEndpointId,
            subscriptions
                .Select(item => new WeChatMiniProgramSubscriptionResponse(
                    item.TemplateId,
                    item.StatusKey,
                    item.AuthorizedAtUtc))
                .ToArray(),
            row.CreatedAtUtc,
            row.UpdatedAtUtc));
    }

    private static Error ValidationFailed() =>
        new(
            NotificationsErrorCodes.WeChatMiniProgramBindingValidationFailed,
            "The WeChat mini program binding request was invalid.",
            ErrorType.Validation);

    private static Error NotFound() =>
        new(
            NotificationsErrorCodes.WeChatMiniProgramBindingNotFound,
            "The WeChat mini program binding was not found.",
            ErrorType.NotFound);
}
