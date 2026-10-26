using Full.NET.Data.Abstractions;
namespace Full.NET.Modules.Workflow.Persistence;

/// <summary>设计资产的所有读写显式限定可信作用域，并与定义修订在同一事务提交。</summary>
internal static class WorkflowDesignerAssetSql
{
    public static readonly SqlStatement FindBindings = new("workflow.designer_asset.bindings", """
        SELECT COALESCE(snapshot.BindingJson, version.WebRenderSchemaJson) FROM fn_workflow_form_version AS version
        LEFT JOIN fn_workflow_form_design_snapshot AS snapshot ON snapshot.FormVersionId = version.Id
        INNER JOIN fn_workflow_form_definition AS definition ON definition.Id = version.FormDefinitionId
        WHERE version.Id = @FormVersionId AND definition.TenantScopeKey = @TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Find = new("workflow.designer_asset.find", """
        SELECT asset.FormId, asset.TenantScopeKey, asset.SourceCommit, asset.FormatVersion, asset.DesignerJson, asset.ContentHash, definition.DraftRevision
        FROM fn_workflow_form_design_asset AS asset INNER JOIN fn_workflow_form_definition AS definition ON definition.Id = asset.FormId
        WHERE asset.FormId = @FormId AND asset.TenantScopeKey = @TenantScopeKey AND definition.TenantScopeKey = @TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Insert = new("workflow.designer_asset.insert", """
        INSERT INTO fn_workflow_form_design_asset (FormId, TenantScopeKey, SourceCommit, FormatVersion, DesignerJson, ContentHash)
        VALUES (@FormId, @TenantScopeKey, @SourceCommit, @FormatVersion, @DesignerJson, @ContentHash)
        """, SqlDataScope.Global);
    public static readonly SqlStatement Update = new("workflow.designer_asset.update", """
        UPDATE fn_workflow_form_design_asset SET SourceCommit = @SourceCommit, FormatVersion = @FormatVersion,
            DesignerJson = @DesignerJson, ContentHash = @ContentHash
        WHERE FormId = @FormId AND TenantScopeKey = @TenantScopeKey
        """, SqlDataScope.Global);
    public static readonly SqlStatement Snapshot = new("workflow.designer_asset.snapshot", """
        INSERT INTO fn_workflow_form_design_snapshot (FormVersionId, SourceCommit, FormatVersion, DesignerJson, DesignHash, RuntimeVersion, BindingJson)
        VALUES (@FormVersionId, @SourceCommit, @FormatVersion, @DesignerJson, @DesignHash, @RuntimeVersion, @BindingJson)
        """, SqlDataScope.Global);
}

/// <summary>完整设计资产的显式数据库投影。</summary>
internal sealed record WorkflowDesignerAssetRecord(Guid FormId, string TenantScopeKey, string SourceCommit, int FormatVersion, string DesignerJson, string ContentHash, long DraftRevision);
