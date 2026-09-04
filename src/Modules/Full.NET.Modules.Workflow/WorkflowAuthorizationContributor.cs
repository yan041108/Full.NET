using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow;

/// <summary>向 Identity 授权目录贡献工作流页面、精确动作和 Host/Tenant 权限。</summary>
internal sealed class WorkflowAuthorizationContributor : IAuthorizationCatalogContributor
{
    private const AuthorizationScope SupportedScopes =
        AuthorizationScope.Host | AuthorizationScope.Tenant;

    public AuthorizationModuleDefinition Module { get; } =
        new("workflow", "工作流", 95);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        Permission(WorkflowPermissions.DefinitionsRead, "查询工作流定义与版本"),
        Permission(WorkflowPermissions.DefinitionsCreate, "创建工作流定义"),
        Permission(WorkflowPermissions.DefinitionsUpdate, "编辑工作流草稿"),
        Permission(WorkflowPermissions.DefinitionsPublish, "发布工作流版本"),
        Permission(WorkflowPermissions.FormsRead, "查询表单定义与版本"),
        Permission(WorkflowPermissions.FormsCreate, "创建表单定义"),
        Permission(WorkflowPermissions.FormsUpdate, "编辑表单草稿"),
        Permission(WorkflowPermissions.FormsPublish, "发布表单版本"),
        Permission(WorkflowPermissions.InstancesRead, "查询工作流实例与轨迹"),
        Permission(WorkflowPermissions.InstancesStart, "启动工作流实例"),
        Permission(WorkflowPermissions.InstancesCancel, "取消工作流实例"),
        Permission(WorkflowPermissions.InstancesRecover, "恢复或改派工作流实例"),
        Permission(WorkflowPermissions.TodosRead, "查询本人工作流待办"),
        Permission(WorkflowPermissions.TodosApprove, "同意本人工作流待办"),
        Permission(WorkflowPermissions.TodosReject, "拒绝本人工作流待办"),
        Permission(WorkflowPermissions.CcRead, "查询本人工作流抄送"),
        Permission(WorkflowPermissions.CcMarkRead, "标记本人工作流抄送已读"),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        NavigationItem("workflow-definitions", "/workflow/definitions", "工作流定义", 10, WorkflowPermissions.DefinitionsRead),
        NavigationItem("workflow-forms", "/workflow/forms", "工作流表单", 20, WorkflowPermissions.FormsRead),
        NavigationItem("workflow-instances", "/workflow/instances", "工作流实例", 30, WorkflowPermissions.InstancesRead),
        NavigationItem("workflow-todos", "/workflow/todos", "我的待办", 40, WorkflowPermissions.TodosRead),
        NavigationItem("workflow-cc", "/workflow/cc", "我的抄送", 50, WorkflowPermissions.CcRead),
    ];

    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        Action("workflow.definitions.create", "workflow-definitions", WorkflowPermissions.DefinitionsCreate, "新建定义", "create", 10),
        Action("workflow.definitions.update", "workflow-definitions", WorkflowPermissions.DefinitionsUpdate, "编辑草稿", "update", 20),
        Action("workflow.definitions.publish", "workflow-definitions", WorkflowPermissions.DefinitionsPublish, "发布版本", "publish", 30),
        Action("workflow.forms.create", "workflow-forms", WorkflowPermissions.FormsCreate, "新建表单", "create", 10),
        Action("workflow.forms.update", "workflow-forms", WorkflowPermissions.FormsUpdate, "编辑表单", "update", 20),
        Action("workflow.forms.publish", "workflow-forms", WorkflowPermissions.FormsPublish, "发布表单", "publish", 30),
        Action("workflow.instances.start", "workflow-instances", WorkflowPermissions.InstancesStart, "启动实例", "start", 10),
        Action("workflow.instances.cancel", "workflow-instances", WorkflowPermissions.InstancesCancel, "取消实例", "cancel", 20),
        Action("workflow.instances.recover", "workflow-instances", WorkflowPermissions.InstancesRecover, "恢复或改派", "recover", 30),
        Action("workflow.todos.approve", "workflow-todos", WorkflowPermissions.TodosApprove, "同意待办", "approve", 10),
        Action("workflow.todos.reject", "workflow-todos", WorkflowPermissions.TodosReject, "拒绝待办", "reject", 20),
        Action("workflow.cc.mark-read", "workflow-cc", WorkflowPermissions.CcMarkRead, "标记已读", "mark-read", 10),
    ];

    private static PermissionDefinition Permission(string code, string name) =>
        new(code, name, SupportedScopes);

    private static NavigationDefinition NavigationItem(
        string id,
        string path,
        string title,
        int order,
        string permission) =>
        new(id, null, id, path, id, title, title, "workflow", 95 + order, permission);

    private static AuthorizationActionDefinition Action(
        string id,
        string navigationId,
        string permission,
        string name,
        string actionKey,
        int order) =>
        new(id, navigationId, permission, name, actionKey, order);
}
