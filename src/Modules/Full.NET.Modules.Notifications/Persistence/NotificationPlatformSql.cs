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
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, LocaleTag, DefaultLocaleTag, ChannelKey,
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
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, LocaleTag, DefaultLocaleTag, ChannelKey,
               ContentCategoryKey, DraftSubject, DraftBodyJson, DraftParameterSchemaJson,
               DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_template
        WHERE TenantScopeKey = @TenantScopeKey
          AND TemplateKey = @TemplateKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindTemplateByKeyAndLocale = new(
        "notifications.platform.template.find_by_key_and_locale",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, LocaleTag, DefaultLocaleTag, ChannelKey,
               ContentCategoryKey, DraftSubject, DraftBodyJson, DraftParameterSchemaJson,
               DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_template
        WHERE TenantScopeKey = @TenantScopeKey
          AND TemplateKey = @TemplateKey
          AND LocaleTag = @LocaleTag
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListTemplateLocalesByKey = new(
        "notifications.platform.template.list_locales_by_key",
        """
        SELECT Id, LocaleTag, DefaultLocaleTag, LatestPublishedVersionId
        FROM fn_notifications_template
        WHERE TenantScopeKey = @TenantScopeKey
          AND TemplateKey = @TemplateKey
        ORDER BY LocaleTag, Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListTemplateLocalesByKeys = new(
        "notifications.platform.template.list_locales_by_keys",
        """
        SELECT TemplateKey, Id, LocaleTag, DefaultLocaleTag, LatestPublishedVersionId
        FROM fn_notifications_template
        WHERE TenantScopeKey = @TenantScopeKey AND TemplateKey IN @TemplateKeys
        ORDER BY TemplateKey, LocaleTag, Id
        """, SqlDataScope.Global);

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
        SELECT t.Id, t.TenantId, t.ScopeKey, t.TenantScopeKey, t.TemplateKey, t.LocaleTag, t.DefaultLocaleTag,
               t.ChannelKey, t.ContentCategoryKey, t.DraftSubject, t.DraftBodyJson, t.DraftParameterSchemaJson,
               t.DraftRevision, t.LatestPublishedVersionId,
               v.VersionNumber AS LatestPublishedVersionNumber,
               v.ContentHash AS LatestContentHash,
               v.ContentClassificationKey AS LatestContentClassificationKey,
               t.CreatedById, t.CreatedAtUtc, t.UpdatedAtUtc, t.Version
        FROM fn_notifications_template t
        LEFT JOIN fn_notifications_template_version v ON v.Id = t.LatestPublishedVersionId
        WHERE t.TenantScopeKey = @TenantScopeKey
        ORDER BY t.CreatedAtUtc DESC, t.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListForScopeMySql = new(
        "notifications.platform.template.list_for_scope.mysql",
        """
        SELECT t.Id, t.TenantId, t.ScopeKey, t.TenantScopeKey, t.TemplateKey, t.LocaleTag, t.DefaultLocaleTag,
               t.ChannelKey, t.ContentCategoryKey, t.DraftSubject, t.DraftBodyJson, t.DraftParameterSchemaJson,
               t.DraftRevision, t.LatestPublishedVersionId,
               v.VersionNumber AS LatestPublishedVersionNumber,
               v.ContentHash AS LatestContentHash,
               v.ContentClassificationKey AS LatestContentClassificationKey,
               t.CreatedById, t.CreatedAtUtc, t.UpdatedAtUtc, t.Version
        FROM fn_notifications_template t
        LEFT JOIN fn_notifications_template_version v ON v.Id = t.LatestPublishedVersionId
        WHERE t.TenantScopeKey = @TenantScopeKey
        ORDER BY t.CreatedAtUtc DESC, t.Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertTemplateHost = new(
        "notifications.platform.template.insert_host",
        """
        INSERT INTO fn_notifications_template
            (Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, LocaleTag, DefaultLocaleTag, ChannelKey,
             ContentCategoryKey, DraftSubject, DraftBodyJson, DraftParameterSchemaJson, DraftRevision,
             LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT @Id, NULL, 'host', 'host', @TemplateKey, @LocaleTag, @DefaultLocaleTag, @ChannelKey,
               @ContentCategoryKey, @DraftSubject, @DraftBodyJson, @DraftParameterSchemaJson, 1,
               NULL, @CreatedById, @CreatedAtUtc, NULL, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_template
            WHERE TenantScopeKey = 'host'
              AND TemplateKey = @TemplateKey
              AND LocaleTag = @LocaleTag)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertTemplateTenant = new(
        "notifications.platform.template.insert_tenant",
        """
        INSERT INTO fn_notifications_template
            (Id, TenantId, ScopeKey, TenantScopeKey, TemplateKey, LocaleTag, DefaultLocaleTag, ChannelKey,
             ContentCategoryKey, DraftSubject, DraftBodyJson, DraftParameterSchemaJson, DraftRevision,
             LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT @Id, @TenantId, 'tenant', @TenantScopeKey, @TemplateKey, @LocaleTag, @DefaultLocaleTag,
               @ChannelKey, @ContentCategoryKey, @DraftSubject, @DraftBodyJson, @DraftParameterSchemaJson, 1,
               NULL, @CreatedById, @CreatedAtUtc, NULL, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_template
            WHERE TenantScopeKey = @TenantScopeKey
              AND TenantId = @TenantId
              AND TemplateKey = @TemplateKey
              AND LocaleTag = @LocaleTag)
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
        SELECT Id, TemplateId, LocaleTag, VersionNumber, SchemaVersion, Subject, BodyJson,
               ParameterSchemaJson, ContentClassificationKey, ContentHash, PublishedById, PublishedAtUtc
        FROM fn_notifications_template_version
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindTemplateVersionByHash = new(
        "notifications.platform.template_version.find_by_hash",
        """
        SELECT Id, TemplateId, LocaleTag, VersionNumber, SchemaVersion, Subject, BodyJson,
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
            (Id, TemplateId, LocaleTag, VersionNumber, SchemaVersion, Subject, BodyJson,
             ParameterSchemaJson, ContentClassificationKey, ContentHash, PublishedById, PublishedAtUtc)
        SELECT @Id, @TemplateId, @LocaleTag, @VersionNumber, @SchemaVersion, @Subject, @BodyJson,
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
               NextAttemptAtUtc, CreatedAtUtc, UpdatedAtUtc, ContentSnapshotJson,
               PreferenceVersion, PolicyDecisionKey, SuppressionReasonCode
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

    /// <summary>历史尝试可能因中断预算留下序号空档；后续序号必须接续最大值而不是记录行数。</summary>
    public static readonly SqlStatement FindLastAttemptNumber = new(
        "notifications.platform.delivery_attempt.find_last_number",
        """
        SELECT COALESCE(MAX(AttemptNumber), 0)
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

    public static readonly SqlStatement ListReceiptsByDelivery = new(
        "notifications.platform.receipt.list_by_delivery",
        """
        SELECT Id, ProviderTypeKey, ProviderMessageId, ReceiptIdempotencyKey, DeliveryId,
               ExternalStatusKey, MappedStatusKey, PayloadDigest, ReceivedAtUtc,
               ProcessedAtUtc, ProcessStatusKey
        FROM fn_notifications_receipt
        WHERE DeliveryId = @DeliveryId
        ORDER BY ReceivedAtUtc, Id
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
             NextAttemptAtUtc, CreatedAtUtc, UpdatedAtUtc, ContentSnapshotJson,
             PreferenceVersion, PolicyDecisionKey, SuppressionReasonCode)
        VALUES
            (@Id, @IntentId, @RecipientId, @ChannelKey, @ProviderProfileVersionId, @BindingVersionId,
             @StatusKey, 1, NULL, NULL, 1, @NextAttemptAtUtc, @CreatedAtUtc, NULL,
             @ContentSnapshotJson, @PreferenceVersion, @PolicyDecisionKey, @SuppressionReasonCode)
        """,
        SqlDataScope.Global);

    /// <summary>Failover 原子推进备用投递；同一收件人、渠道和 Profile 已存在时不重复创建。</summary>
    public static readonly SqlStatement InsertDeliveryIfAbsent = new(
        "notifications.platform.delivery.insert_if_absent",
        """
        INSERT INTO fn_notifications_delivery
            (Id, IntentId, RecipientId, ChannelKey, ProviderProfileVersionId, BindingVersionId,
             StatusKey, Revision, LeaseOwnerKey, LeaseExpiresAtUtc, LeaseGeneration,
             NextAttemptAtUtc, CreatedAtUtc, UpdatedAtUtc, ContentSnapshotJson,
             PreferenceVersion, PolicyDecisionKey, SuppressionReasonCode)
        SELECT @Id, @IntentId, @RecipientId, @ChannelKey, @ProviderProfileVersionId, @BindingVersionId,
               @StatusKey, 1, NULL, NULL, 1, @NextAttemptAtUtc, @CreatedAtUtc, NULL,
               @ContentSnapshotJson, @PreferenceVersion, @PolicyDecisionKey, @SuppressionReasonCode
        FROM fn_notifications_recipient
        WHERE Id = @RecipientId
          AND NOT EXISTS (
              SELECT 1
              FROM fn_notifications_delivery
              WHERE RecipientId = @RecipientId
                AND ChannelKey = @ChannelKey
                AND ProviderProfileVersionId = @ProviderProfileVersionId)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindDeliveryForScope = new(
        "notifications.platform.delivery.find_for_scope",
        """
        SELECT d.Id, d.IntentId, d.RecipientId, d.ChannelKey, d.ProviderProfileVersionId, d.BindingVersionId,
               d.StatusKey, d.Revision, d.LeaseOwnerKey, d.LeaseExpiresAtUtc, d.LeaseGeneration,
               d.NextAttemptAtUtc, d.CreatedAtUtc, d.UpdatedAtUtc, d.ContentSnapshotJson,
               d.PreferenceVersion, d.PolicyDecisionKey, d.SuppressionReasonCode
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
               d.NextAttemptAtUtc, d.CreatedAtUtc, d.UpdatedAtUtc, d.ContentSnapshotJson,
               d.PreferenceVersion, d.PolicyDecisionKey, d.SuppressionReasonCode
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
               d.NextAttemptAtUtc, d.CreatedAtUtc, d.UpdatedAtUtc, d.ContentSnapshotJson,
               d.PreferenceVersion, d.PolicyDecisionKey, d.SuppressionReasonCode
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

    /// <summary>
    /// 领取可自动执行的投递；暂挂的 unknown（无计划重试时间）只能由可信回执或人工处置推进，不参与自动领取。
    /// </summary>
    public static readonly SqlStatement ClaimDeliveriesSqlServer = new(
        "notifications.platform.delivery.claim.sql_server",
        """
        ;WITH Pending AS
        (
            SELECT TOP (@BatchSize) *
            FROM fn_notifications_delivery WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE (
                    (StatusKey = 'accepted' AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now))
                    OR (StatusKey = 'unknown' AND NextAttemptAtUtc IS NOT NULL AND NextAttemptAtUtc <= @Now)
                  )
              AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
            ORDER BY NextAttemptAtUtc, CreatedAtUtc, Id
        )
        UPDATE Pending
        SET LeaseOwnerKey = @LeaseOwnerKey,
            LeaseExpiresAtUtc = @LeaseExpiresAtUtc,
            LeaseGeneration = LeaseGeneration + 1,
            AttemptBudgetUsed = AttemptBudgetUsed + 1,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        OUTPUT inserted.Id, inserted.IntentId, inserted.RecipientId, inserted.ChannelKey,
               inserted.ProviderProfileVersionId, inserted.BindingVersionId, inserted.StatusKey,
               inserted.Revision, inserted.LeaseOwnerKey, inserted.LeaseExpiresAtUtc,
               inserted.LeaseGeneration, inserted.NextAttemptAtUtc, inserted.CreatedAtUtc,
               inserted.UpdatedAtUtc, inserted.ContentSnapshotJson, inserted.PreferenceVersion,
               inserted.PolicyDecisionKey, inserted.SuppressionReasonCode;
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SelectClaimableDeliveryIdsMySql = new(
        "notifications.platform.delivery.select_claimable_ids.mysql",
        """
        SELECT Id
        FROM fn_notifications_delivery
        WHERE (
                (StatusKey = 'accepted' AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now))
                OR (StatusKey = 'unknown' AND NextAttemptAtUtc IS NOT NULL AND NextAttemptAtUtc <= @Now)
              )
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
            AttemptBudgetUsed = AttemptBudgetUsed + 1,
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
               NextAttemptAtUtc, CreatedAtUtc, UpdatedAtUtc, ContentSnapshotJson,
               PreferenceVersion, PolicyDecisionKey, SuppressionReasonCode
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

    /// <summary>仅续展仍有效且未被其他 Worker 接管的租约；两种数据库使用相同条件更新。</summary>
    public static readonly SqlStatement RenewDeliveryLease = new(
        "notifications.platform.delivery.renew_lease",
        """
        UPDATE fn_notifications_delivery
        SET LeaseExpiresAtUtc = @LeaseExpiresAtUtc
        WHERE Id = @Id
          AND StatusKey IN ('accepted', 'unknown')
          AND LeaseOwnerKey = @LeaseOwnerKey
          AND LeaseGeneration = @LeaseGeneration
          AND Revision = @Revision
          AND LeaseExpiresAtUtc > @Now
        """,
        SqlDataScope.Global);

    /// <summary>只有持有有效租约的执行者可以写入本轮结果。</summary>
    public static readonly SqlStatement CompleteDelivery = new(
        "notifications.platform.delivery.complete",
        """
        UPDATE fn_notifications_delivery
        SET StatusKey = @StatusKey,
            SuppressionReasonCode = COALESCE(@SuppressionReasonCode, SuppressionReasonCode),
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            NextAttemptAtUtc = @NextAttemptAtUtc,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND LeaseGeneration = @LeaseGeneration
          AND Revision = @Revision
          AND LeaseOwnerKey = @LeaseOwnerKey
          AND LeaseExpiresAtUtc > @Now
        """,
        SqlDataScope.Global);

    /// <summary>读取本次领取计入的独立重试预算；租约世代只承担栅栏职责。</summary>
    public static readonly SqlStatement FindAttemptBudget = new(
        "notifications.platform.delivery.find_attempt_budget",
        """
        SELECT AttemptBudgetUsed FROM fn_notifications_delivery WHERE Id = @Id
        """,
        SqlDataScope.Global);

    /// <summary>配额事务先锁定有效投递世代，阻止旧 Worker 在接管后继续占额或进入厂商。</summary>
    public static readonly SqlStatement LockDeliveryForQuotaSqlServer = new(
        "notifications.platform.delivery.lock_for_quota.sql_server",
        """
        SELECT Id FROM fn_notifications_delivery WITH (UPDLOCK, ROWLOCK)
        WHERE Id = @Id AND LeaseGeneration = @LeaseGeneration
          AND LeaseExpiresAtUtc > @Now AND StatusKey IN ('accepted', 'unknown')
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement LockDeliveryForQuotaMySql = new(
        "notifications.platform.delivery.lock_for_quota.mysql",
        """
        SELECT Id FROM fn_notifications_delivery
        WHERE Id = @Id AND LeaseGeneration = @LeaseGeneration
          AND LeaseExpiresAtUtc > @Now AND StatusKey IN ('accepted', 'unknown')
        FOR UPDATE
        """,
        SqlDataScope.Global);

    /// <summary>仅在本轮完成成功的同一事务内退还治理延迟预算，不改变租约栅栏。</summary>
    public static readonly SqlStatement RefundGovernanceAttemptBudget = new(
        "notifications.platform.delivery.refund_governance_budget",
        """
        UPDATE fn_notifications_delivery
        SET AttemptBudgetUsed = AttemptBudgetUsed - 1
        WHERE Id = @Id
          AND LeaseGeneration = @LeaseGeneration
          AND Revision = @Revision
          AND LeaseOwnerKey IS NULL
          AND AttemptBudgetUsed > 0
        """,
        SqlDataScope.Global);

    /// <summary>受审计人工重试重置自动预算并推进栅栏，旧配额预留世代不会被重复使用。</summary>
    public static readonly SqlStatement RetryDelivery = new(
        "notifications.platform.delivery.retry",
        """
        UPDATE fn_notifications_delivery
        SET StatusKey = 'accepted',
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            LeaseGeneration = LeaseGeneration + 1,
            AttemptBudgetUsed = 0,
            NextAttemptAtUtc = @NextAttemptAtUtc,
            Revision = Revision + 1,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND Revision = @Revision
          AND StatusKey IN ('failed', 'dead_lettered')
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertAttempt = new(
        "notifications.platform.delivery_attempt.insert",
        """
        INSERT INTO fn_notifications_delivery_attempt
            (Id, DeliveryId, AttemptNumber, LeaseOwnerKey, LeaseGeneration, LeaseExpiresAtUtc,
             ResultCategoryKey, StatusKey, ProviderMessageId, ErrorCode, ReceiptDigest,
             StartedAtUtc, FinishedAtUtc, CorrelationJson)
        VALUES
            (@Id, @DeliveryId, @AttemptNumber, @LeaseOwnerKey, @LeaseGeneration, @LeaseExpiresAtUtc,
             @ResultCategoryKey, @StatusKey, @ProviderMessageId, @ErrorCode, @ReceiptDigest,
             @StartedAtUtc, @FinishedAtUtc, @CorrelationJson)
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
               d.NextAttemptAtUtc, d.CreatedAtUtc, d.UpdatedAtUtc, d.ContentSnapshotJson,
               d.PreferenceVersion, d.PolicyDecisionKey, d.SuppressionReasonCode
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

    /// <summary>
    /// 按提供程序消息号列出尚未关联的提前回执，供 Attempt 入库后做有界补偿重关联。
    /// </summary>
    public static readonly SqlStatement ListPendingReceiptsByProviderMessageId = new(
        "notifications.platform.receipt.list_pending_by_provider_message_id",
        """
        SELECT Id, ProviderTypeKey, ProviderMessageId, ReceiptIdempotencyKey, DeliveryId,
               ExternalStatusKey, MappedStatusKey, PayloadDigest, ReceivedAtUtc,
               ProcessedAtUtc, ProcessStatusKey
        FROM fn_notifications_receipt
        WHERE ProviderTypeKey = @ProviderTypeKey
          AND ProviderMessageId = @ProviderMessageId
          AND ProcessStatusKey IN ('pending_association', 'pending_apply')
        ORDER BY ReceivedAtUtc, Id
        """,
        SqlDataScope.Global);

    /// <summary>提前回执在 Attempt 入库后重新关联；只有未完成回执可被补偿推进。</summary>
    public static readonly SqlStatement UpdateReceiptProcessing = new(
        "notifications.platform.receipt.update_processing",
        """
        UPDATE fn_notifications_receipt
        SET DeliveryId = @DeliveryId,
            ProcessStatusKey = @ProcessStatusKey,
            ProcessedAtUtc = @ProcessedAtUtc
        WHERE Id = @Id
          AND ProcessStatusKey <> 'completed'
        """,
        SqlDataScope.Global);

    /// <summary>Worker 仍欠的自动执行积压；暂挂 unknown 不计入，避免长期告警噪音。</summary>
    public static readonly SqlStatement CountDeliveryBacklog = new(
        "notifications.platform.delivery.count_backlog",
        """
        SELECT COUNT(*)
        FROM fn_notifications_delivery
        WHERE StatusKey = 'accepted'
          AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        """,
        SqlDataScope.Global);

    /// <summary>等待可信回执对账或人工处置的暂挂 unknown 数量，供运维告警与看板区分展示。</summary>
    public static readonly SqlStatement CountDeliveryAwaitingReconciliation = new(
        "notifications.platform.delivery.count_awaiting_reconciliation",
        """
        SELECT COUNT(*)
        FROM fn_notifications_delivery
        WHERE StatusKey = 'unknown'
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement OldestDeliveryBacklog = new(
        "notifications.platform.delivery.oldest_backlog",
        """
        SELECT MIN(CreatedAtUtc)
        FROM fn_notifications_delivery
        WHERE StatusKey = 'accepted'
          AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @Now)
          AND (LeaseExpiresAtUtc IS NULL OR LeaseExpiresAtUtc <= @Now)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindPreferenceByUserChannel = new(
        "notifications.platform.preference.find_by_user_channel",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, UserId, ChannelKey,
               ChannelOptedOut, MarketingConsentGranted, QuietHoursJson, UpdatedAtUtc, Version
        FROM fn_notifications_preference
        WHERE TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND ChannelKey = @ChannelKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListPreferencesByUser = new(
        "notifications.platform.preference.list_by_user",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, UserId, ChannelKey,
               ChannelOptedOut, MarketingConsentGranted, QuietHoursJson, UpdatedAtUtc, Version
        FROM fn_notifications_preference
        WHERE TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
        ORDER BY ChannelKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdatePreference = new(
        "notifications.platform.preference.update",
        """
        UPDATE fn_notifications_preference
        SET ChannelOptedOut = @ChannelOptedOut,
            MarketingConsentGranted = @MarketingConsentGranted,
            QuietHoursJson = @QuietHoursJson,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND UserId = @UserId
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertPreference = new(
        "notifications.platform.preference.insert",
        """
        INSERT INTO fn_notifications_preference
            (Id, TenantId, ScopeKey, TenantScopeKey, UserId, ChannelKey,
             ChannelOptedOut, MarketingConsentGranted, QuietHoursJson, UpdatedAtUtc, Version)
        SELECT @Id, @TenantId, @ScopeKey, @TenantScopeKey, @UserId, @ChannelKey,
               @ChannelOptedOut, @MarketingConsentGranted, @QuietHoursJson, @UpdatedAtUtc, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_preference
            WHERE TenantScopeKey = @TenantScopeKey
              AND UserId = @UserId
              AND ChannelKey = @ChannelKey)
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

    public static readonly SqlStatement FindProducerScene = new(
        "notifications.platform.producer_scene.find_by_key",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, PolicyCategoryKey,
               AllowedChannelKeysJson, EmergencyOverrideAllowed, IsEnabled, CreatedById,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_producer_scene
        WHERE TenantScopeKey = @TenantScopeKey
          AND ProducerKey = @ProducerKey
          AND SceneKey = @SceneKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindProducerSceneById = new(
        "notifications.platform.producer_scene.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, PolicyCategoryKey,
               AllowedChannelKeysJson, EmergencyOverrideAllowed, IsEnabled, CreatedById,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_producer_scene
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountProducerScenesForScope = new(
        "notifications.platform.producer_scene.count_for_scope",
        """
        SELECT COUNT(*)
        FROM fn_notifications_producer_scene
        WHERE TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListProducerScenesSqlServer = new(
        "notifications.platform.producer_scene.list_for_scope.sql_server",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, PolicyCategoryKey,
               AllowedChannelKeysJson, EmergencyOverrideAllowed, IsEnabled, CreatedById,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_producer_scene
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY ProducerKey, SceneKey, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListProducerScenesMySql = new(
        "notifications.platform.producer_scene.list_for_scope.mysql",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, PolicyCategoryKey,
               AllowedChannelKeysJson, EmergencyOverrideAllowed, IsEnabled, CreatedById,
               CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_producer_scene
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY ProducerKey, SceneKey, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertProducerSceneHost = new(
        "notifications.platform.producer_scene.insert_host",
        """
        INSERT INTO fn_notifications_producer_scene
            (Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, PolicyCategoryKey,
             AllowedChannelKeysJson, EmergencyOverrideAllowed, IsEnabled, CreatedById,
             CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT @Id, NULL, 'host', 'host', @ProducerKey, @SceneKey, @PolicyCategoryKey,
               @AllowedChannelKeysJson, @EmergencyOverrideAllowed, 1, @CreatedById,
               @CreatedAtUtc, @CreatedAtUtc, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_producer_scene
            WHERE TenantScopeKey = 'host'
              AND ProducerKey = @ProducerKey
              AND SceneKey = @SceneKey)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertProducerSceneTenant = new(
        "notifications.platform.producer_scene.insert_tenant",
        """
        INSERT INTO fn_notifications_producer_scene
            (Id, TenantId, ScopeKey, TenantScopeKey, ProducerKey, SceneKey, PolicyCategoryKey,
             AllowedChannelKeysJson, EmergencyOverrideAllowed, IsEnabled, CreatedById,
             CreatedAtUtc, UpdatedAtUtc, Version)
        SELECT @Id, @TenantId, 'tenant', @TenantScopeKey, @ProducerKey, @SceneKey, @PolicyCategoryKey,
               @AllowedChannelKeysJson, @EmergencyOverrideAllowed, 1, @CreatedById,
               @CreatedAtUtc, @CreatedAtUtc, 1
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_producer_scene
            WHERE TenantScopeKey = @TenantScopeKey
              AND TenantId = @TenantId
              AND ProducerKey = @ProducerKey
              AND SceneKey = @SceneKey)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateProducerScene = new(
        "notifications.platform.producer_scene.update",
        """
        UPDATE fn_notifications_producer_scene
        SET PolicyCategoryKey = @PolicyCategoryKey,
            AllowedChannelKeysJson = @AllowedChannelKeysJson,
            EmergencyOverrideAllowed = @EmergencyOverrideAllowed,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SetProducerSceneEnabled = new(
        "notifications.platform.producer_scene.set_enabled",
        """
        UPDATE fn_notifications_producer_scene
        SET IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertDomainAuditHost = new(
        "notifications.platform.domain_audit.insert_host",
        """
        INSERT INTO fn_notifications_domain_audit
            (Id, TenantId, ScopeKey, IntentId, OperationKey, ActorUserId,
             ResourceTypeKey, ResourceId, OutcomeKey, DetailJson, CreatedAtUtc, CorrelationJson)
        VALUES
            (@Id, NULL, 'host', NULL, @OperationKey, @ActorUserId,
             @ResourceTypeKey, @ResourceId, @OutcomeKey, @DetailJson, @CreatedAtUtc, @CorrelationJson)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertDomainAuditTenant = new(
        "notifications.platform.domain_audit.insert_tenant",
        """
        INSERT INTO fn_notifications_domain_audit
            (Id, TenantId, ScopeKey, IntentId, OperationKey, ActorUserId,
             ResourceTypeKey, ResourceId, OutcomeKey, DetailJson, CreatedAtUtc, CorrelationJson)
        VALUES
            (@Id, @TenantId, 'tenant', NULL, @OperationKey, @ActorUserId,
             @ResourceTypeKey, @ResourceId, @OutcomeKey, @DetailJson, @CreatedAtUtc, @CorrelationJson)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    // ===== 发送配额治理：政策声明、窗口计数器与预留台账 =====

    public static readonly SqlStatement FindQuotaPolicyById = new(
        "notifications.platform.quota_policy.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, DimensionKey, DimensionValue,
               WindowSeconds, MaxCount, IsEnabled, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_quota_policy
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CountQuotaPoliciesForScope = new(
        "notifications.platform.quota_policy.count_for_scope",
        """
        SELECT COUNT(*)
        FROM fn_notifications_quota_policy
        WHERE TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListQuotaPoliciesSqlServer = new(
        "notifications.platform.quota_policy.list_for_scope.sql_server",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, DimensionKey, DimensionValue,
               WindowSeconds, MaxCount, IsEnabled, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_quota_policy
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY DimensionKey, DimensionValue, Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListQuotaPoliciesMySql = new(
        "notifications.platform.quota_policy.list_for_scope.mysql",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, DimensionKey, DimensionValue,
               WindowSeconds, MaxCount, IsEnabled, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_notifications_quota_policy
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY DimensionKey, DimensionValue, Id
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertQuotaPolicyHost = new(
        "notifications.platform.quota_policy.insert_host",
        """
        INSERT INTO fn_notifications_quota_policy
            (Id, TenantId, ScopeKey, TenantScopeKey, DimensionKey, DimensionValue,
             WindowSeconds, MaxCount, IsEnabled, Version, CreatedById, CreatedAtUtc, UpdatedAtUtc)
        SELECT @Id, NULL, 'host', 'host', @DimensionKey, @DimensionValue,
               @WindowSeconds, @MaxCount, 1, 1, @CreatedById, @Now, @Now
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_quota_policy
            WHERE TenantScopeKey = 'host'
              AND DimensionKey = @DimensionKey
              AND DimensionValue = @DimensionValue)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertQuotaPolicyTenant = new(
        "notifications.platform.quota_policy.insert_tenant",
        """
        INSERT INTO fn_notifications_quota_policy
            (Id, TenantId, ScopeKey, TenantScopeKey, DimensionKey, DimensionValue,
             WindowSeconds, MaxCount, IsEnabled, Version, CreatedById, CreatedAtUtc, UpdatedAtUtc)
        SELECT @Id, @TenantId, 'tenant', @TenantScopeKey, @DimensionKey, @DimensionValue,
               @WindowSeconds, @MaxCount, 1, 1, @CreatedById, @Now, @Now
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_quota_policy
            WHERE TenantScopeKey = @TenantScopeKey
              AND TenantId = @TenantId
              AND DimensionKey = @DimensionKey
              AND DimensionValue = @DimensionValue)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    public static readonly SqlStatement UpdateQuotaPolicy = new(
        "notifications.platform.quota_policy.update",
        """
        UPDATE fn_notifications_quota_policy
        SET WindowSeconds = @WindowSeconds,
            MaxCount = @MaxCount,
            UpdatedAtUtc = @Now,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SetQuotaPolicyEnabled = new(
        "notifications.platform.quota_policy.set_enabled",
        """
        UPDATE fn_notifications_quota_policy
        SET IsEnabled = @IsEnabled,
            UpdatedAtUtc = @Now,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND Version = @Version
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 按四个维度解析当前生效的有界政策；租户精确声明优先于租户通配，再退到 host 精确与 host 通配。
    /// 某个维度没有命中行即表示该维度缺失配置，发送链路必须失败关闭而不是按无界处理。
    /// </summary>
    public static readonly SqlStatement ResolveQuotaPolicies = new(
        "notifications.platform.quota_policy.resolve",
        """
        WITH Dimensions AS
        (
            SELECT 'provider' AS DimensionKey, @ProviderValue AS DimensionValue
            UNION ALL SELECT 'tenant', @TenantValue
            UNION ALL SELECT 'producer', @ProducerValue
            UNION ALL SELECT 'scene', @SceneValue
        ),
        Candidates AS
        (
            SELECT d.DimensionKey,
                   p.Id AS PolicyId,
                   p.WindowSeconds,
                   p.MaxCount,
                   CASE
                       WHEN p.TenantScopeKey = @TenantScopeKey AND p.DimensionValue = d.DimensionValue THEN 1
                       WHEN p.TenantScopeKey = @TenantScopeKey AND p.DimensionValue = '*' THEN 2
                       WHEN p.TenantScopeKey = 'host' AND p.DimensionValue = d.DimensionValue THEN 3
                       ELSE 4
                   END AS MatchRank
            FROM Dimensions d
            INNER JOIN fn_notifications_quota_policy p
                ON p.DimensionKey = d.DimensionKey
               AND p.IsEnabled = 1
               AND (p.TenantScopeKey = @TenantScopeKey OR p.TenantScopeKey = 'host')
               AND (p.DimensionValue = d.DimensionValue OR p.DimensionValue = '*')
        ),
        Ranked AS
        (
            SELECT DimensionKey, PolicyId, WindowSeconds, MaxCount,
                   ROW_NUMBER() OVER (PARTITION BY DimensionKey ORDER BY MatchRank, PolicyId) AS MatchOrder
            FROM Candidates
        )
        SELECT DimensionKey, PolicyId, WindowSeconds, MaxCount
        FROM Ranked
        WHERE MatchOrder = 1
        """,
        SqlDataScope.Global);

    /// <summary>锁定政策行以串行化同一政策下的窗口预留，阻止并发超卖。</summary>
    public static readonly SqlStatement LockQuotaPolicySqlServer = new(
        "notifications.platform.quota_policy.lock.sql_server",
        """
        SELECT Id
        FROM fn_notifications_quota_policy WITH (UPDLOCK, ROWLOCK)
        WHERE Id = @Id
        """,
        SqlDataScope.Global);

    /// <summary>MySQL 使用排他行锁取得与 SQL Server 等价的串行化窗口预留。</summary>
    public static readonly SqlStatement LockQuotaPolicyMySql = new(
        "notifications.platform.quota_policy.lock.mysql",
        """
        SELECT Id
        FROM fn_notifications_quota_policy
        WHERE Id = @Id
        FOR UPDATE
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindQuotaCounter = new(
        "notifications.platform.quota_counter.find",
        """
        SELECT Id, ReservedCount, ConsumedCount, ReleasedCount
        FROM fn_notifications_quota_counter
        WHERE PolicyId = @PolicyId
          AND WindowStartUtc = @WindowStartUtc
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertQuotaCounter = new(
        "notifications.platform.quota_counter.insert",
        """
        INSERT INTO fn_notifications_quota_counter
            (Id, PolicyId, WindowStartUtc, WindowSeconds, ReservedCount, ConsumedCount, ReleasedCount, UpdatedAtUtc)
        SELECT @Id, @PolicyId, @WindowStartUtc, @WindowSeconds, 0, 0, 0, @Now
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_quota_counter
            WHERE PolicyId = @PolicyId
              AND WindowStartUtc = @WindowStartUtc)
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 原子预留一次发送额度；条件 <c>ReservedCount - ReleasedCount &lt; MaxCount</c> 保证窗口内
    /// “在飞 + 已核销 + 暂挂”不超过上限，返回 0 表示窗口已满。
    /// </summary>
    public static readonly SqlStatement IncrementQuotaCounterReserved = new(
        "notifications.platform.quota_counter.increment_reserved",
        """
        UPDATE fn_notifications_quota_counter
        SET ReservedCount = ReservedCount + 1,
            UpdatedAtUtc = @Now
        WHERE PolicyId = @PolicyId
          AND WindowStartUtc = @WindowStartUtc
          AND ReservedCount - ReleasedCount < @MaxCount
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ConsumeQuotaCounter = new(
        "notifications.platform.quota_counter.consume",
        """
        UPDATE fn_notifications_quota_counter
        SET ConsumedCount = ConsumedCount + 1,
            UpdatedAtUtc = @Now
        WHERE PolicyId = @PolicyId
          AND WindowStartUtc = @WindowStartUtc
        """,
        SqlDataScope.Global);

    /// <summary>确定未发送时释放额度；释放后该窗口重新接受新的预留。</summary>
    public static readonly SqlStatement ReleaseQuotaCounter = new(
        "notifications.platform.quota_counter.release",
        """
        UPDATE fn_notifications_quota_counter
        SET ReleasedCount = ReleasedCount + 1,
            UpdatedAtUtc = @Now
        WHERE PolicyId = @PolicyId
          AND WindowStartUtc = @WindowStartUtc
          AND ReservedCount - ReleasedCount > 0
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertQuotaReservation = new(
        "notifications.platform.quota_reservation.insert",
        """
        INSERT INTO fn_notifications_quota_reservation
            (Id, DeliveryId, PolicyId, DimensionKey, TenantScopeKey, WindowStartUtc, LeaseGeneration,
             StateKey, CreatedAtUtc, UpdatedAtUtc)
        SELECT @Id, @DeliveryId, @PolicyId, @DimensionKey, @TenantScopeKey, @WindowStartUtc,
               @LeaseGeneration, @StateKey, @Now, @Now
        WHERE NOT EXISTS (
            SELECT 1
            FROM fn_notifications_quota_reservation
            WHERE DeliveryId = @DeliveryId
              AND PolicyId = @PolicyId
              AND LeaseGeneration = @LeaseGeneration)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ResolveQuotaReservations = new(
        "notifications.platform.quota_reservation.resolve",
        """
        UPDATE fn_notifications_quota_reservation
        SET StateKey = @StateKey,
            UpdatedAtUtc = @Now
        WHERE DeliveryId = @DeliveryId
          AND LeaseGeneration = @LeaseGeneration
          AND StateKey = 'reserved'
        """,
        SqlDataScope.Global);

    /// <summary>列出本轮仍为 reserved 的预留，用于把结果映射到正确的窗口计数器。</summary>
    public static readonly SqlStatement ListReservedQuotaReservations = new(
        "notifications.platform.quota_reservation.list_reserved",
        """
        SELECT Id, PolicyId, DimensionKey, WindowStartUtc
        FROM fn_notifications_quota_reservation
        WHERE DeliveryId = @DeliveryId
          AND LeaseGeneration = @LeaseGeneration
          AND StateKey = 'reserved'
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 列出被更新世代抛弃的预留；不能证明未发送，必须保持占额并转待对账。
    /// </summary>
    public static readonly SqlStatement ListAbandonedQuotaReservationsSqlServer = new(
        "notifications.platform.quota_reservation.list_abandoned.sql_server",
        """
        SELECT TOP (@MaxRows) Id, PolicyId, DimensionKey, WindowStartUtc, LeaseGeneration
        FROM fn_notifications_quota_reservation
        WHERE DeliveryId = @DeliveryId
          AND LeaseGeneration < @LeaseGeneration
          AND StateKey IN ('reserved', 'pending_reconciliation')
        ORDER BY LeaseGeneration, Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListAbandonedQuotaReservationsMySql = new(
        "notifications.platform.quota_reservation.list_abandoned.mysql",
        """
        SELECT Id, PolicyId, DimensionKey, WindowStartUtc, LeaseGeneration
        FROM fn_notifications_quota_reservation
        WHERE DeliveryId = @DeliveryId
          AND LeaseGeneration < @LeaseGeneration
          AND StateKey IN ('reserved', 'pending_reconciliation')
        ORDER BY LeaseGeneration, Id
        LIMIT @MaxRows
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ResolveQuotaReservationById = new(
        "notifications.platform.quota_reservation.resolve_by_id",
        """
        UPDATE fn_notifications_quota_reservation
        SET StateKey = @StateKey,
            UpdatedAtUtc = @Now
        WHERE Id = @Id
          AND StateKey IN ('reserved', 'pending_reconciliation')
          AND StateKey <> @StateKey
        """,
        SqlDataScope.Global);

    /// <summary>可信消息回执只收敛产生该消息号的尝试世代，不影响人工重试后的其他预留。</summary>
    public static readonly SqlStatement ListQuotaReservationsForMessage = new(
        "notifications.platform.quota_reservation.list_for_message",
        """
        SELECT q.Id, q.PolicyId, q.DimensionKey, q.WindowStartUtc
        FROM fn_notifications_quota_reservation q
        WHERE q.DeliveryId = @DeliveryId
          AND q.StateKey IN ('reserved', 'pending_reconciliation')
          AND EXISTS (
              SELECT 1 FROM fn_notifications_delivery_attempt a
              WHERE a.DeliveryId = q.DeliveryId
                AND a.LeaseGeneration = q.LeaseGeneration
                AND a.ProviderMessageId = @ProviderMessageId)
        ORDER BY q.PolicyId, q.Id
        """,
        SqlDataScope.Global);

    /// <summary>按作用域聚合预留台账，供控制面定位受配额阻塞与等待对账的受影响范围。</summary>
    public static readonly SqlStatement CountQuotaOutstandingByScope = new(
        "notifications.platform.quota_reservation.count_outstanding",
        """
        SELECT DimensionKey, StateKey, COUNT(*) AS ReservationCount
        FROM fn_notifications_quota_reservation
        WHERE TenantScopeKey = @TenantScopeKey
        GROUP BY DimensionKey, StateKey
        ORDER BY DimensionKey, StateKey
        """,
        SqlDataScope.Global);

    /// <summary>SQL Server 聚合显式返回 bigint，匹配不可变记录的 long 构造参数。</summary>
    public static readonly SqlStatement CountQuotaOutstandingByScopeSqlServer = new(
        "notifications.platform.quota_reservation.count_outstanding.sql_server",
        """
        SELECT DimensionKey, StateKey, COUNT_BIG(*) AS ReservationCount
        FROM fn_notifications_quota_reservation
        WHERE TenantScopeKey = @TenantScopeKey
        GROUP BY DimensionKey, StateKey
        ORDER BY DimensionKey, StateKey
        """,
        SqlDataScope.Global);
}
