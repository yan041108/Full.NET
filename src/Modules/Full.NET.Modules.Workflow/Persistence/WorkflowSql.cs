using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>Workflow 模块参数化 SQL 语句集；跨 Host/Tenant 查询必须显式携带 TenantScopeKey。</summary>
internal static class WorkflowSql
{
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
        SELECT version.Id, version.DefinitionId, version.VersionNumber,
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
