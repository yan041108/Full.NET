using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>固定父子版本依赖属于Workflow；版本删除与发布按定义行串行化，并由外键裁决最终并发窗口。</summary>
internal static class WorkflowVersionReferenceSql
{
    public static readonly SqlStatement LockDefinition = new("workflow.definition_version.lock_definition", """
        UPDATE fn_workflow_definition SET Version=Version WHERE Id=@DefinitionId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement HasReferences = new("workflow.definition_version.has_references", """
        SELECT CASE WHEN EXISTS (SELECT 1 FROM fn_workflow_instance WHERE DefinitionVersionId=@DefinitionVersionId AND TenantScopeKey=@TenantScopeKey)
          OR EXISTS (SELECT 1 FROM fn_workflow_subprocess_call WHERE ChildDefinitionVersionId=@DefinitionVersionId AND TenantScopeKey=@TenantScopeKey)
          OR EXISTS (SELECT 1 FROM fn_workflow_subprocess_ref WHERE ChildVersionId=@DefinitionVersionId AND TenantScopeKey=@TenantScopeKey)
        THEN 1 ELSE 0 END
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.definition_version.insert_reference", """
        INSERT INTO fn_workflow_subprocess_ref (ParentVersionId,ChildVersionId,TenantScopeKey,CreatedAtUtc)
        SELECT parentVersion.Id,childVersion.Id,@TenantScopeKey,@CreatedAtUtc
        FROM fn_workflow_definition_version AS parentVersion
        INNER JOIN fn_workflow_definition AS parentDefinition ON parentDefinition.Id=parentVersion.DefinitionId
        INNER JOIN fn_workflow_definition_version AS childVersion ON childVersion.Id=@ChildVersionId
        INNER JOIN fn_workflow_definition AS childDefinition ON childDefinition.Id=childVersion.DefinitionId
        WHERE parentVersion.Id=@ParentVersionId AND parentDefinition.Id=@DefinitionId AND parentDefinition.TenantScopeKey=@TenantScopeKey
          AND childDefinition.TenantScopeKey=@TenantScopeKey AND childDefinition.StatusKey='active' AND parentVersion.Id<>childVersion.Id
        """, SqlDataScope.Global);
    public static readonly SqlStatement DeleteOwned = new("workflow.definition_version.delete_owned_references", """
        DELETE FROM fn_workflow_subprocess_ref WHERE ParentVersionId=@DefinitionVersionId AND TenantScopeKey=@TenantScopeKey
        """, SqlDataScope.Global);
}
