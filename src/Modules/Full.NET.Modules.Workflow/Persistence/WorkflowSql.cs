using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>Workflow 模块参数化 SQL 语句集；跨 Host/Tenant 查询必须显式携带 TenantScopeKey。</summary>
internal static class WorkflowSql
{
    public static readonly SqlStatement InsertDomainAudit = new(
        "workflow.domain_audit.insert",
        """
        INSERT INTO fn_workflow_domain_audit
            (Id, TenantId, ScopeKey, InstanceId, OperationKey, ActorUserId,
             ResourceTypeKey, ResourceId, OutcomeKey, DetailJson, CreatedAtUtc)
        VALUES
            (@Id, @TenantId, @ScopeKey, @InstanceId, @OperationKey, @ActorUserId,
             @ResourceTypeKey, @ResourceId, @OutcomeKey, @DetailJson, @CreatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListDefinitions = new(
        "workflow.definition.list",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, DefinitionKey, DraftId,
               LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_workflow_definition
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY DefinitionKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindDefinitionById = new(
        "workflow.definition.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, DefinitionKey, DraftId,
               LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_workflow_definition
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListDefinitionDrafts = new(
        "workflow.definition_draft.list",
        """
        SELECT draft.Id, draft.DefinitionId, draft.DraftJson, draft.DraftRevision,
               draft.ContentHash, draft.UpdatedById, draft.UpdatedAtUtc
        FROM fn_workflow_definition_draft AS draft
        INNER JOIN fn_workflow_definition AS definition ON definition.Id = draft.DefinitionId
        WHERE definition.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertDefinition = new(
        "workflow.definition.insert",
        """
        INSERT INTO fn_workflow_definition
            (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionKey, DraftId,
             LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @ScopeKey, @TenantScopeKey, @DefinitionKey, @DraftId,
             NULL, @CreatedById, @CreatedAtUtc, NULL, 1)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertDefinitionDraft = new(
        "workflow.definition_draft.insert",
        """
        INSERT INTO fn_workflow_definition_draft
            (Id, DefinitionId, DraftJson, DraftRevision, ContentHash, UpdatedById, UpdatedAtUtc)
        VALUES
            (@Id, @DefinitionId, @DraftJson, 1, @ContentHash, @UpdatedById, @UpdatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateDefinitionDraft = new(
        "workflow.definition_draft.update",
        """
        UPDATE fn_workflow_definition_draft
        SET DraftJson = @DraftJson,
            DraftRevision = DraftRevision + 1,
            ContentHash = @ContentHash,
            UpdatedById = @UpdatedById,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE DefinitionId = @DefinitionId
          AND DraftRevision = @ExpectedRevision
          AND EXISTS (
              SELECT 1 FROM fn_workflow_definition AS definition
              WHERE definition.Id = fn_workflow_definition_draft.DefinitionId
                AND definition.TenantScopeKey = @TenantScopeKey)
        """,
        SqlDataScope.Global);

    /// <summary>先占用草稿修订号，串行化同一草稿的并发发布。</summary>
    public static readonly SqlStatement ClaimDefinitionDraftForPublish = new(
        "workflow.definition_draft.claim_publish",
        """
        UPDATE fn_workflow_definition_draft
        SET DraftRevision = DraftRevision + 1,
            UpdatedById = @UpdatedById,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE DefinitionId = @DefinitionId
          AND DraftRevision = @ExpectedRevision
          AND EXISTS (
              SELECT 1 FROM fn_workflow_definition AS definition
              WHERE definition.Id = fn_workflow_definition_draft.DefinitionId
                AND definition.TenantScopeKey = @TenantScopeKey)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindNextDefinitionVersionNumber = new(
        "workflow.definition_version.find_next_number",
        """
        SELECT COALESCE(MAX(version.VersionNumber), 0) + 1
        FROM fn_workflow_definition_version AS version
        INNER JOIN fn_workflow_definition AS definition ON definition.Id = version.DefinitionId
        WHERE version.DefinitionId = @DefinitionId
          AND definition.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertDefinitionVersion = new(
        "workflow.definition_version.insert",
        """
        INSERT INTO fn_workflow_definition_version
            (Id, DefinitionId, FormVersionId, VersionNumber, SchemaVersion,
             CanonicalJson, ContentHash, PublishedById, PublishedAtUtc)
        VALUES
            (@Id, @DefinitionId, @FormVersionId, @VersionNumber, @SchemaVersion,
             @CanonicalJson, @ContentHash, @PublishedById, @PublishedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SetLatestDefinitionVersion = new(
        "workflow.definition.set_latest_version",
        """
        UPDATE fn_workflow_definition
        SET LatestPublishedVersionId = @VersionId,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListDefinitionVersions = new(
        "workflow.definition_version.list",
        """
        SELECT version.Id, version.DefinitionId, version.FormVersionId, version.VersionNumber,
               version.SchemaVersion, version.CanonicalJson, version.ContentHash,
               version.PublishedById, version.PublishedAtUtc
        FROM fn_workflow_definition_version AS version
        INNER JOIN fn_workflow_definition AS definition ON definition.Id = version.DefinitionId
        WHERE version.DefinitionId = @DefinitionId
          AND definition.TenantScopeKey = @TenantScopeKey
        ORDER BY version.VersionNumber DESC
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListFormDefinitions = new(
        "workflow.form_definition.list",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, FormKey, DraftSchemaJson,
               DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc,
               UpdatedAtUtc
        FROM fn_workflow_form_definition
        WHERE TenantScopeKey = @TenantScopeKey
        ORDER BY FormKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindFormDefinitionById = new(
        "workflow.form_definition.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, FormKey, DraftSchemaJson,
               DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc,
               UpdatedAtUtc
        FROM fn_workflow_form_definition
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertFormDefinition = new(
        "workflow.form_definition.insert",
        """
        INSERT INTO fn_workflow_form_definition
            (Id, TenantId, ScopeKey, TenantScopeKey, FormKey, DraftSchemaJson,
             DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @TenantId, @ScopeKey, @TenantScopeKey, @FormKey, @DraftSchemaJson,
             1, NULL, @CreatedById, @CreatedAtUtc, NULL)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateFormDraft = new(
        "workflow.form_definition.update_draft",
        """
        UPDATE fn_workflow_form_definition
        SET DraftSchemaJson = @DraftSchemaJson,
            DraftRevision = DraftRevision + 1,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND DraftRevision = @ExpectedRevision
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindNextFormVersionNumber = new(
        "workflow.form_version.find_next_number",
        """
        SELECT COALESCE(MAX(version.VersionNumber), 0) + 1
        FROM fn_workflow_form_version AS version
        INNER JOIN fn_workflow_form_definition AS definition
            ON definition.Id = version.FormDefinitionId
        WHERE version.FormDefinitionId = @FormDefinitionId
          AND definition.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertFormVersion = new(
        "workflow.form_version.insert",
        """
        INSERT INTO fn_workflow_form_version
            (Id, FormDefinitionId, VersionNumber, SchemaVersion, AdapterVersion,
             ComponentCatalogVersion, FormSchemaJson, WebRenderSchemaJson,
             ContentHash, PublishedById, PublishedAtUtc)
        VALUES
            (@Id, @FormDefinitionId, @VersionNumber, @SchemaVersion, @AdapterVersion,
             @ComponentCatalogVersion, @FormSchemaJson, @WebRenderSchemaJson,
             @ContentHash, @PublishedById, @PublishedAtUtc)
        """,
        SqlDataScope.Global);

    /// <summary>发布时推进草稿修订号，使同一 ExpectedRevision 只能成功一次。</summary>
    public static readonly SqlStatement PublishFormVersion = new(
        "workflow.form_definition.publish",
        """
        UPDATE fn_workflow_form_definition
        SET LatestPublishedVersionId = @VersionId,
            DraftRevision = DraftRevision + 1,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND DraftRevision = @ExpectedRevision
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindDefinitionByKey = new(
        "workflow.definition.find_by_key",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, DefinitionKey, DraftId,
               LatestPublishedVersionId, CreatedById, CreatedAtUtc, UpdatedAtUtc, Version
        FROM fn_workflow_definition
        WHERE TenantScopeKey = @TenantScopeKey
          AND DefinitionKey = @DefinitionKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindDefinitionDraftByDefinition = new(
        "workflow.definition_draft.find_by_definition",
        """
        SELECT draft.Id, draft.DefinitionId, draft.DraftJson, draft.DraftRevision,
               draft.ContentHash, draft.UpdatedById, draft.UpdatedAtUtc
        FROM fn_workflow_definition_draft AS draft
        INNER JOIN fn_workflow_definition AS definition
            ON definition.Id = draft.DefinitionId
        WHERE draft.DefinitionId = @DefinitionId
          AND definition.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindDefinitionVersionById = new(
        "workflow.definition_version.find_by_id",
        """
        SELECT version.Id, version.DefinitionId, version.FormVersionId, version.VersionNumber,
               version.SchemaVersion, version.CanonicalJson, version.ContentHash,
               version.PublishedById, version.PublishedAtUtc
        FROM fn_workflow_definition_version AS version
        INNER JOIN fn_workflow_definition AS definition
            ON definition.Id = version.DefinitionId
        WHERE version.Id = @Id
          AND definition.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindFormDefinitionByKey = new(
        "workflow.form_definition.find_by_key",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, FormKey, DraftSchemaJson,
               DraftRevision, LatestPublishedVersionId, CreatedById, CreatedAtUtc,
               UpdatedAtUtc
        FROM fn_workflow_form_definition
        WHERE TenantScopeKey = @TenantScopeKey
          AND FormKey = @FormKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindFormVersionById = new(
        "workflow.form_version.find_by_id",
        """
        SELECT version.Id, version.FormDefinitionId, version.VersionNumber,
               version.SchemaVersion, version.AdapterVersion,
               version.ComponentCatalogVersion, version.FormSchemaJson,
               version.WebRenderSchemaJson, version.ContentHash,
               version.PublishedById, version.PublishedAtUtc
        FROM fn_workflow_form_version AS version
        INNER JOIN fn_workflow_form_definition AS definition
            ON definition.Id = version.FormDefinitionId
        WHERE version.Id = @Id
          AND definition.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindInstanceById = new(
        "workflow.instance.find_by_id",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, DefinitionVersionId,
               FormVersionId, BusinessType, BusinessId, StatusKey, Revision,
               StartedById, StartedAtUtc, CompletedAtUtc, CancelledById,
               CancelledAtUtc, CancellationReason, LeaseOwnerKey, LeaseExpiresAtUtc
        FROM fn_workflow_instance
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 查找当前作用域内仍占用业务键的实例；暂停与运行同等占用，避免恢复时撞唯一约束。
    /// </summary>
    public static readonly SqlStatement FindActiveInstanceByBusinessKey = new(
        "workflow.instance.find_active_by_business_key",
        """
        SELECT Id, TenantId, ScopeKey, TenantScopeKey, DefinitionVersionId,
               FormVersionId, BusinessType, BusinessId, StatusKey, Revision,
               StartedById, StartedAtUtc, CompletedAtUtc, CancelledById,
               CancelledAtUtc, CancellationReason, LeaseOwnerKey, LeaseExpiresAtUtc
        FROM fn_workflow_instance
        WHERE TenantScopeKey = @TenantScopeKey
          AND BusinessType = @BusinessType
          AND BusinessId = @BusinessId
          AND StatusKey IN ('active', 'suspended')
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindRuntimeAsset = new(
        "workflow.runtime_asset.find",
        """
        SELECT version.Id AS DefinitionVersionId, version.FormVersionId,
               version.CanonicalJson, formVersion.FormSchemaJson
        FROM fn_workflow_definition_version AS version
        INNER JOIN fn_workflow_definition AS definition
            ON definition.Id = version.DefinitionId
        INNER JOIN fn_workflow_form_version AS formVersion
            ON formVersion.Id = version.FormVersionId
        INNER JOIN fn_workflow_form_definition AS formDefinition
            ON formDefinition.Id = formVersion.FormDefinitionId
        WHERE version.Id = @DefinitionVersionId
          AND definition.TenantScopeKey = @TenantScopeKey
          AND formDefinition.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertInstance = new(
        "workflow.instance.insert",
        """
        INSERT INTO fn_workflow_instance
            (Id, TenantId, ScopeKey, TenantScopeKey, DefinitionVersionId, FormVersionId,
             BusinessType, BusinessId, StatusKey, Revision, StartedById, StartedAtUtc,
             CompletedAtUtc, CancelledById, CancelledAtUtc, CancellationReason,
             LeaseOwnerKey, LeaseExpiresAtUtc)
        VALUES
            (@Id, @TenantId, @ScopeKey, @TenantScopeKey, @DefinitionVersionId, @FormVersionId,
             @BusinessType, @BusinessId, 'active', 1, @StartedById, @StartedAtUtc,
             NULL, NULL, NULL, NULL, NULL, NULL)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertStep = new(
        "workflow.step.insert",
        """
        INSERT INTO fn_workflow_step
            (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId,
             DueAtUtc, AttemptCount, Revision, ExecutionSequence, StartedAtUtc, CompletedAtUtc)
        VALUES
            (@Id, @InstanceId, @NodeKey, 'human.approval', 'active', @AssignedUserId,
             NULL, 0, 1, @ExecutionSequence, @StartedAtUtc, NULL)
        """,
        SqlDataScope.Global);

    /// <summary>写入已经同步完成的抄送步骤；抄送不产生待办，也不占用处理人。</summary>
    public static readonly SqlStatement InsertCompletedCcStep = new(
        "workflow.step.insert_completed_cc",
        """
        INSERT INTO fn_workflow_step
            (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId,
             DueAtUtc, AttemptCount, Revision, ExecutionSequence, StartedAtUtc, CompletedAtUtc)
        VALUES
            (@Id, @InstanceId, @NodeKey, 'notify.cc', 'completed', NULL,
             NULL, 0, 1, @ExecutionSequence, @StartedAtUtc, @CompletedAtUtc)
        """,
        SqlDataScope.Global);

    /// <summary>写入已经同步完成的排他网关步骤；网关只记录路由决策，不产生待办。</summary>
    public static readonly SqlStatement InsertCompletedGatewayStep = new(
        "workflow.step.insert_completed_gateway",
        """
        INSERT INTO fn_workflow_step
            (Id, InstanceId, NodeKey, NodeTypeKey, StatusKey, AssignedUserId,
             DueAtUtc, AttemptCount, Revision, ExecutionSequence, StartedAtUtc, CompletedAtUtc)
        VALUES
            (@Id, @InstanceId, @NodeKey, 'gateway.exclusive', 'completed', NULL,
             NULL, 0, 1, @ExecutionSequence, @StartedAtUtc, @CompletedAtUtc)
        """,
        SqlDataScope.Global);

    /// <summary>读取同一实例已经产生的抄送人，用于满足既有实例级唯一约束。</summary>
    public static readonly SqlStatement ListCcRecipientIdsByInstance = new(
        "workflow.cc.list_recipient_ids_by_instance",
        """
        SELECT cc.RecipientUserId
        FROM fn_workflow_cc AS cc
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = cc.InstanceId
        WHERE cc.InstanceId = @InstanceId
          AND instance.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    /// <summary>写入单个实例级抄送知识记录。</summary>
    public static readonly SqlStatement InsertCc = new(
        "workflow.cc.insert",
        """
        INSERT INTO fn_workflow_cc
            (Id, InstanceId, StepId, RecipientUserId, CreatedAtUtc, ReadAtUtc)
        VALUES
            (@Id, @InstanceId, @StepId, @RecipientUserId, @CreatedAtUtc, NULL)
        """,
        SqlDataScope.Global);

    /// <summary>按时间倒序读取 SQL Server 上当前用户的有界抄送记录。</summary>
    public static readonly SqlStatement ListMyCcSqlServer = new(
        "workflow.cc.list_mine.sqlserver",
        """
        SELECT TOP (@Take) cc.Id, cc.InstanceId, cc.StepId, step.NodeKey,
               cc.RecipientUserId, instance.BusinessType, instance.BusinessId,
               cc.CreatedAtUtc, cc.ReadAtUtc
        FROM fn_workflow_cc AS cc
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = cc.InstanceId
        LEFT JOIN fn_workflow_step AS step ON step.Id = cc.StepId
        WHERE instance.TenantScopeKey = @TenantScopeKey
          AND cc.RecipientUserId = @RecipientUserId
        ORDER BY cc.CreatedAtUtc DESC, cc.Id DESC
        """,
        SqlDataScope.Global);

    /// <summary>按时间倒序读取 MySQL 上当前用户的有界抄送记录。</summary>
    public static readonly SqlStatement ListMyCcMySql = new(
        "workflow.cc.list_mine.mysql",
        """
        SELECT cc.Id, cc.InstanceId, cc.StepId, step.NodeKey,
               cc.RecipientUserId, instance.BusinessType, instance.BusinessId,
               cc.CreatedAtUtc, cc.ReadAtUtc
        FROM fn_workflow_cc AS cc
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = cc.InstanceId
        LEFT JOIN fn_workflow_step AS step ON step.Id = cc.StepId
        WHERE instance.TenantScopeKey = @TenantScopeKey
          AND cc.RecipientUserId = @RecipientUserId
        ORDER BY cc.CreatedAtUtc DESC, cc.Id DESC
        LIMIT @Take
        """,
        SqlDataScope.Global);

    /// <summary>在可信租户和当前用户边界内读取单条抄送记录。</summary>
    public static readonly SqlStatement FindOwnCcById = new(
        "workflow.cc.find_own_by_id",
        """
        SELECT cc.Id, cc.InstanceId, cc.StepId, step.NodeKey,
               cc.RecipientUserId, instance.BusinessType, instance.BusinessId,
               cc.CreatedAtUtc, cc.ReadAtUtc
        FROM fn_workflow_cc AS cc
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = cc.InstanceId
        LEFT JOIN fn_workflow_step AS step ON step.Id = cc.StepId
        WHERE cc.Id = @Id
          AND cc.RecipientUserId = @RecipientUserId
          AND instance.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    /// <summary>仅为当前用户尚未阅读的租户内抄送记录写入首次已读时间。</summary>
    public static readonly SqlStatement MarkOwnCcRead = new(
        "workflow.cc.mark_own_read",
        """
        UPDATE fn_workflow_cc
        SET ReadAtUtc = @ReadAtUtc
        WHERE Id = @Id
          AND RecipientUserId = @RecipientUserId
          AND ReadAtUtc IS NULL
          AND EXISTS (
              SELECT 1
              FROM fn_workflow_instance AS instance
              WHERE instance.Id = fn_workflow_cc.InstanceId
                AND instance.TenantScopeKey = @TenantScopeKey)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertTodo = new(
        "workflow.todo.insert",
        """
        INSERT INTO fn_workflow_todo
            (Id, InstanceId, StepId, AssigneeUserId, StatusKey, Revision,
             ArrivedAtUtc, CompletedAtUtc, ResultActionKey, DueAtUtc,
             NextReminderAtUtc, EscalateAtUtc, MaxReminderCount, ReminderIntervalMinutes, ReminderCount,
             EscalationRecipientUserId, EscalatedAtUtc, NextTimeoutSignalAtUtc)
        VALUES
            (@Id, @InstanceId, @StepId, @AssigneeUserId, 'active', 1,
             @ArrivedAtUtc, NULL, NULL, @DueAtUtc, @NextReminderAtUtc,
             @EscalateAtUtc, @MaxReminderCount, @ReminderIntervalMinutes, 0, @EscalationRecipientUserId,
             NULL, @NextTimeoutSignalAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertFormSubmission = new(
        "workflow.form_submission.insert",
        """
        INSERT INTO fn_workflow_form_submission
            (Id, InstanceId, FormVersionId, SubmissionJson, DataClassificationSummary,
             Revision, UpdatedById, UpdatedAtUtc)
        VALUES
            (@Id, @InstanceId, @FormVersionId, @SubmissionJson, 'none',
             1, @UpdatedById, @UpdatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindFormSubmissionByInstance = new(
        "workflow.form_submission.find_by_instance",
        """
        SELECT submission.Id, submission.InstanceId, submission.FormVersionId,
               submission.SubmissionJson, submission.DataClassificationSummary,
               submission.Revision, submission.UpdatedById, submission.UpdatedAtUtc
        FROM fn_workflow_form_submission AS submission
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = submission.InstanceId
        WHERE submission.InstanceId = @InstanceId
          AND instance.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateFormSubmissionWithRevision = new(
        "workflow.form_submission.update_with_revision",
        """
        UPDATE fn_workflow_form_submission
        SET SubmissionJson = @SubmissionJson,
            Revision = Revision + 1,
            UpdatedById = @UpdatedById,
            UpdatedAtUtc = @UpdatedAtUtc
        WHERE InstanceId = @InstanceId
          AND FormVersionId = @FormVersionId
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertActionRecord = new(
        "workflow.action_record.insert",
        """
        INSERT INTO fn_workflow_action_record
            (Id, InstanceId, StepId, TodoId, ActionKey, ActorUserId,
             InstanceRevision, IdempotencyKey, CommentSummary, CreatedAtUtc)
        VALUES
            (@Id, @InstanceId, @StepId, @TodoId, @ActionKey, @ActorUserId,
             @InstanceRevision, @IdempotencyKey, @CommentSummary, @CreatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertExecutionLog = new(
        "workflow.execution_log.insert",
        """
        INSERT INTO fn_workflow_execution_log
            (Id, InstanceId, StepId, TransitionKey, FromStatusKey, ToStatusKey,
             IdempotencyKey, Summary, CreatedAtUtc)
        VALUES
            (@Id, @InstanceId, @StepId, @TransitionKey, @FromStatusKey, @ToStatusKey,
             @IdempotencyKey, @Summary, @CreatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindActionReceipt = new(
        "workflow.action_record.find_receipt",
        """
        SELECT action.ActionKey, action.ActorUserId, action.InstanceRevision,
               action.IdempotencyKey, log.Summary AS RequestHash,
               result_todo.Id AS ResultTodoId
        FROM fn_workflow_action_record AS action
        LEFT JOIN fn_workflow_execution_log AS log
         ON log.InstanceId = action.InstanceId
         AND log.IdempotencyKey = action.IdempotencyKey
         AND log.TransitionKey <> 'step.reactivated'
        LEFT JOIN fn_workflow_execution_log AS result_log
          ON result_log.InstanceId = action.InstanceId
         AND result_log.IdempotencyKey = action.IdempotencyKey
         AND result_log.TransitionKey = 'step.reactivated'
        LEFT JOIN fn_workflow_todo AS result_todo
          ON result_todo.StepId = result_log.StepId
        WHERE action.InstanceId = @InstanceId
          AND action.IdempotencyKey = @IdempotencyKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListExecutionLogsSqlServer = new(
        "workflow.execution_log.list.sqlserver",
        """
        SELECT TOP (@Take) log.Id, log.InstanceId, log.StepId, log.TransitionKey,
               log.FromStatusKey, log.ToStatusKey, log.IdempotencyKey,
               log.Summary, log.CreatedAtUtc
        FROM fn_workflow_execution_log AS log
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = log.InstanceId
        WHERE log.InstanceId = @InstanceId
          AND instance.TenantScopeKey = @TenantScopeKey
        ORDER BY log.CreatedAtUtc, log.Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListExecutionLogsMySql = new(
        "workflow.execution_log.list.mysql",
        """
        SELECT log.Id, log.InstanceId, log.StepId, log.TransitionKey,
               log.FromStatusKey, log.ToStatusKey, log.IdempotencyKey,
               log.Summary, log.CreatedAtUtc
        FROM fn_workflow_execution_log AS log
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = log.InstanceId
        WHERE log.InstanceId = @InstanceId
          AND instance.TenantScopeKey = @TenantScopeKey
        ORDER BY log.CreatedAtUtc, log.Id
        LIMIT @Take
        """,
        SqlDataScope.Global);

    /// <summary>列出当前用户在运行中实例上的活动待办；暂停实例不得出现在我的待办。</summary>
    public static readonly SqlStatement ListMineSqlServer = new(
        "workflow.todo.list_mine.sqlserver",
        """
        SELECT TOP (@Take) todo.Id, todo.InstanceId, todo.StepId, todo.AssigneeUserId,
               todo.StatusKey, todo.ArrivedAtUtc, todo.CompletedAtUtc,
               todo.ResultActionKey, todo.Revision
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = todo.InstanceId
        WHERE instance.TenantScopeKey = @TenantScopeKey
          AND todo.AssigneeUserId = @AssigneeUserId
          AND todo.StatusKey = 'active'
          AND instance.StatusKey = 'active'
        ORDER BY todo.ArrivedAtUtc DESC, todo.Id DESC
        """,
        SqlDataScope.Global);

    /// <summary>列出当前用户在运行中实例上的活动待办；暂停实例不得出现在我的待办。</summary>
    public static readonly SqlStatement ListMineMySql = new(
        "workflow.todo.list_mine.mysql",
        """
        SELECT todo.Id, todo.InstanceId, todo.StepId, todo.AssigneeUserId,
               todo.StatusKey, todo.ArrivedAtUtc, todo.CompletedAtUtc,
               todo.ResultActionKey, todo.Revision
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = todo.InstanceId
        WHERE instance.TenantScopeKey = @TenantScopeKey
          AND todo.AssigneeUserId = @AssigneeUserId
          AND todo.StatusKey = 'active'
          AND instance.StatusKey = 'active'
        ORDER BY todo.ArrivedAtUtc DESC, todo.Id DESC
        LIMIT @Take
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindTodoById = new(
        "workflow.todo.find_by_id",
        """
        SELECT todo.Id, todo.InstanceId, todo.StepId, todo.AssigneeUserId,
               todo.StatusKey, todo.ArrivedAtUtc, todo.CompletedAtUtc,
               todo.ResultActionKey, todo.Revision, step.NodeKey, step.Revision AS StepRevision
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance
            ON instance.Id = todo.InstanceId
        INNER JOIN fn_workflow_step AS step
            ON step.Id = todo.StepId
        WHERE todo.Id = @Id
          AND instance.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    /// <summary>在实例级 CAS 成功后分配下一步骤执行序号；同一事务内不存在并发写入者。</summary>
    public static readonly SqlStatement FindNextStepExecutionSequence = new(
        "workflow.step.find_next_execution_sequence",
        """
        SELECT COALESCE(MAX(ExecutionSequence), 0) + 1
        FROM fn_workflow_step
        WHERE InstanceId = @InstanceId
        """,
        SqlDataScope.Global);

    /// <summary>按最近完成顺序列出当前有效链上的可退回人工审批步骤。</summary>
    public static readonly SqlStatement ListTodoReturnTargetsSqlServer = new(
        "workflow.todo_return_target.list.sqlserver",
        """
        SELECT step.Id AS StepId, step.NodeKey, step.AssignedUserId,
               step.ExecutionSequence,
               step.StartedAtUtc, step.CompletedAtUtc
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = step.InstanceId
        WHERE step.InstanceId = @InstanceId
          AND step.NodeTypeKey = 'human.approval'
          AND step.StatusKey = 'completed'
          AND step.ExecutionSequence IS NOT NULL
          AND step.AssignedUserId IS NOT NULL
          AND step.CompletedAtUtc IS NOT NULL
          AND instance.StatusKey = 'active'
          AND instance.TenantScopeKey = @TenantScopeKey
        ORDER BY step.ExecutionSequence DESC, step.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.Global);

    /// <summary>按最近完成顺序列出当前有效链上的可退回人工审批步骤。</summary>
    public static readonly SqlStatement ListTodoReturnTargetsMySql = new(
        "workflow.todo_return_target.list.mysql",
        """
        SELECT step.Id AS StepId, step.NodeKey, step.AssignedUserId,
               step.ExecutionSequence,
               step.StartedAtUtc, step.CompletedAtUtc
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = step.InstanceId
        WHERE step.InstanceId = @InstanceId
          AND step.NodeTypeKey = 'human.approval'
          AND step.StatusKey = 'completed'
          AND step.ExecutionSequence IS NOT NULL
          AND step.AssignedUserId IS NOT NULL
          AND step.CompletedAtUtc IS NOT NULL
          AND instance.StatusKey = 'active'
          AND instance.TenantScopeKey = @TenantScopeKey
        ORDER BY step.ExecutionSequence DESC, step.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.Global);

    /// <summary>提交退回时在可信实例内重新锁定一个有效人工审批目标。</summary>
    public static readonly SqlStatement FindTodoReturnTarget = new(
        "workflow.todo_return_target.find",
        """
        SELECT step.Id AS StepId, step.NodeKey, step.AssignedUserId,
               step.ExecutionSequence,
               step.StartedAtUtc, step.CompletedAtUtc
        FROM fn_workflow_step AS step
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = step.InstanceId
        WHERE step.Id = @TargetStepId
          AND step.InstanceId = @InstanceId
          AND step.NodeTypeKey = 'human.approval'
          AND step.StatusKey = 'completed'
          AND step.ExecutionSequence IS NOT NULL
          AND step.AssignedUserId IS NOT NULL
          AND step.CompletedAtUtc IS NOT NULL
          AND instance.StatusKey = 'active'
          AND instance.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindActiveTodoByInstance = new(
        "workflow.todo.find_active_by_instance",
        """
        SELECT todo.Id, todo.InstanceId, todo.StepId, todo.AssigneeUserId,
               todo.StatusKey, todo.ArrivedAtUtc, todo.CompletedAtUtc,
               todo.ResultActionKey, todo.Revision
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = todo.InstanceId
        WHERE todo.InstanceId = @InstanceId
          AND todo.StatusKey = 'active'
          AND instance.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    /// <summary>读取实例详情所需的活动待办超时摘要。</summary>
    public static readonly SqlStatement FindActiveTodoTimeoutByInstance = new(
        "workflow.todo.find_active_timeout_by_instance",
        """
        SELECT todo.Id, todo.DueAtUtc, todo.ReminderCount, todo.EscalatedAtUtc
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = todo.InstanceId
        WHERE todo.InstanceId = @InstanceId
          AND todo.StatusKey = 'active'
          AND instance.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    /// <summary>读取运行或暂停实例上仍活动的步骤与待办，供生命周期转换保留原节点。</summary>
    public static readonly SqlStatement FindActiveWorkByInstance = new(
        "workflow.instance.find_active_work",
        """
        SELECT todo.Id AS TodoId, todo.Revision AS TodoRevision,
               step.Id AS StepId, step.Revision AS StepRevision
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_step AS step ON step.Id = todo.StepId
        INNER JOIN fn_workflow_instance AS instance ON instance.Id = todo.InstanceId
        WHERE todo.InstanceId = @InstanceId
          AND todo.StatusKey = 'active'
          AND step.StatusKey = 'active'
          AND instance.StatusKey IN ('active', 'suspended')
          AND instance.TenantScopeKey = @TenantScopeKey
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement IsInstanceParticipant = new(
        "workflow.instance.is_participant",
        """
        SELECT CASE WHEN EXISTS (
            SELECT 1
            FROM fn_workflow_todo AS todo
            INNER JOIN fn_workflow_instance AS instance ON instance.Id = todo.InstanceId
            WHERE todo.InstanceId = @InstanceId
              AND todo.AssigneeUserId = @ActorUserId
              AND instance.TenantScopeKey = @TenantScopeKey
        ) OR EXISTS (
            SELECT 1
            FROM fn_workflow_cc AS cc
            INNER JOIN fn_workflow_instance AS instance ON instance.Id = cc.InstanceId
            WHERE cc.InstanceId = @InstanceId
              AND cc.RecipientUserId = @ActorUserId
              AND instance.TenantScopeKey = @TenantScopeKey
        ) THEN 1 ELSE 0 END
        """,
        SqlDataScope.Global);

    /// <summary>以待办修订号和处理人共同保护完成动作；受影响行为零表示冲突或越权。</summary>
    public static readonly SqlStatement CompleteTodoWithRevision = new(
        "workflow.todo.complete_with_revision",
        """
        UPDATE fn_workflow_todo
        SET StatusKey = 'completed',
            CompletedAtUtc = @CompletedAtUtc,
            ResultActionKey = @ResultActionKey,
            Revision = Revision + 1
        WHERE Id = @Id
          AND AssigneeUserId = @AssigneeUserId
          AND StatusKey = 'active'
          AND Revision = @Revision
          AND EXISTS (
              SELECT 1
              FROM fn_workflow_instance AS instance
              WHERE instance.Id = fn_workflow_todo.InstanceId
                AND instance.TenantScopeKey = @TenantScopeKey)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CompleteStepWithRevision = new(
        "workflow.step.complete_with_revision",
        """
        UPDATE fn_workflow_step
        SET StatusKey = @StatusKey,
            CompletedAtUtc = @CompletedAtUtc,
            Revision = Revision + 1
        WHERE Id = @Id
          AND InstanceId = @InstanceId
          AND StatusKey = 'active'
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    /// <summary>把发起退回的当前活动步骤关闭为 returned。</summary>
    public static readonly SqlStatement ReturnStepWithRevision = new(
        "workflow.step.return_with_revision",
        """
        UPDATE fn_workflow_step
        SET StatusKey = 'returned',
            CompletedAtUtc = @CompletedAtUtc,
            Revision = Revision + 1
        WHERE Id = @Id
          AND InstanceId = @InstanceId
          AND StatusKey = 'active'
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    /// <summary>从目标开始失效旧执行链，避免旧目标完成记录与新步骤尝试重复成为候选。</summary>
    public static readonly SqlStatement RollBackCompletedStepsFromTarget = new(
        "workflow.step.rollback_completed_from_target",
        """
        UPDATE fn_workflow_step
        SET StatusKey = 'rolled_back',
            Revision = Revision + 1
        WHERE InstanceId = @InstanceId
          AND StatusKey = 'completed'
          AND ExecutionSequence >= @TargetExecutionSequence
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CompleteInstanceWithRevision = new(
        "workflow.instance.complete_with_revision",
        """
        UPDATE fn_workflow_instance
        SET StatusKey = @StatusKey,
            CompletedAtUtc = @CompletedAtUtc,
            Revision = Revision + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND StatusKey = 'active'
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    /// <summary>线性审批进入下一节点时只推进实例修订号，终态时间保持为空。</summary>
    public static readonly SqlStatement AdvanceInstanceWithRevision = new(
        "workflow.instance.advance_with_revision",
        """
        UPDATE fn_workflow_instance
        SET Revision = Revision + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND StatusKey = 'active'
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    /// <summary>在租户作用域和待办修订号保护下改派活动待办。</summary>
    public static readonly SqlStatement ReassignTodoWithRevision = new(
        "workflow.todo.reassign_with_revision",
        """
        UPDATE fn_workflow_todo
        SET AssigneeUserId = @AssigneeUserId,
            Revision = Revision + 1
        WHERE Id = @Id
          AND InstanceId = @InstanceId
          AND AssigneeUserId = @ExpectedAssigneeUserId
          AND StatusKey = 'active'
          AND Revision = @Revision
          AND EXISTS (
              SELECT 1
              FROM fn_workflow_instance AS instance
              WHERE instance.Id = fn_workflow_todo.InstanceId
                AND instance.StatusKey = 'active'
                AND instance.TenantScopeKey = @TenantScopeKey)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CancelTodoWithRevision = new(
        "workflow.todo.cancel_with_revision",
        """
        UPDATE fn_workflow_todo
        SET StatusKey = 'cancelled',
            CompletedAtUtc = @CompletedAtUtc,
            ResultActionKey = 'cancel',
            Revision = Revision + 1
        WHERE Id = @Id
          AND InstanceId = @InstanceId
          AND StatusKey = 'active'
          AND Revision = @Revision
          AND EXISTS (
              SELECT 1
              FROM fn_workflow_instance AS instance
              WHERE instance.Id = fn_workflow_todo.InstanceId
                AND instance.TenantScopeKey = @TenantScopeKey)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement CancelStepWithRevision = new(
        "workflow.step.cancel_with_revision",
        """
        UPDATE fn_workflow_step
        SET StatusKey = 'cancelled',
            CompletedAtUtc = @CompletedAtUtc,
            Revision = Revision + 1
        WHERE Id = @Id
          AND InstanceId = @InstanceId
          AND StatusKey = 'active'
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    /// <summary>取消运行中或已暂停实例，并释放执行租约；终态不得命中。</summary>
    public static readonly SqlStatement CancelInstanceWithRevision = new(
        "workflow.instance.cancel_with_revision",
        """
        UPDATE fn_workflow_instance
        SET StatusKey = 'cancelled',
            CancelledById = @CancelledById,
            CancelledAtUtc = @CancelledAtUtc,
            CancellationReason = @CancellationReason,
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            Revision = Revision + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND StatusKey IN ('active', 'suspended')
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 把运行中的实例暂停并释放租约；步骤和待办保持活动以便恢复后继续。
    /// </summary>
    public static readonly SqlStatement SuspendInstanceWithRevision = new(
        "workflow.instance.suspend_with_revision",
        """
        UPDATE fn_workflow_instance
        SET StatusKey = 'suspended',
            LeaseOwnerKey = NULL,
            LeaseExpiresAtUtc = NULL,
            Revision = Revision + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND StatusKey = 'active'
          AND Revision = @Revision
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 把已暂停实例恢复为运行中，并从原活动节点继续，不得新建步骤或待办。
    /// </summary>
    public static readonly SqlStatement ResumeInstanceWithRevision = new(
        "workflow.instance.resume_with_revision",
        """
        UPDATE fn_workflow_instance
        SET StatusKey = 'active',
            Revision = Revision + 1
        WHERE Id = @Id
          AND TenantScopeKey = @TenantScopeKey
          AND StatusKey = 'suspended'
          AND Revision = @Revision
        """,
        SqlDataScope.Global);
}
