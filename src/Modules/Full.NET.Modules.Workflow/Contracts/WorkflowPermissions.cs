namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>工作流管理页面与业务操作使用的精确权限码。</summary>
public static class WorkflowPermissions
{
    /// <summary>读取自己参与的双边协作授权。</summary>
    public const string CollaborationGrantsRead="workflow.collaboration_grants.read";
    /// <summary>以当前可信作用域提出协作同意。</summary>
    public const string CollaborationGrantsPropose="workflow.collaboration_grants.propose";
    /// <summary>以目标方身份接受固定版本和本地成员。</summary>
    public const string CollaborationGrantsAccept="workflow.collaboration_grants.accept";
    /// <summary>撤销自己一方的协作同意。</summary>
    public const string CollaborationGrantsRevoke="workflow.collaboration_grants.revoke";
    /// <summary>读取当前作用域路由状态及固定版本目标。</summary>
    public const string RoutesRead = "workflow.routes.read";
    /// <summary>人工恢复选择未知的固定路由。</summary>
    public const string RoutesReconcile = "workflow.routes.reconcile";
    /// <summary>读取当前作用域工作日历及固定版本。</summary>
    public const string CalendarsRead="workflow.calendars.read";
    /// <summary>创建当前作用域工作日历。</summary>
    public const string CalendarsCreate="workflow.calendars.create";
    /// <summary>修改工作日历草稿。</summary>
    public const string CalendarsUpdate="workflow.calendars.update";
    /// <summary>发布不可变工作日历版本。</summary>
    public const string CalendarsPublish="workflow.calendars.publish";
    /// <summary>对账受控连接器的未知结果及接纳服务器已保存的真实回执。</summary>
    public const string ConnectorCallsReconcile = "workflow.connector_calls.reconcile";
    /// <summary>查询当前可信作用域的连接器运维状态，不暴露原始业务载荷。</summary>
    public const string ConnectorCallsRead = "workflow.connector_calls.read";
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
    /// <summary>发起人按发布边界撤回本人实例，不授予管理员取消能力。</summary>
    public const string InstancesWithdraw = "workflow.instances.withdraw";
    /// <summary>发起人对本人实例的活动节点发起有界催办。</summary>
    public const string InstancesRemind = "workflow.instances.remind";
    /// <summary>暂停正在运行的工作流实例。</summary>
    public const string InstancesPause = "workflow.instances.pause";
    /// <summary>普通恢复已暂停的工作流实例。</summary>
    public const string InstancesResume = "workflow.instances.resume";
    /// <summary>强制恢复或改派工作流实例的高权限控制面。</summary>
    public const string InstancesRecover = "workflow.instances.recover";
    /// <summary>查询本人工作流待办。</summary>
    public const string TodosRead = "workflow.todos.read";
    /// <summary>为本人活动待办采集签名证据。</summary>
    public const string TodosSign = "workflow.todos.sign";
    /// <summary>提交完成本人办理任务，不授予审批权限。</summary>
    public const string TodosComplete = "workflow.todos.complete";
    /// <summary>提交本人独立选票，不授权普通审批或其他候选的表单。</summary>
    public const string TodosVote = "workflow.todos.vote";
    /// <summary>查看本人固定候选领取池。</summary>
    public const string ClaimPoolsRead = "workflow.claim_pools.read";
    /// <summary>以当前有效候选身份领取池内任务。</summary>
    public const string ClaimPoolsClaim = "workflow.claim_pools.claim";
    /// <summary>管理员把尚未领取的任务明确分配给同作用域活动成员。</summary>
    public const string ClaimPoolsAssign = "workflow.claim_pools.assign";
    /// <summary>按发布策略释放本人已经领取的任务。</summary>
    public const string TodosRelease = "workflow.todos.release";
    /// <summary>同意本人工作流待办。</summary>
    public const string TodosApprove = "workflow.todos.approve";
    /// <summary>拒绝本人工作流待办。</summary>
    public const string TodosReject = "workflow.todos.reject";
    /// <summary>把本人工作流待办退回到合法历史审批节点。</summary>
    public const string TodosReturn = "workflow.todos.return";
    /// <summary>对本人活动待办发起前加签或后加签。</summary>
    public const string TodosCountersign = "workflow.todos.countersign";
    /// <summary>把本人活动工作流待办转办给同作用域的可信活动成员。</summary>
    public const string TodosTransfer = "workflow.todos.transfer";
    /// <summary>把本人当前待办授权给同作用域代理人，仅覆盖这一份待办。</summary>
    public const string TodosDelegate = "workflow.todos.delegate";
    /// <summary>原办理人在代理办结前收回本人发出的待办代理。</summary>
    public const string TodosRecallDelegation = "workflow.todos.recall_delegation";
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
        CalendarsRead,
        CalendarsCreate,
        CalendarsUpdate,
        CalendarsPublish,
        ConnectorCallsRead,
        ConnectorCallsReconcile,
        RoutesRead,
        RoutesReconcile,
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
        InstancesWithdraw,
        InstancesRemind,
        InstancesPause,
        InstancesResume,
        InstancesRecover,
        TodosRead,
        TodosSign,
        TodosApprove,
        TodosComplete,
        TodosVote,
        ClaimPoolsRead,
        ClaimPoolsClaim,
        ClaimPoolsAssign,
        TodosRelease,
        TodosReject,
        TodosReturn,
        TodosCountersign,
        TodosTransfer,
        TodosDelegate,
        TodosRecallDelegation,
        CcRead,
        CcMarkRead,
        RecoveryTasksRead,
        RecoveryTasksRetry,
        RecoveryTasksReconcile,
    ]);
}
