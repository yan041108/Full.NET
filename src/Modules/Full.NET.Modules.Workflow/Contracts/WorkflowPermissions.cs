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
    /// <summary>查询表单定义与版本。</summary>
    public const string FormsRead = "workflow.forms.read";
    /// <summary>创建表单定义。</summary>
    public const string FormsCreate = "workflow.forms.create";
    /// <summary>编辑表单草稿。</summary>
    public const string FormsUpdate = "workflow.forms.update";
    /// <summary>发布表单版本。</summary>
    public const string FormsPublish = "workflow.forms.publish";
    /// <summary>查询工作流实例与轨迹。</summary>
    public const string InstancesRead = "workflow.instances.read";
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
        FormsRead,
        FormsCreate,
        FormsUpdate,
        FormsPublish,
        InstancesRead,
        InstancesStart,
        InstancesCancel,
        InstancesPause,
        InstancesResume,
        InstancesRecover,
        TodosRead,
        TodosApprove,
        TodosReject,
        TodosReturn,
        CcRead,
        CcMarkRead,
        RecoveryTasksRead,
        RecoveryTasksRetry,
        RecoveryTasksReconcile,
    ]);
}
