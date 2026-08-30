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
          AND StatusKey = 'active'
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
             DueAtUtc, AttemptCount, Revision, StartedAtUtc, CompletedAtUtc)
        VALUES
            (@Id, @InstanceId, @NodeKey, 'human.approval', 'active', @AssignedUserId,
             NULL, 0, 1, @StartedAtUtc, NULL)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertTodo = new(
        "workflow.todo.insert",
        """
        INSERT INTO fn_workflow_todo
            (Id, InstanceId, StepId, AssigneeUserId, StatusKey, Revision,
             ArrivedAtUtc, CompletedAtUtc, ResultActionKey)
        VALUES
            (@Id, @InstanceId, @StepId, @AssigneeUserId, 'active', 1,
             @ArrivedAtUtc, NULL, NULL)
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
               action.IdempotencyKey, log.Summary AS RequestHash
        FROM fn_workflow_action_record AS action
        LEFT JOIN fn_workflow_execution_log AS log
          ON log.InstanceId = action.InstanceId
         AND log.IdempotencyKey = action.IdempotencyKey
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
        ORDER BY todo.ArrivedAtUtc DESC, todo.Id DESC
        """,
        SqlDataScope.Global);

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
        ORDER BY todo.ArrivedAtUtc DESC, todo.Id DESC
        LIMIT @Take
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindTodoById = new(
        "workflow.todo.find_by_id",
        """
        SELECT todo.Id, todo.InstanceId, todo.StepId, todo.AssigneeUserId,
               todo.StatusKey, todo.ArrivedAtUtc, todo.CompletedAtUtc,
               todo.ResultActionKey, todo.Revision, step.NodeKey
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance
            ON instance.Id = todo.InstanceId
        INNER JOIN fn_workflow_step AS step
            ON step.Id = todo.StepId
        WHERE todo.Id = @Id
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
}
