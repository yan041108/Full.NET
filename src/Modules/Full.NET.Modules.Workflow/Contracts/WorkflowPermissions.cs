namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>工作流管理页面与业务操作使用的精确权限码。</summary>
public static class WorkflowPermissions
{
    public const string DefinitionsRead = "workflow.definitions.read";
    public const string DefinitionsCreate = "workflow.definitions.create";
    public const string DefinitionsUpdate = "workflow.definitions.update";
    public const string DefinitionsPublish = "workflow.definitions.publish";
    public const string FormsRead = "workflow.forms.read";
    public const string FormsCreate = "workflow.forms.create";
    public const string FormsUpdate = "workflow.forms.update";
    public const string FormsPublish = "workflow.forms.publish";
    public const string InstancesRead = "workflow.instances.read";
    public const string InstancesStart = "workflow.instances.start";
    public const string InstancesCancel = "workflow.instances.cancel";
    public const string InstancesRecover = "workflow.instances.recover";
    public const string TodosRead = "workflow.todos.read";
    public const string TodosApprove = "workflow.todos.approve";
    public const string TodosReject = "workflow.todos.reject";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        DefinitionsRead,
        DefinitionsCreate,
        DefinitionsUpdate,
        DefinitionsPublish,
        FormsRead,
        FormsCreate,
        FormsUpdate,
        FormsPublish,
        InstancesRead,
        InstancesStart,
        InstancesCancel,
        InstancesRecover,
        TodosRead,
        TodosApprove,
        TodosReject,
    ]);
}
