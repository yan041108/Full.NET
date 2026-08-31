using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Notifications.Persistence;

/// <summary>
/// 通知平台参数化 SQL；跨 Host/Tenant 查询必须显式携带 TenantScopeKey。
/// TenantRequired 的 INSERT...SELECT 必须在 WHERE 中写 <c>TenantId = @TenantId</c>，
/// 仅把 <c>@TenantId</c> 放在 SELECT 列表会被 SqlScopeGuard 拒绝。
/// </summary>
internal static class NotificationPlatformSql
{
    public static readonly SqlStatement FindTemplateById = new(
        "notifications.platform.template.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, ChannelKey,
               ContentCategoryKey, DraftSubject, DraftBodyJson, DraftParameterSchemaJson,
               DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_template
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindTemplateByKey = new(
        "notifications.platform.template.find_by_key",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, ChannelKey,
               ContentCategoryKey, DraftSubject, DraftBodyJson, DraftParameterSchemaJson,
               DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_template
        WHERE TenantScopeKey = @TenantScopeKey
          AND TemplateKey = @TemplateKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountForScope = new(
        "notifications.platform.template.count_for_scope",
        """
        SELECT COUNT(*)
        FROM fn_notifications_template
        WHERE TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListForScopeSqlServer = new(
        "notifications.platform.template.list_for_scope.sql_server",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, ChannelKey,
               ContentCategoryKey, DraftSubject, DraftBodyJson, DraftParameterSchemaJson,
               DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_template
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListForScopeMySql = new(
        "notifications.platform.template.list_for_scope.mysql",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, ChannelKey,
               ContentCategoryKey, DraftSubject, DraftBodyJson, DraftParameterSchemaJson,
               DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_template
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertTemplateHost = new(
        "notifications.platform.template.insert_host",
        """
        INSERT INTO fn_notifications_template
            (Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, ChannelKey, ContentCategoryKey,
             DraftSubject, DraftBodyJson, DraftParameterSchemaJson, DraftRevision,
             LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT @Id, NULL, 'host', 'host', @TemplateKey, @ChannelKey, @ContentCategoryKey,
               @DraftSubject, @DraftBodyJson, @DraftParameterSchemaJson, 1,
               NULL, @CreatedById, @CreatedAtUtc, NULL, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_template
            WHERE TenantScopeKey = 'host'
              AND TemplateKey = @TemplateKey)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertTemplateTenant = new(
        "notifications.platform.template.insert_tenant",
        """
        INSERT INTO fn_notifications_template
            (Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, ChannelKey, ContentCategoryKey,
             DraftSubject, DraftBodyJson, DraftParameterSchemaJson, DraftRevision,
             LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT @Id, @TenantId, 'tenant', @TenantScopeKey, @TemplateKey, @ChannelKey, @ContentCategoryKey,
               @DraftSubject, @DraftBodyJson, @DraftParameterSchemaJson, 1,
               NULL, @CreatedById, @CreatedAtUtc, NULL, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_template
            WHERE TenantScopeKey = @TenantScopeKey
              AND TenantId = @TenantId
              AND TemplateKey = @TemplateKey)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateDraft = new(
        "notifications.platform.template.update_draft",
        """
        UPDATE fn_notifications_template
        SET DraftSubject = @DraftSubject,
            DraftBodyJson = @DraftBodyJson,
            DraftParameterSchemaJson = @DraftParameterSchemaJson,
            DraftRevision = DraftRevision + 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = @NextVersion
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement PublishTemplate = new(
        "notifications.platform.template.publish",
        """
        UPDATE fn_notifications_template
        SET LatestPublishedVersionId = @LatestPublishedVersionId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = @NextVersion
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindTemplateVersionById = new(
        "notifications.platform.template_version.find_by_id",
        """
        SELECT Id, TemplateId, VersionNumber, SchemaVersion, Subject, BodyJson,
               ParameterSchemaJson, ContentClassificationKey, ContentHash, PublishedById, PublishedAtUtc
        FROM fn_notifications_template_version
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindTemplateVersionByHash = new(
        "notifications.platform.template_version.find_by_hash",
        """
        SELECT Id, TemplateId, VersionNumber, SchemaVersion, Subject, BodyJson,
               ParameterSchemaJson, ContentClassificationKey, ContentHash, PublishedById, PublishedAtUtc
        FROM fn_notifications_template_version
        WHERE TemplateId = @TemplateId
          AND ContentHash = @ContentHash
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement MaxTemplateVersionNumber = new(
        "notifications.platform.template_version.max_number",
        """
        SELECT COALESCE(MAX(VersionNumber), 0)
        FROM fn_notifications_template_version
        WHERE TemplateId = @TemplateId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertTemplateVersion = new(
        "notifications.platform.template_version.insert",
        """
        INSERT INTO fn_notifications_template_version
            (Id, TemplateId, VersionNumber, SchemaVersion, Subject, BodyJson,
             ParameterSchemaJson, ContentClassificationKey, ContentHash, PublishedById, PublishedAtUtc)
        SELECT @Id, @TemplateId, @VersionNumber, @SchemaVersion, @Subject, @BodyJson,
               @ParameterSchemaJson, @ContentClassificationKey, @ContentHash, @PublishedById, @PublishedAtUtc
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_template_version
            WHERE TemplateId = @TemplateId
              AND ContentHash = @ContentHash)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindIntentById = new(
        "notifications.platform.intent.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
               TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
               RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision
        FROM fn_notifications_intent
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindIntentByIdUnscoped = new(
        "notifications.platform.intent.find_by_id_unscoped",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
               TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
               RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision
        FROM fn_notifications_intent
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindIntentByIdempotency = new(
        "notifications.platform.intent.find_by_idempotency",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
               TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
               RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision
        FROM fn_notifications_intent
        WHERE TenantScopeKey = @TenantScopeKey
          AND ProducerKey = @ProducerKey
          AND IdempotencyKey = @IdempotencyKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertIntentHost = new(
        "notifications.platform.intent.insert_host",
        """
        INSERT INTO fn_notifications_intent
            (Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
             TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
             RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision)
        SELECT @Id, NULL, 'host', 'host', @ProducerKey, @SceneKey, @IdempotencyKey,
               @TemplateVersionId, @BindingVersionId, @PolicyCategoryKey, @DispatchModeKey,
               @RouteSnapshotJson, @ParameterSnapshotJson, @StatusKey, @CreatedById, @CreatedAtUtc, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_intent
            WHERE TenantScopeKey = 'host'
              AND ProducerKey = @ProducerKey
              AND IdempotencyKey = @IdempotencyKey)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertIntentTenant = new(
        "notifications.platform.intent.insert_tenant",
        """
        INSERT INTO fn_notifications_intent
            (Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, IdempotencyKey,
             TemplateVersionId, BindingVersionId, PolicyCategoryKey, DispatchModeKey,
             RouteSnapshotJson, ParameterSnapshotJson, StatusKey, CreatedById, CreatedAtUtc, Revision)
        SELECT @Id, @TenantId, 'tenant', @TenantScopeKey, @ProducerKey, @SceneKey, @IdempotencyKey,
               @TemplateVersionId, @BindingVersionId, @PolicyCategoryKey, @DispatchModeKey,
               @RouteSnapshotJson, @ParameterSnapshotJson, @StatusKey, @CreatedById, @CreatedAtUtc, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_intent
            WHERE TenantScopeKey = @TenantScopeKey
              AND TenantId = @TenantId
              AND ProducerKey = @ProducerKey
              AND IdempotencyKey = @IdempotencyKey)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement FindRecipientById = new(
        "notifications.platform.recipient.find_by_id",
        """
        SELECT Id, IntentId, RecipientTypeKey, RecipientKey, UserId, AddressDigest,
               ResolutionStatusKey, CreatedAtUtc
        FROM fn_notifications_recipient
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListRecipientsByIntent = new(
        "notifications.platform.recipient.list_by_intent",
        """
        SELECT Id, IntentId, RecipientTypeKey, RecipientKey, UserId, AddressDigest,
               ResolutionStatusKey, CreatedAtUtc
        FROM fn_notifications_recipient
        WHERE IntentId = @IntentId
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertRecipient = new(
        "notifications.platform.recipient.insert",
        """
        INSERT INTO fn_notifications_recipient
            (Id, IntentId, RecipientTypeKey, RecipientKey, UserId, AddressDigest,
             ResolutionStatusKey, CreatedAtUtc)
        VALUES
            (@Id, @IntentId, @RecipientTypeKey, @RecipientKey, @UserId, NULL,
             @ResolutionStatusKey, @CreatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindDeliveryById = new(
        "notifications.platform.delivery.find_by_id",
        """
        SELECT Id, IntentId, RecipientId, ChannelKey, ProviderProfileVersionId, BindingVersionId,
               StatusKey, Revision, LeaseOwnerKey, LeaseExpiresAtUtc, LeaseGeneration,
               NextAttemptAtUtc, CreatedAtUtc, UpdatedAtUtc
        FROM fn_notifications_delivery
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindDeliveryAttemptById = new(
        "notifications.platform.delivery_attempt.find_by_id",
        """
        SELECT Id, DeliveryId, AttemptNumber, LeaseOwnerKey, LeaseGeneration, LeaseExpiresAtUtc,
               ResultCategoryKey, StatusKey, ProviderMessageId, ErrorCode, ReceiptDigest,
               StartedAtUtc, FinishedAtUtc
        FROM fn_notifications_delivery_attempt
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindReceiptById = new(
        "notifications.platform.receipt.find_by_id",
        """
        SELECT Id, ProviderTypeKey, ProviderMessageId, ReceiptIdempotencyKey, DeliveryId,
               ExternalStatusKey, MappedStatusKey, PayloadDigest, ReceivedAtUtc,
               ProcessedAtUtc, ProcessStatusKey
        FROM fn_notifications_receipt
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountDeliveriesByIntent = new(
        "notifications.platform.delivery.count_by_intent",
        """
        SELECT COUNT(*)
        FROM fn_notifications_delivery
        WHERE IntentId = @IntentId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountAttemptsByDelivery = new(
        "notifications.platform.delivery_attempt.count_by_delivery",
        """
        SELECT COUNT(*)
        FROM fn_notifications_delivery_attempt
        WHERE DeliveryId = @DeliveryId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountAttemptsByIntent = new(
        "notifications.platform.delivery_attempt.count_by_intent",
        """
        SELECT COUNT(*)
        FROM fn_notifications_delivery_attempt a
        INNER JOIN fn_notifications_delivery d ON d.Id = a.DeliveryId
        WHERE d.IntentId = @IntentId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListAttemptsByDelivery = new(
        "notifications.platform.delivery_attempt.list_by_delivery",
        """
        SELECT Id, DeliveryId, AttemptNumber, LeaseOwnerKey, LeaseGeneration, LeaseExpiresAtUtc,
               ResultCategoryKey, StatusKey, ProviderMessageId, ErrorCode, ReceiptDigest,
               StartedAtUtc, FinishedAtUtc
        FROM fn_notifications_delivery_attempt
        WHERE DeliveryId = @DeliveryId
        ORDER BY AttemptNumber, StartedAtUtc, Id
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 插入 accepted Delivery。Intent 幂等回放在插入前即返回，首次受理不会撞 Recipient+Channel+Profile 唯一索引。
    /// 使用 VALUES 而非无 FROM 的 INSERT SELECT，避免 MySQL 把无表 SELECT 物化为 0 行。
    /// </summary>
    public static readonly SqlStatement InsertDelivery = new(
        "notifications.platform.delivery.insert",
        """
        INSERT INTO fn_notifications_delivery
            (Id, IntentId, RecipientId, ChannelKey, ProviderProfileVersionId, BindingVersionId,
             StatusKey, Revision, LeaseOwnerKey, LeaseExpiresAtUtc, LeaseGeneration,
             NextAttemptAtUtc, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @IntentId, @RecipientId, @ChannelKey, @ProviderProfileVersionId, @BindingVersionId,
             @StatusKey, 1, NULL, NULL, 1, @NextAttemptAtUtc, @CreatedAtUtc, NULL)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindDeliveryForScope = new(
        "notifications.platform.delivery.find_for_scope",
        """
        SELECT d.Id, d.IntentId, d.RecipientId, d.ChannelKey, d.ProviderProfileVersionId, d.BindingVersionId,
               d.StatusKey, d.Revision, d.LeaseOwnerKey, d.LeaseExpiresAtUtc, d.LeaseGeneration,
               d.NextAttemptAtUtc, d.CreatedAtUtc, d.UpdatedAtUtc
        FROM fn_notifications_delivery d
        INNER JOIN fn_notifications_intent i ON i.Id = d.IntentId
        WHERE d.Id = @Id
          AND i.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListDeliveriesForScopeSqlServer = new(
        "notifications.platform.delivery.list_for_scope.sql_server",
        """
        SELECT d.Id, d.IntentId, d.RecipientId, d.ChannelKey, d.ProviderProfileVersionId, d.BindingVersionId,
               d.StatusKey, d.Revision, d.LeaseOwnerKey, d.LeaseExpiresAtUtc, d.LeaseGeneration,
               d.NextAttemptAtUtc, d.CreatedAtUtc, d.UpdatedAtUtc
        FROM fn_notifications_delivery d
        INNER JOIN fn_notifications_intent i ON i.Id = d.IntentId
        WHERE i.TenantScopeKey = @TenantScopeKey
        ORDER BY d.CreatedAtUtc DESC, d.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListDeliveriesForScopeMySql = new(
        "notifications.platform.delivery.list_for_scope.mysql",
        """
        SELECT d.Id, d.IntentId, d.RecipientId, d.ChannelKey, d.ProviderProfileVersionId, d.BindingVersionId,
               d.StatusKey, d.Revision, d.LeaseOwnerKey, d.LeaseExpiresAtUtc, d.LeaseGeneration,
               d.NextAttemptAtUtc, d.CreatedAtUtc, d.UpdatedAtUtc
        FROM fn_notifications_delivery d
        INNER JOIN fn_notifications_intent i ON i.Id = d.IntentId
        WHERE i.TenantScopeKey = @TenantScopeKey
        ORDER BY d.CreatedAtUtc DESC, d.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountDeliveriesForScope = new(
        "notifications.platform.delivery.count_for_scope",
        """
        SELECT COUNT(*)
        FROM fn_notifications_delivery d
        INNER JOIN fn_notifications_intent i ON i.Id = d.IntentId
        WHERE i.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ClaimDeliveriesSqlServer = new(
        "notifications.platform.delivery.claim.sql_server",
        """
        ;WITH Pending AS
        (
            SELECT TOP (@BatchSize) *
            FROM fn_notifications_delivery WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE StatusKey IN ('accepted', 'unknown')
              AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
              AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
            ORDER BY NextAttemptAtUtc, CreatedAtUtc, Id
        )
        UPDATE Pending
        SET LeaseOwnerKey = @LeaseOwnerKey,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            LeaseGeneration = LeaseGeneration + 1,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        OUTPUT inserted.Id, inserted.IntentId, inserted.RecipientId, inserted.ChannelKey,
               inserted.ProviderProfileVersionId, inserted.BindingVersionId, inserted.StatusKey,
               inserted.Revision, inserted.LeaseOwnerKey, inserted.LeaseExpiresAtUtc,
               inserted.LeaseGeneration, inserted.NextAttemptAtUtc, inserted.CreatedAtUtc,
               inserted.UpdatedAtUtc;
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SelectClaimableDeliveryIdsMySql = new(
        "notifications.platform.delivery.select_claimable_ids.mysql",
        """
        SELECT Id
        FROM fn_notifications_delivery
        WHERE StatusKey IN ('accepted', 'unknown')
          AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        ORDER BY NextAttemptAtUtc, CreatedAtUtc, Id
        LIMIT @BatchSize
        FOR UPDATE SKIP LOCKED
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ClaimDeliveriesByIdsMySql = new(
        "notifications.platform.delivery.claim_by_ids.mysql",
        """
        UPDATE fn_notifications_delivery
        SET LeaseOwnerKey = @LeaseOwnerKey,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            LeaseGeneration = LeaseGeneration + 1,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id IN @Ids
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SelectDeliveriesByLease = new(
        "notifications.platform.delivery.select_by_lease",
        """
        SELECT Id, IntentId, RecipientId, ChannelKey, ProviderProfileVersionId, BindingVersionId,
               StatusKey, Revision, LeaseOwnerKey, LeaseExpiresAtUtc, LeaseGeneration,
               NextAttemptAtUtc, CreatedAtUtc, UpdatedAtUtc
        FROM fn_notifications_delivery
        WHERE LeaseOwnerKey = @LeaseOwnerKey
        ORDER BY CreatedAtUtc, Id
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 强制过期租约，供崩溃窗口测试与运维重领；生产 Worker 正常路径依赖 LeaseExpiresAtUtc 自然到期。
    /// </summary>
    public static readonly SqlStatement ExpireDeliveryLease = new(
        "notifications.platform.delivery.expire_lease",
        """
        UPDATE fn_notifications_delivery
        SET LeaseExpiresAtUtc = @ExpiredAt
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CompleteDelivery = new(
        "notifications.platform.delivery.complete",
        """
        UPDATE fn_notifications_delivery
        SET StatusKey = @StatusKey,
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            NextAttemptAtUtc = @NextAttemptAtUtc,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND LeaseGeneration = @LeaseGeneration
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement RetryDelivery = new(
        "notifications.platform.delivery.retry",
        """
        UPDATE fn_notifications_delivery
        SET StatusKey = 'accepted',
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            NextAttemptAtUtc = @NextAttemptAtUtc,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND Revision = @Revision
          AND StatusKey IN ('failed', 'dead_lettered', 'unknown')
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertAttempt = new(
        "notifications.platform.delivery_attempt.insert",
        """
        INSERT INTO fn_notifications_delivery_attempt
            (Id, DeliveryId, AttemptNumber, LeaseOwnerKey, LeaseGeneration, LeaseExpiresAtUtc,
             ResultCategoryKey, StatusKey, ProviderMessageId, ErrorCode, ReceiptDigest,
             StartedAtUtc, FinishedAtUtc)
        VALUES
            (@Id, @DeliveryId, @AttemptNumber, @LeaseOwnerKey, @LeaseGeneration, @LeaseExpiresAtUtc,
             @ResultCategoryKey, @StatusKey, @ProviderMessageId, @ErrorCode, @ReceiptDigest,
             @StartedAtUtc, @FinishedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindReceiptByIdempotency = new(
        "notifications.platform.receipt.find_by_idempotency",
        """
        SELECT Id, ProviderTypeKey, ProviderMessageId, ReceiptIdempotencyKey, DeliveryId,
               ExternalStatusKey, MappedStatusKey, PayloadDigest, ReceivedAtUtc,
               ProcessedAtUtc, ProcessStatusKey
        FROM fn_notifications_receipt
        WHERE ProviderTypeKey = @ProviderTypeKey
          AND ReceiptIdempotencyKey = @ReceiptIdempotencyKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindDeliveryByProviderMessageId = new(
        "notifications.platform.delivery.find_by_provider_message_id",
        """
        SELECT d.Id, d.IntentId, d.RecipientId, d.ChannelKey, d.ProviderProfileVersionId, d.BindingVersionId,
               d.StatusKey, d.Revision, d.LeaseOwnerKey, d.LeaseExpiresAtUtc, d.LeaseGeneration,
               d.NextAttemptAtUtc, d.CreatedAtUtc, d.UpdatedAtUtc
        FROM fn_notifications_delivery d
        INNER JOIN fn_notifications_delivery_attempt a ON a.DeliveryId = d.Id
        INNER JOIN fn_notifications_provider_profile_version pv ON pv.Id = d.ProviderProfileVersionId
        WHERE pv.ProviderTypeKey = @ProviderTypeKey
          AND a.ProviderMessageId = @ProviderMessageId
        ORDER BY a.FinishedAtUtc DESC, a.Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertReceipt = new(
        "notifications.platform.receipt.insert",
        """
        INSERT INTO fn_notifications_receipt
            (Id, ProviderTypeKey, ProviderMessageId, ReceiptIdempotencyKey, DeliveryId,
             ExternalStatusKey, MappedStatusKey, PayloadDigest, ReceivedAtUtc,
             ProcessedAtUtc, ProcessStatusKey)
        SELECT @Id, @ProviderTypeKey, @ProviderMessageId, @ReceiptIdempotencyKey, @DeliveryId,
               @ExternalStatusKey, @MappedStatusKey, @PayloadDigest, @ReceivedAtUtc,
               @ProcessedAtUtc, @ProcessStatusKey
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_receipt
            WHERE ProviderTypeKey = @ProviderTypeKey
              AND ReceiptIdempotencyKey = @ReceiptIdempotencyKey)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ApplyDeliveryStatus = new(
        "notifications.platform.delivery.apply_status",
        """
        UPDATE fn_notifications_delivery
        SET StatusKey = @StatusKey,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountDeliveryBacklog = new(
        "notifications.platform.delivery.count_backlog",
        """
        SELECT COUNT(*)
        FROM fn_notifications_delivery
        WHERE StatusKey IN ('accepted', 'unknown')
          AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement OldestDeliveryBacklog = new(
        "notifications.platform.delivery.oldest_backlog",
        """
        SELECT MIN(CreatedAtUtc)
        FROM fn_notifications_delivery
        WHERE StatusKey IN ('accepted', 'unknown')
          AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindProfileById = new(
        "notifications.platform.profile.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProfileKey, ProviderTypeKey,
               NonSecretConfigJson, SecretReference, IsEnabled, DraftRevision,
               LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_provider_profile
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindProfileByKey = new(
        "notifications.platform.profile.find_by_key",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProfileKey, ProviderTypeKey,
               NonSecretConfigJson, SecretReference, IsEnabled, DraftRevision,
               LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_provider_profile
        WHERE TenantScopeKey = @TenantScopeKey
          AND ProfileKey = @ProfileKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountProfilesForScope = new(
        "notifications.platform.profile.count_for_scope",
        """
        SELECT COUNT(*)
        FROM fn_notifications_provider_profile
        WHERE TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListProfilesSqlServer = new(
        "notifications.platform.profile.list_for_scope.sql_server",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProfileKey, ProviderTypeKey,
               NonSecretConfigJson, SecretReference, IsEnabled, DraftRevision,
               LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_provider_profile
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListProfilesMySql = new(
        "notifications.platform.profile.list_for_scope.mysql",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProfileKey, ProviderTypeKey,
               NonSecretConfigJson, SecretReference, IsEnabled, DraftRevision,
               LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_provider_profile
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertProfileHost = new(
        "notifications.platform.profile.insert_host",
        """
        INSERT INTO fn_notifications_provider_profile
            (Id, TenantId, ScopeKey, TenantScopeKey, ProfileKey, ProviderTypeKey,
             NonSecretConfigJson, SecretReference, IsEnabled, DraftRevision,
             LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT @Id, NULL, 'host', 'host', @ProfileKey, @ProviderTypeKey,
               @NonSecretConfigJson, @SecretReference, 0, 1,
               NULL, @CreatedById, @CreatedAtUtc, NULL, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_provider_profile
            WHERE TenantScopeKey = 'host'
              AND ProfileKey = @ProfileKey)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertProfileTenant = new(
        "notifications.platform.profile.insert_tenant",
        """
        INSERT INTO fn_notifications_provider_profile
            (Id, TenantId, ScopeKey, TenantScopeKey, ProfileKey, ProviderTypeKey,
             NonSecretConfigJson, SecretReference, IsEnabled, DraftRevision,
             LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT @Id, @TenantId, 'tenant', @TenantScopeKey, @ProfileKey, @ProviderTypeKey,
               @NonSecretConfigJson, @SecretReference, 0, 1,
               NULL, @CreatedById, @CreatedAtUtc, NULL, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_provider_profile
            WHERE TenantScopeKey = @TenantScopeKey
              AND TenantId = @TenantId
              AND ProfileKey = @ProfileKey)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateProfileDraft = new(
        "notifications.platform.profile.update_draft",
        """
        UPDATE fn_notifications_provider_profile
        SET NonSecretConfigJson = @NonSecretConfigJson,
            SecretReference = @SecretReference,
            DraftRevision = DraftRevision + 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = @NextVersion
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SetProfileEnabled = new(
        "notifications.platform.profile.set_enabled",
        """
        UPDATE fn_notifications_provider_profile
        SET IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = @NextVersion
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement PublishProfile = new(
        "notifications.platform.profile.publish",
        """
        UPDATE fn_notifications_provider_profile
        SET LatestPublishedVersionId = @LatestPublishedVersionId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = @NextVersion
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindProfileVersionById = new(
        "notifications.platform.profile_version.find_by_id",
        """
        SELECT Id, ProfileId, VersionNumber, ProviderTypeKey, AdapterVersion,
               NonSecretConfigJson, SecretReference, ContentHash, PublishedById, PublishedAtUtc
        FROM fn_notifications_provider_profile_version
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindProfileVersionByHash = new(
        "notifications.platform.profile_version.find_by_hash",
        """
        SELECT Id, ProfileId, VersionNumber, ProviderTypeKey, AdapterVersion,
               NonSecretConfigJson, SecretReference, ContentHash, PublishedById, PublishedAtUtc
        FROM fn_notifications_provider_profile_version
        WHERE ProfileId = @ProfileId
          AND ContentHash = @ContentHash
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountProfileVersions = new(
        "notifications.platform.profile_version.count",
        """
        SELECT COUNT(*)
        FROM fn_notifications_provider_profile_version
        WHERE ProfileId = @ProfileId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertProfileVersion = new(
        "notifications.platform.profile_version.insert",
        """
        INSERT INTO fn_notifications_provider_profile_version
            (Id, ProfileId, VersionNumber, ProviderTypeKey, AdapterVersion,
             NonSecretConfigJson, SecretReference, ContentHash, PublishedById, PublishedAtUtc)
        SELECT @Id, @ProfileId, @VersionNumber, @ProviderTypeKey, @AdapterVersion,
               @NonSecretConfigJson, @SecretReference, @ContentHash, @PublishedById, @PublishedAtUtc
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_provider_profile_version
            WHERE ProfileId = @ProfileId
              AND ContentHash = @ContentHash)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindBindingById = new(
        "notifications.platform.binding.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, BindingKey, DraftDispatchModeKey,
               DraftJson, DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc,
               UpdatedAtUtc, Version
        FROM fn_notifications_binding
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindBindingByKey = new(
        "notifications.platform.binding.find_by_key",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, BindingKey, DraftDispatchModeKey,
               DraftJson, DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc,
               UpdatedAtUtc, Version
        FROM fn_notifications_binding
        WHERE TenantScopeKey = @TenantScopeKey
          AND BindingKey = @BindingKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountBindingsForScope = new(
        "notifications.platform.binding.count_for_scope",
        """
        SELECT COUNT(*)
        FROM fn_notifications_binding
        WHERE TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListBindingsSqlServer = new(
        "notifications.platform.binding.list_for_scope.sql_server",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, BindingKey, DraftDispatchModeKey,
               DraftJson, DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc,
               UpdatedAtUtc, Version
        FROM fn_notifications_binding
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListBindingsMySql = new(
        "notifications.platform.binding.list_for_scope.mysql",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, BindingKey, DraftDispatchModeKey,
               DraftJson, DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc,
               UpdatedAtUtc, Version
        FROM fn_notifications_binding
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY CreatedAtUtc DESC, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertBindingHost = new(
        "notifications.platform.binding.insert_host",
        """
        INSERT INTO fn_notifications_binding
            (Id, TenantId, ScopeKey, TenantScopeKey, BindingKey, DraftDispatchModeKey,
             DraftJson, DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc,
             UpdatedAtUtc, Version)
        SELECT @Id, NULL, 'host', 'host', @BindingKey, @DraftDispatchModeKey,
               @DraftJson, 1, NULL, @CreatedById, @CreatedAtUtc, NULL, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_binding
            WHERE TenantScopeKey = 'host'
              AND BindingKey = @BindingKey)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertBindingTenant = new(
        "notifications.platform.binding.insert_tenant",
        """
        INSERT INTO fn_notifications_binding
            (Id, TenantId, ScopeKey, TenantScopeKey, BindingKey, DraftDispatchModeKey,
             DraftJson, DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc,
             UpdatedAtUtc, Version)
        SELECT @Id, @TenantId, 'tenant', @TenantScopeKey, @BindingKey, @DraftDispatchModeKey,
               @DraftJson, 1, NULL, @CreatedById, @CreatedAtUtc, NULL, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_binding
            WHERE TenantScopeKey = @TenantScopeKey
              AND TenantId = @TenantId
              AND BindingKey = @BindingKey)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateBindingDraft = new(
        "notifications.platform.binding.update_draft",
        """
        UPDATE fn_notifications_binding
        SET DraftDispatchModeKey = @DraftDispatchModeKey,
            DraftJson = @DraftJson,
            DraftRevision = DraftRevision + 1,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = @NextVersion
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement PublishBinding = new(
        "notifications.platform.binding.publish",
        """
        UPDATE fn_notifications_binding
        SET LatestPublishedVersionId = @LatestPublishedVersionId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = @NextVersion
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindBindingVersionById = new(
        "notifications.platform.binding_version.find_by_id",
        """
        SELECT Id, BindingId, VersionNumber, ProducerKey, SceneKey, ChannelKey,
               DispatchModeKey, BindingTargetsJson, ContentHash, PublishedById, PublishedAtUtc
        FROM fn_notifications_binding_version
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindBindingVersionByHash = new(
        "notifications.platform.binding_version.find_by_hash",
        """
        SELECT Id, BindingId, VersionNumber, ProducerKey, SceneKey, ChannelKey,
               DispatchModeKey, BindingTargetsJson, ContentHash, PublishedById, PublishedAtUtc
        FROM fn_notifications_binding_version
        WHERE BindingId = @BindingId
          AND ContentHash = @ContentHash
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountBindingVersions = new(
        "notifications.platform.binding_version.count",
        """
        SELECT COUNT(*)
        FROM fn_notifications_binding_version
        WHERE BindingId = @BindingId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertBindingVersion = new(
        "notifications.platform.binding_version.insert",
        """
        INSERT INTO fn_notifications_binding_version
            (Id, BindingId, VersionNumber, ProducerKey, SceneKey, ChannelKey,
             DispatchModeKey, BindingTargetsJson, ContentHash, PublishedById, PublishedAtUtc)
        SELECT @Id, @BindingId, @VersionNumber, @ProducerKey, @SceneKey, @ChannelKey,
               @DispatchModeKey, @BindingTargetsJson, @ContentHash, @PublishedById, @PublishedAtUtc
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_binding_version
            WHERE BindingId = @BindingId
              AND ContentHash = @ContentHash)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListPublishedBindingsByScene = new(
        "notifications.platform.binding_version.list_published_by_scene",
        """
        SELECT v.Id, v.BindingId, v.VersionNumber, v.ProducerKey, v.SceneKey, v.ChannelKey,
               v.DispatchModeKey, v.BindingTargetsJson, v.ContentHash, v.PublishedById, v.PublishedAtUtc
        FROM fn_notifications_binding_version v
        INNER JOIN fn_notifications_binding b ON b.LatestPublishedVersionId = v.Id
        WHERE b.TenantScopeKey = @TenantScopeKey
          AND v.ProducerKey = @ProducerKey
          AND v.SceneKey = @SceneKey
          AND v.ChannelKey = @ChannelKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertDomainAuditHost = new(
        "notifications.platform.domain_audit.insert_host",
        """
        INSERT INTO fn_notifications_domain_audit
            (Id, TenantId, ScopeKey, IntentId, OperationKey, ActorUserId,
             ResourceTypeKey, ResourceId, OutcomeKey, DetailJson, CreatedAtUtc)
        VALUES
            (@Id, NULL, 'host', NULL, @OperationKey, @ActorUserId,
             @ResourceTypeKey, @ResourceId, @OutcomeKey, @DetailJson, @CreatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertDomainAuditTenant = new(
        "notifications.platform.domain_audit.insert_tenant",
        """
        INSERT INTO fn_notifications_domain_audit
            (Id, TenantId, ScopeKey, IntentId, OperationKey, ActorUserId,
             ResourceTypeKey, ResourceId, OutcomeKey, DetailJson, CreatedAtUtc)
        VALUES
            (@Id, @TenantId, 'tenant', NULL, @OperationKey, @ActorUserId,
             @ResourceTypeKey, @ResourceId, @OutcomeKey, @DetailJson, @CreatedAtUtc)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}
