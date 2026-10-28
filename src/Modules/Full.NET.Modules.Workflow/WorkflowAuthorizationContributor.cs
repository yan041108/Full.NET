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

    /// <summary>获取工作流精确权限目录，包含暂停与普通恢复。</summary>
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        Permission(WorkflowPermissions.CollaborationGrantsRead,"读取双边协作授权"),
        Permission(WorkflowPermissions.CollaborationGrantsPropose,"提出双边协作同意"),
        Permission(WorkflowPermissions.CollaborationGrantsAccept,"接受双边协作同意"),
        Permission(WorkflowPermissions.CollaborationGrantsRevoke,"撤销双边协作同意"),
        Permission(WorkflowPermissions.CalendarsRead,"读取工作日历及版本"),
        Permission(WorkflowPermissions.CalendarsCreate,"创建工作日历"),
        Permission(WorkflowPermissions.CalendarsUpdate,"编辑工作日历草稿"),
        Permission(WorkflowPermissions.CalendarsPublish,"发布工作日历版本"),
        Permission(WorkflowPermissions.DefinitionsRead, "查询工作流定义与版本"),
        Permission(WorkflowPermissions.DefinitionsCreate, "创建工作流定义"),
        Permission(WorkflowPermissions.DefinitionsUpdate, "编辑工作流草稿"),
        Permission(WorkflowPermissions.DefinitionsPublish, "发布工作流版本"),
        Permission(WorkflowPermissions.DefinitionsManageStatus, "变更工作流定义状态"),
        Permission(WorkflowPermissions.DefinitionsDeleteVersion, "删除工作流定义版本"),
        Permission(WorkflowPermissions.FormsRead, "查询表单定义与版本"),
        Permission(WorkflowPermissions.FormsCreate, "创建表单定义"),
        Permission(WorkflowPermissions.FormsUpdate, "编辑表单草稿"),
        Permission(WorkflowPermissions.FormsPublish, "发布表单版本"),
        Permission(WorkflowPermissions.FormsManageStatus, "变更工作流表单状态"),
        Permission(WorkflowPermissions.FormsDeleteVersion, "删除工作流表单版本"),
        Permission(WorkflowPermissions.InstancesRead, "查询工作流实例与轨迹"),
        Permission(WorkflowPermissions.ConnectorCallsReconcile, "对账工作流连接器结果"),
        Permission(WorkflowPermissions.ConnectorCallsRead, "查询工作流连接器运维状态"),
        Permission(WorkflowPermissions.RoutesRead, "查询工作流路由状态"),
        Permission(WorkflowPermissions.RoutesReconcile, "恢复工作流路由选择"),
        Permission(WorkflowPermissions.InstancesList, "分页查询全部工作流实例"),
        Permission(WorkflowPermissions.InstancesStart, "启动工作流实例"),
        Permission(WorkflowPermissions.InstancesCancel, "取消工作流实例"),
        Permission(WorkflowPermissions.InstancesWithdraw, "按发布边界撤回本人实例"),
        Permission(WorkflowPermissions.InstancesRemind, "催办本人实例的活动节点"),
        Permission(WorkflowPermissions.InstancesPause, "暂停工作流实例"),
        Permission(WorkflowPermissions.InstancesResume, "恢复已暂停的工作流实例"),
        Permission(WorkflowPermissions.InstancesRecover, "强制恢复或改派工作流实例"),
        Permission(WorkflowPermissions.TodosRead, "查询本人工作流待办"),
        Permission(WorkflowPermissions.TodosSign, "采集本人工作流签名"),
        Permission(WorkflowPermissions.TodosComplete, "提交完成本人办理任务"),
        Permission(WorkflowPermissions.TodosVote, "提交本人独立选票"),
        Permission(WorkflowPermissions.ClaimPoolsRead, "查询本人候选领取池"),
        Permission(WorkflowPermissions.ClaimPoolsClaim, "领取候选任务"),
        Permission(WorkflowPermissions.ClaimPoolsAssign, "管理员分配领取池任务"),
        Permission(WorkflowPermissions.TodosRelease, "释放本人领取任务"),
        Permission(WorkflowPermissions.TodosApprove, "同意本人工作流待办"),
        Permission(WorkflowPermissions.TodosReject, "拒绝本人工作流待办"),
        Permission(WorkflowPermissions.TodosReturn, "退回本人工作流待办"),
        Permission(WorkflowPermissions.TodosCountersign, "加签本人工作流待办"),
        Permission(WorkflowPermissions.TodosTransfer, "转办本人工作流待办"),
        Permission(WorkflowPermissions.TodosDelegate, "委托本人工作流待办代理"),
        Permission(WorkflowPermissions.TodosRecallDelegation, "收回本人工作流待办代理"),
        Permission(WorkflowPermissions.CcRead, "查询本人工作流抄送"),
        Permission(WorkflowPermissions.CcMarkRead, "标记本人工作流抄送已读"),
        Permission(WorkflowPermissions.RecoveryTasksRead, "查询工作流恢复任务"),
        Permission(WorkflowPermissions.RecoveryTasksRetry, "人工重试工作流恢复任务"),
        Permission(WorkflowPermissions.RecoveryTasksReconcile, "对账工作流恢复任务"),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } =
    [
        NavigationItem("workflow-definitions", "/workflow/definitions", "工作流定义", 10, WorkflowPermissions.DefinitionsRead),
        NavigationItem("workflow-forms", "/workflow/forms", "工作流表单", 20, WorkflowPermissions.FormsRead),
        NavigationItem("workflow-calendars", "/workflow/calendars", "工作日历", 25, WorkflowPermissions.CalendarsRead),
        NavigationItem("workflow-instances", "/workflow/instances", "工作流实例", 30, WorkflowPermissions.InstancesRead),
        NavigationItem("workflow-todos", "/workflow/todos", "我的待办", 40, WorkflowPermissions.TodosRead),
        NavigationItem("workflow-claim-pools", "/workflow/claim-pools", "待领取任务", 45, WorkflowPermissions.ClaimPoolsRead),
        NavigationItem("workflow-cc", "/workflow/cc", "我的抄送", 50, WorkflowPermissions.CcRead),
        NavigationItem("workflow-recovery-tasks", "/workflow/recovery-tasks", "恢复任务", 60, WorkflowPermissions.RecoveryTasksRead),
    ];

    /// <summary>获取实例页上的精确操作，暂停、恢复与强制恢复各自独立授权。</summary>
    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } =
    [
        Action("workflow.calendars.create","workflow-calendars",WorkflowPermissions.CalendarsCreate,"创建工作日历","create",10),
        Action("workflow.calendars.update","workflow-calendars",WorkflowPermissions.CalendarsUpdate,"编辑日历草稿","update",20),
        Action("workflow.calendars.publish","workflow-calendars",WorkflowPermissions.CalendarsPublish,"发布日历版本","publish",30),
        Action("workflow.connector_calls.read","workflow-instances",WorkflowPermissions.ConnectorCallsRead,"查询连接器状态","read_connectors",79),
        Action("workflow.definitions.create", "workflow-definitions", WorkflowPermissions.DefinitionsCreate, "新建定义", "create", 10),
        Action("workflow.definitions.update", "workflow-definitions", WorkflowPermissions.DefinitionsUpdate, "编辑草稿", "update", 20),
        Action("workflow.definitions.publish", "workflow-definitions", WorkflowPermissions.DefinitionsPublish, "发布版本", "publish", 30),
        Action("workflow.definitions.manage-status", "workflow-definitions", WorkflowPermissions.DefinitionsManageStatus, "管理定义状态", "manage-status", 40),
        Action("workflow.definitions.delete-version", "workflow-definitions", WorkflowPermissions.DefinitionsDeleteVersion, "删除版本", "delete-version", 50),
        Action("workflow.forms.create", "workflow-forms", WorkflowPermissions.FormsCreate, "新建表单", "create", 10),
        Action("workflow.forms.update", "workflow-forms", WorkflowPermissions.FormsUpdate, "编辑表单", "update", 20),
        Action("workflow.forms.publish", "workflow-forms", WorkflowPermissions.FormsPublish, "发布表单", "publish", 30),
        Action("workflow.forms.manage-status", "workflow-forms", WorkflowPermissions.FormsManageStatus, "管理表单状态", "manage-status", 40),
        Action("workflow.forms.delete-version", "workflow-forms", WorkflowPermissions.FormsDeleteVersion, "删除版本", "delete-version", 50),
        Action("workflow.instances.start", "workflow-instances", WorkflowPermissions.InstancesStart, "启动实例", "start", 10),
        Action("workflow.instances.list", "workflow-instances", WorkflowPermissions.InstancesList, "查看全部实例", "list", 12),
        Action("workflow.instances.cancel", "workflow-instances", WorkflowPermissions.InstancesCancel, "取消实例", "cancel", 20),
        Action("workflow.instances.withdraw", "workflow-instances", WorkflowPermissions.InstancesWithdraw, "撤回本人实例", "withdraw", 21),
        Action("workflow.instances.remind", "workflow-instances", WorkflowPermissions.InstancesRemind, "催办本人实例", "remind", 22),
        Action("workflow.instances.pause", "workflow-instances", WorkflowPermissions.InstancesPause, "暂停实例", "pause", 25),
        Action("workflow.instances.resume", "workflow-instances", WorkflowPermissions.InstancesResume, "恢复实例", "resume", 28),
        Action("workflow.instances.recover", "workflow-instances", WorkflowPermissions.InstancesRecover, "强制恢复或改派", "recover", 30),
        Action("workflow.todos.approve", "workflow-todos", WorkflowPermissions.TodosApprove, "同意待办", "approve", 10),
        Action("workflow.todos.complete", "workflow-todos", WorkflowPermissions.TodosComplete, "提交完成", "complete", 15),
        Action("workflow.todos.vote", "workflow-todos", WorkflowPermissions.TodosVote, "提交选票", "vote", 16),
        Action("workflow.claim_pools.claim", "workflow-claim-pools", WorkflowPermissions.ClaimPoolsClaim, "领取任务", "claim", 61),
        Action("workflow.claim_pools.assign", "workflow-claim-pools", WorkflowPermissions.ClaimPoolsAssign, "分配候选任务", "assign", 62),
        Action("workflow.todos.release", "workflow-todos", WorkflowPermissions.TodosRelease, "释放领取任务", "release", 63),
        Action("workflow.todos.sign", "workflow-todos", WorkflowPermissions.TodosSign, "采集签名", "sign", 5),
        Action("workflow.todos.reject", "workflow-todos", WorkflowPermissions.TodosReject, "拒绝待办", "reject", 20),
        Action("workflow.todos.return", "workflow-todos", WorkflowPermissions.TodosReturn, "退回待办", "return", 30),
        Action("workflow.todos.countersign", "workflow-todos", WorkflowPermissions.TodosCountersign, "加签待办", "countersign", 40),
        Action("workflow.todos.transfer", "workflow-todos", WorkflowPermissions.TodosTransfer, "转办待办", "transfer", 50),
        Action("workflow.todos.delegate", "workflow-todos", WorkflowPermissions.TodosDelegate, "委托待办代理", "delegate", 51),
        Action("workflow.todos.recall_delegation", "workflow-todos", WorkflowPermissions.TodosRecallDelegation, "收回待办代理", "recall_delegation", 52),
        Action("workflow.cc.mark-read", "workflow-cc", WorkflowPermissions.CcMarkRead, "标记已读", "mark-read", 10),
        Action("workflow.recovery_tasks.retry", "workflow-recovery-tasks", WorkflowPermissions.RecoveryTasksRetry, "重试恢复任务", "retry", 10),
        Action("workflow.connector_calls.reconcile", "workflow-instances", WorkflowPermissions.ConnectorCallsReconcile, "对账连接器结果", "reconcile_connector", 80),
        Action("workflow.routes.reconcile", "workflow-instances", WorkflowPermissions.RoutesReconcile, "恢复路由选择", "reconcile_route", 81),
        Action("workflow.recovery_tasks.reconcile", "workflow-recovery-tasks", WorkflowPermissions.RecoveryTasksReconcile, "对账恢复任务", "reconcile", 20),
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
