namespace Full.NET.Modules.Workflow.Domain;

/// <summary>工作流定义生命周期状态键。</summary>
internal static class WorkflowDefinitionStatusKeys
{
    /// <summary>可发布、可启动新实例。</summary>
    public const string Active = "active";

    /// <summary>阻止新实例与发布，允许继续维护草稿。</summary>
    public const string Disabled = "disabled";

    /// <summary>只读归档，禁止草稿、发布与状态回退。</summary>
    public const string Archived = "archived";
}
