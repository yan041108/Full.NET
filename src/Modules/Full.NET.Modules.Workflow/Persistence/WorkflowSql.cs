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

    public static readonly SqlStatement FindTodoById = new(
        "workflow.todo.find_by_id",
        """
        SELECT todo.Id, todo.InstanceId, todo.StepId, todo.AssigneeUserId,
               todo.StatusKey, todo.ArrivedAtUtc, todo.CompletedAtUtc,
               todo.ResultActionKey, todo.Revision
        FROM fn_workflow_todo AS todo
        INNER JOIN fn_workflow_instance AS instance
            ON instance.Id = todo.InstanceId
        WHERE todo.Id = @Id
          AND instance.TenantScopeKey = @TenantScopeKey
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
          AND StatusKey = 'pending'
          AND Revision = @Revision
          AND EXISTS (
              SELECT 1
              FROM fn_workflow_instance AS instance
              WHERE instance.Id = fn_workflow_todo.InstanceId
                AND instance.TenantScopeKey = @TenantScopeKey)
        """,
        SqlDataScope.Global);
}
