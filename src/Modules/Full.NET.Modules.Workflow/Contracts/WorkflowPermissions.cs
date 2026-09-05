namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>工作流管理页面与业务操作使用的精确权限码。</summary>
public static class WorkflowPermissions
{
    /// <summary>查询工作流定义与版本。</summary>
    public const string DefinitionsRead = "workflow.definitions.read";
    /// <summary>创建工作流定义。</summary>
    public const string DefinitionsCreate = "workflow.definitions.create";
    /// <summary>编辑工作流草稿。</summary>
    public const string DefinitionsUpdate = "workflow.definitions.update";
    /// <summary>发布工作流版本。</summary>
    public const string DefinitionsPublish = "workflow.definitions.publish";
    /// <summary>变更工作流定义启停或归档状态。</summary>
    public const string DefinitionsManageStatus = "workflow.definitions.manage_status";
    /// <summary>删除未被运行实例引用的工作流定义版本。</summary>
    public const string DefinitionsDeleteVersion = "workflow.definitions.delete_version";
    /// <summary>查询表单定义与版本。</summary>
    public const string FormsRead = "workflow.forms.read";
    /// <summary>创建表单定义。</summary>
    public const string FormsCreate = "workflow.forms.create";
    /// <summary>编辑表单草稿。</summary>
    public const string FormsUpdate = "workflow.forms.update";
    /// <summary>发布表单版本。</summary>
    public const string FormsPublish = "workflow.forms.publish";
    /// <summary>变更工作流表单启停或归档状态。</summary>
    public const string FormsManageStatus = "workflow.forms.manage_status";
    /// <summary>删除未被运行实例引用的工作流表单版本。</summary>
    public const string FormsDeleteVersion = "workflow.forms.delete_version";
    /// <summary>查询工作流实例与轨迹。</summary>
    public const string InstancesRead = "workflow.instances.read";
    /// <summary>分页查询当前作用域内全部工作流实例。</summary>
    public const string InstancesList = "workflow.instances.list";
    /// <summary>启动工作流实例。</summary>
    public const string InstancesStart = "workflow.instances.start";
    /// <summary>取消工作流实例。</summary>
    public const string InstancesCancel = "workflow.instances.cancel";
    /// <summary>暂停正在运行的工作流实例。</summary>
    public const string InstancesPause = "workflow.instances.pause";
    /// <summary>普通恢复已暂停的工作流实例。</summary>
    public const string InstancesResume = "workflow.instances.resume";
    /// <summary>强制恢复或改派工作流实例的高权限控制面。</summary>
    public const string InstancesRecover = "workflow.instances.recover";
    /// <summary>查询本人工作流待办。</summary>
    public const string TodosRead = "workflow.todos.read";
    /// <summary>同意本人工作流待办。</summary>
    public const string TodosApprove = "workflow.todos.approve";
    /// <summary>拒绝本人工作流待办。</summary>
    public const string TodosReject = "workflow.todos.reject";
    /// <summary>把本人工作流待办退回到合法历史审批节点。</summary>
    public const string TodosReturn = "workflow.todos.return";
    /// <summary>对本人活动待办发起前加签或后加签。</summary>
    public const string TodosCountersign = "workflow.todos.countersign";
    /// <summary>查询本人工作流抄送。</summary>
    public const string CcRead = "workflow.cc.read";
    /// <summary>标记本人工作流抄送已读。</summary>
    public const string CcMarkRead = "workflow.cc.mark_read";
    /// <summary>查询工作流恢复任务。</summary>
    public const string RecoveryTasksRead = "workflow.recovery_tasks.read";
    /// <summary>人工重试工作流恢复任务。</summary>
    public const string RecoveryTasksRetry = "workflow.recovery_tasks.retry";
    /// <summary>对账并收敛工作流恢复任务。</summary>
    public const string RecoveryTasksReconcile = "workflow.recovery_tasks.reconcile";

    /// <summary>获取当前模块全部精确权限码，供授权目录与测试枚举。</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        DefinitionsRead,
        DefinitionsCreate,
        DefinitionsUpdate,
        DefinitionsPublish,
        DefinitionsManageStatus,
        DefinitionsDeleteVersion,
        FormsRead,
        FormsCreate,
        FormsUpdate,
        FormsPublish,
        FormsManageStatus,
        FormsDeleteVersion,
        InstancesRead,
        InstancesList,
        InstancesStart,
        InstancesCancel,
        InstancesPause,
        InstancesResume,
        InstancesRecover,
        TodosRead,
        TodosApprove,
        TodosReject,
        TodosReturn,
        TodosCountersign,
        CcRead,
        CcMarkRead,
        RecoveryTasksRead,
        RecoveryTasksRetry,
        RecoveryTasksReconcile,
    ]);
}
