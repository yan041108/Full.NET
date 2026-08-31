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

namespace Full.NET.Modules.Notifications.Features.ManageTemplates;

/// <summary>当前作用域模板草稿的查询、CAS 更新与不可变版本发布。</summary>
internal sealed class NotificationTemplateService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    INotificationProviderTypeCatalog catalog,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<NotificationTemplateResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                NotificationPlatformSql.CountForScope,
                NotificationPlatformSqlParameters.Create(("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<NotificationTemplateListRecord>(
                ResolveListStatement(),
                NotificationPlatformSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(MapListRecord).ToArray();

        return Result<PagedResult<NotificationTemplateResponse>>.Success(
            new PagedResult<NotificationTemplateResponse>(items, page, pageSize, total));
    }

    public async Task<Result<NotificationTemplateResponse>> GetByIdAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var record = await FindTemplateAsync(templateId, cancellationToken).ConfigureAwait(false);
        return record is null
            ? NotFound()
            : Result<NotificationTemplateResponse>.Success(
                await MapAsync(record, includeLatestVersion: true, cancellationToken).ConfigureAwait(false));
    }

    public Task<Result<NotificationTemplateResponse>> CreateAsync(
        Guid actorUserId,
        CreateNotificationTemplateRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(actorUserId, request, token),
            cancellationToken);

    public Task<Result<NotificationTemplateResponse>> UpdateAsync(
        Guid actorUserId,
        Guid templateId,
        UpdateNotificationTemplateRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(actorUserId, templateId, request, token),
            cancellationToken);

    public Task<Result<NotificationTemplateResponse>> PublishAsync(
        Guid actorUserId,
        Guid templateId,
        PublishNotificationTemplateRequest request,
        CancellationToken cancellationToken) =>
        transaction.ExecuteResultAsync(
            token => PublishCoreAsync(actorUserId, templateId, request, token),
            cancellationToken);

    private async Task<Result<NotificationTemplateResponse>> CreateCoreAsync(
        Guid actorUserId,
        CreateNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var key = NotificationTemplateCompiler.NormalizeStableKey(request.TemplateKey, "TemplateKey");
        if (!key.IsSuccess)
        {
            return Result<NotificationTemplateResponse>.Failure(key.Error!);
        }

        var channel = NotificationTemplateCompiler.NormalizeChannel(request.ChannelKey, catalog);
        if (!channel.IsSuccess)
        {
            return Result<NotificationTemplateResponse>.Failure(channel.Error!);
        }

        var category = NotificationTemplateCompiler.NormalizeContentCategory(request.ContentCategoryKey);
        if (!category.IsSuccess)
        {
            return Result<NotificationTemplateResponse>.Failure(category.Error!);
        }

        var draft = NotificationTemplateCompiler.NormalizeDraft(
            request.DraftSubject,
            request.DraftBody,
            request.ParameterSchema);
        if (!draft.IsSuccess)
        {
            return Result<NotificationTemplateResponse>.Failure(draft.Error!);
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var now = clock.UtcNow;
        var templateId = idGenerator.NewId();
        var insert = scope.IsHost
            ? NotificationPlatformSql.InsertTemplateHost
            : NotificationPlatformSql.InsertTemplateTenant;
        var affected = await commandExecutor.ExecuteAsync(
                insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", templateId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("TemplateKey", key.Value!),
                    ("ChannelKey", channel.Value!),
                    ("ContentCategoryKey", category.Value!),
                    ("DraftSubject", draft.Value!.Subject),
                    ("DraftBodyJson", draft.Value.BodyJson),
                    ("DraftParameterSchemaJson", draft.Value.ParameterSchemaJson),
                    ("CreatedById", actorUserId),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<NotificationTemplateResponse>.Failure(new Error(
                NotificationsErrorCodes.TemplateKeyConflict,
                "A template with this key already exists.",
                ErrorType.Conflict));
        }

        return await GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationTemplateResponse>> UpdateCoreAsync(
        Guid actorUserId,
        Guid templateId,
        UpdateNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindTemplateAsync(templateId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var draft = NotificationTemplateCompiler.NormalizeDraft(
            request.DraftSubject,
            request.DraftBody,
            request.ParameterSchema);
        if (!draft.IsSuccess)
        {
            return Result<NotificationTemplateResponse>.Failure(draft.Error!);
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.UpdateDraft,
                NotificationPlatformSqlParameters.Create(
                    ("Id", templateId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("DraftSubject", draft.Value!.Subject),
                    ("DraftBodyJson", draft.Value.BodyJson),
                    ("DraftParameterSchemaJson", draft.Value.ParameterSchemaJson),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("NextVersion", request.Version + 1),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? ConcurrencyConflict()
            : await GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<NotificationTemplateResponse>> PublishCoreAsync(
        Guid actorUserId,
        Guid templateId,
        PublishNotificationTemplateRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await FindTemplateAsync(templateId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var classification = NotificationTemplateCompiler.NormalizeClassification(
            request.ContentClassificationKey);
        if (!classification.IsSuccess)
        {
            return Result<NotificationTemplateResponse>.Failure(classification.Error!);
        }

        var schema = DeserializeSchema(existing.DraftParameterSchemaJson);
        var draft = NotificationTemplateCompiler.NormalizeDraft(
            existing.DraftSubject,
            new NotificationTemplateBody(ReadBodyText(existing.DraftBodyJson)),
            schema);
        if (!draft.IsSuccess)
        {
            return Result<NotificationTemplateResponse>.Failure(draft.Error!);
        }

        var contentHash = NotificationTemplateCompiler.ComputeContentHash(
            draft.Value!.Subject,
            draft.Value.BodyJson,
            draft.Value.ParameterSchemaJson,
            classification.Value!);
        var versionRecord = await queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateVersionRecord>(
                NotificationPlatformSql.FindTemplateVersionByHash,
                NotificationPlatformSqlParameters.Create(
                    ("TemplateId", templateId),
                    ("ContentHash", contentHash)),
                cancellationToken)
            .ConfigureAwait(false);
        if (versionRecord is null)
        {
            var nextNumber = await queryExecutor.QuerySingleOrDefaultAsync<int>(
                    NotificationPlatformSql.MaxTemplateVersionNumber,
                    NotificationPlatformSqlParameters.Create(("TemplateId", templateId)),
                    cancellationToken)
                .ConfigureAwait(false) + 1;
            var versionId = idGenerator.NewId();
            var inserted = await commandExecutor.ExecuteAsync(
                    NotificationPlatformSql.InsertTemplateVersion,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", versionId),
                        ("TemplateId", templateId),
                        ("VersionNumber", nextNumber),
                        ("SchemaVersion", NotificationTemplateCompiler.SchemaVersion),
                        ("Subject", draft.Value.Subject),
                        ("BodyJson", draft.Value.BodyJson),
                        ("ParameterSchemaJson", draft.Value.ParameterSchemaJson),
                        ("ContentClassificationKey", classification.Value!),
                        ("ContentHash", contentHash),
                        ("PublishedById", actorUserId),
                        ("PublishedAtUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false);
            versionRecord = await queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateVersionRecord>(
                    NotificationPlatformSql.FindTemplateVersionByHash,
                    NotificationPlatformSqlParameters.Create(
                        ("TemplateId", templateId),
                        ("ContentHash", contentHash)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (inserted == 0 || versionRecord is null)
            {
                return Result<NotificationTemplateResponse>.Failure(new Error(
                    NotificationsErrorCodes.TemplateConcurrencyConflict,
                    "The template changed concurrently. Refresh and try again.",
                    ErrorType.Conflict));
            }
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var affected = await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.PublishTemplate,
                NotificationPlatformSqlParameters.Create(
                    ("Id", templateId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("LatestPublishedVersionId", versionRecord.Id),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("NextVersion", request.Version + 1),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 0
            ? ConcurrencyConflict()
            : await GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
    }

    private Task<NotificationTemplateRecord?> FindTemplateAsync(
        Guid templateId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        return queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateRecord>(
            NotificationPlatformSql.FindTemplateById,
            NotificationPlatformSqlParameters.Create(
                ("Id", templateId),
                ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken);
    }

    private async Task<NotificationTemplateResponse> MapAsync(
        NotificationTemplateRecord record,
        bool includeLatestVersion,
        CancellationToken cancellationToken)
    {
        NotificationTemplateVersionRecord? version = null;
        if (includeLatestVersion && record.LatestPublishedVersionId is { } versionId)
        {
            version = await queryExecutor.QuerySingleOrDefaultAsync<NotificationTemplateVersionRecord>(
                    NotificationPlatformSql.FindTemplateVersionById,
                    NotificationPlatformSqlParameters.Create(("Id", versionId)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new NotificationTemplateResponse(
            record.Id,
            record.TemplateKey,
            record.ChannelKey,
            record.ContentCategoryKey,
            record.DraftSubject,
            record.DraftBodyJson,
            record.DraftParameterSchemaJson,
            record.DraftRevision,
            record.LatestPublishedVersionId,
            version?.VersionNumber,
            version?.ContentHash,
            version?.ContentClassificationKey,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);
    }

    private static NotificationTemplateResponse MapListRecord(NotificationTemplateListRecord record) =>
        new(
            record.Id,
            record.TemplateKey,
            record.ChannelKey,
            record.ContentCategoryKey,
            record.DraftSubject,
            record.DraftBodyJson,
            record.DraftParameterSchemaJson,
            record.DraftRevision,
            record.LatestPublishedVersionId,
            record.LatestPublishedVersionNumber,
            record.LatestContentHash,
            record.LatestContentClassificationKey,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private SqlStatement ResolveListStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => NotificationPlatformSql.ListForScopeSqlServer,
            DatabaseProvider.MySql => NotificationPlatformSql.ListForScopeMySql,
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{databaseOptions.Value.Provider}'."),
        };

    private static NotificationTemplateParameterSchema DeserializeSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
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

        return new NotificationTemplateParameterSchema(schemaVersion, parameters);
    }

    private static string ReadBodyText(string bodyJson)
    {
        using var document = JsonDocument.Parse(bodyJson);
        return document.RootElement.TryGetProperty("text", out var text)
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }

    private static Result<NotificationTemplateResponse> NotFound() =>
        Result<NotificationTemplateResponse>.Failure(new Error(
            NotificationsErrorCodes.TemplateNotFound,
            "The notification template was not found.",
            ErrorType.NotFound));

    private static Result<NotificationTemplateResponse> ConcurrencyConflict() =>
        Result<NotificationTemplateResponse>.Failure(new Error(
            NotificationsErrorCodes.TemplateConcurrencyConflict,
            "The template changed concurrently. Refresh and try again.",
            ErrorType.Conflict));
}
