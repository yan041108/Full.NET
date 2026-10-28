namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>
/// 定义工作流编译与运行时使用的稳定机器错误码。
/// </summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加。</remarks>
public static class WorkflowErrorCodes
{
    /// <summary>双边政策、目标版本或本地活动成员无效。</summary>
    public const string CollaborationGrantInvalid="workflow.collaboration_grant.invalid";
    /// <summary>当前可信作用域不是授权参与方或授权不存在。</summary>
    public const string CollaborationGrantNotFound="workflow.collaboration_grant.not_found";
    /// <summary>授权修订、状态或幂等载荷冲突。</summary>
    public const string CollaborationGrantConflict="workflow.collaboration_grant.conflict";
    /// <summary>业务所有者保留类型只能从实际业务入口经受信端口发起。</summary>
    public const string BusinessOwnerRequired = "workflow.business_owner_required";
    /// <summary>实际业务动作不在当前目录或不符合受控根审批最终提交位置。</summary>
    public const string DefinitionBusinessCommandInvalid = "workflow.definition.business_command_invalid";
    /// <summary>固定子版本、字段映射或层级执行额度无效。</summary>
    public const string DefinitionSubprocessInvalid = "workflow.definition.subprocess_invalid";
    /// <summary>原父活动、子输入或剩余额度不允许激活。</summary>
    public const string SubprocessActivationInvalid = "workflow.subprocess.activation_invalid";
    /// <summary>工作日历窗口、时区或请求结构无效。</summary>
    public const string CalendarInvalid=Prefix+"calendar.invalid";
    /// <summary>当前作用域不存在所选工作日历。</summary>
    public const string CalendarNotFound=Prefix+"calendar.not_found";
    /// <summary>当前作用域已存在相同稳定日历键。</summary>
    public const string CalendarKeyExists=Prefix+"calendar.key_exists";
    /// <summary>当前作用域不存在所选不可变日历版本。</summary>
    public const string CalendarVersionNotFound=Prefix+"calendar.version_not_found";
    /// <summary>工作流模块错误码统一前缀；所有稳定错误码均以此前缀开头。</summary>
    public const string Prefix = "workflow.";
    /// <summary>工作流定义包含未知节点类型；调用方应使用已注册节点类型重新提交定义。</summary>
    public const string DefinitionNodeTypeUnknown = Prefix + "definition.node_type_unknown";
    /// <summary>工作流定义引用的节点类型在当前运行时不可用；调用方应确认节点类型是否已注册或依赖模块是否加载。</summary>
    public const string DefinitionNodeTypeUnavailable = Prefix + "definition.node_type_unavailable";
    /// <summary>工作流定义中节点键重复；调用方应修正重复节点键后重新保存。</summary>
    public const string DefinitionNodeKeyDuplicate = Prefix + "definition.node_key_duplicate";
    /// <summary>工作流定义存在悬空引用；调用方应检查节点引用的目标节点或字段是否存在。</summary>
    public const string DefinitionReferenceDangling = Prefix + "definition.reference_dangling";
    /// <summary>工作流定义中存在不可达节点；调用方应修正分支或连线使所有节点可达。</summary>
    public const string DefinitionNodeUnreachable = Prefix + "definition.node_unreachable";
    /// <summary>工作流定义缺少结束节点；调用方应补充有效的结束节点。</summary>
    public const string DefinitionEndMissing = Prefix + "definition.end_missing";
    /// <summary>工作流定义存在非法回边；调用方应移除禁止的循环连线。</summary>
    public const string DefinitionBackEdgeIllegal = Prefix + "definition.back_edge_illegal";
    /// <summary>工作流定义的起始节点配置无效；调用方应确保仅有一个合法起始节点。</summary>
    public const string DefinitionStartInvalid = Prefix + "definition.start_invalid";
    /// <summary>工作流定义 Schema 版本不被支持；调用方应使用兼容的 Schema 版本。</summary>
    public const string DefinitionSchemaUnsupported = Prefix + "definition.schema_unsupported";
    /// <summary>工作流定义字段策略配置无效；调用方应检查字段权限或映射规则。</summary>
    public const string DefinitionFieldPolicyInvalid = Prefix + "definition.field_policy_invalid";
    /// <summary>可能同时执行的自动分支写入了同一表单字段。</summary>
    public const string DefinitionParallelWriteConflict = Prefix + "definition.parallel_write_conflict";
    /// <summary>异步等待依赖缺失、越过实际激活路径或引用未来/自身节点。</summary>
    public const string DefinitionAsyncDependencyInvalid = Prefix + "definition.async_dependency_invalid";
    /// <summary>工作流定义抄送接收人配置无效；调用方应修正抄送接收人列表。</summary>
    public const string DefinitionCcRecipientsInvalid = Prefix + "definition.cc_recipients_invalid";
    /// <summary>工作流定义网关配置无效；调用方应检查网关节点分支与条件。</summary>
    public const string DefinitionGatewayInvalid = Prefix + "definition.gateway_invalid";
    /// <summary>审批待办超时、催办或升级策略无效。</summary>
    public const string DefinitionTimeoutPolicyInvalid = Prefix + "definition.timeout_policy_invalid";
    /// <summary>延时类型、时区、时间点或绑定字段无效。</summary>
    public const string DefinitionDelayPolicyInvalid = Prefix + "definition.delay_policy_invalid";
    /// <summary>办理提交完成规则无效，不能回退为审批规则。</summary>
    public const string DefinitionTaskPolicyInvalid = Prefix + "definition.task_policy_invalid";
    /// <summary>独立投票规则、表单版本或结果字段绑定无效。</summary>
    public const string DefinitionVotePolicyInvalid = Prefix + "definition.vote_policy_invalid";
    /// <summary>领取池的释放规则无效。</summary>
    public const string DefinitionClaimPolicyInvalid = Prefix + "definition.claim_policy_invalid";
    /// <summary>发布版本的发起人撤回边界配置无效。</summary>
    public const string DefinitionWithdrawalPolicyInvalid = Prefix + "definition.withdrawal_policy_invalid";
    /// <summary>当前活动或已发生的业务副作用不允许发起人撤回。</summary>
    public const string InstanceWithdrawalBoundaryInvalid = Prefix + "instance.withdrawal_boundary_invalid";
    /// <summary>审批完成门槛配置无效。</summary>
    public const string DefinitionApprovalPolicyInvalid = Prefix + "definition.approval_policy_invalid";
    /// <summary>办理人解析策略结构、作用域或实体引用无效。</summary>
    public const string DefinitionAssigneePolicyInvalid = Prefix + "definition.assignee_policy_invalid";
    /// <summary>工作流定义拓扑结构不受支持；调用方应调整为支持的拓扑。</summary>
    public const string DefinitionTopologyUnsupported = Prefix + "definition.topology_unsupported";
    /// <summary>工作流定义已停用，禁止启动新实例或发布。</summary>
    public const string DefinitionDisabled = Prefix + "definition.disabled";
    /// <summary>工作流定义已归档，禁止继续变更。</summary>
    public const string DefinitionArchived = Prefix + "definition.archived";
    /// <summary>工作流定义状态键或状态迁移无效。</summary>
    public const string DefinitionStatusInvalid = Prefix + "definition.status_invalid";
    /// <summary>工作流定义版本仍被运行实例引用，禁止删除。</summary>
    public const string VersionInUse = Prefix + "version.in_use";
    /// <summary>工作流定义不存在；调用方应检查定义标识是否正确。</summary>
    public const string DefinitionNotFound = Prefix + "definition.not_found";
    /// <summary>工作流定义键已存在；调用方应使用其他键或更新已有定义。</summary>
    public const string DefinitionKeyExists = Prefix + "definition.key_exists";
    /// <summary>工作流表单字段类型未知；调用方应使用已注册字段类型。</summary>
    public const string FormFieldTypeUnknown = Prefix + "form.field_type_unknown";
    /// <summary>工作流表单字段键重复；调用方应修正重复字段键。</summary>
    public const string FormFieldKeyDuplicate = Prefix + "form.field_key_duplicate";
    /// <summary>工作流表单扩展被禁止；调用方应移除未授权的扩展配置。</summary>
    public const string FormExtensionForbidden = Prefix + "form.extension_forbidden";
    /// <summary>工作流表单金额字段小数位数无效；调用方应按约束设置小数位。</summary>
    public const string FormMoneyScaleInvalid = Prefix + "form.money_scale_invalid";
    /// <summary>工作流表单选择项无效；调用方应提供合法选项集合。</summary>
    public const string FormChoiceOptionsInvalid = Prefix + "form.choice_options_invalid";
    /// <summary>工作流表单字段约束无效；调用方应校验必填、长度等约束。</summary>
    public const string FormFieldConstraintsInvalid = Prefix + "form.field_constraints_invalid";
    /// <summary>工作流表单结构无效；调用方应按 Schema 要求重组表单。</summary>
    public const string FormStructureInvalid = Prefix + "form.structure_invalid";
    /// <summary>工作流表单大小超限；调用方应精简字段或拆分表单。</summary>
    public const string FormSizeLimitExceeded = Prefix + "form.size_limit_exceeded";
    /// <summary>工作流表单 Schema 版本不被支持；调用方应使用兼容 Schema。</summary>
    public const string FormSchemaUnsupported = Prefix + "form.schema_unsupported";
    /// <summary>工作流表单不存在；调用方应检查表单元键是否正确。</summary>
    public const string FormNotFound = Prefix + "form.not_found";
    /// <summary>工作流表单已停用，禁止发布或被新流程绑定。</summary>
    public const string FormDisabled = Prefix + "form.disabled";
    /// <summary>工作流表单已归档，禁止继续变更。</summary>
    public const string FormArchived = Prefix + "form.archived";
    /// <summary>工作流表单状态键或状态迁移无效。</summary>
    public const string FormStatusInvalid = Prefix + "form.status_invalid";
    /// <summary>工作流表单版本仍被运行实例引用，禁止删除。</summary>
    public const string FormVersionInUse = Prefix + "form.version_in_use";
    /// <summary>表单附件字段引用了无效、越权或不符合约束的文件。</summary>
    public const string FormAttachmentInvalid = Prefix + "form.attachment_invalid";
    /// <summary>工作流表单键已存在；调用方应使用其他键或更新已有表单。</summary>
    public const string FormKeyExists = Prefix + "form.key_exists";
    /// <summary>待办办理人与当前操作人不匹配；调用方应确认办理人身份后重试。</summary>
    public const string TodoAssigneeMismatch = Prefix + "todo.assignee_mismatch";
    /// <summary>待办指定办理人不存在；调用方应校验办理人标识。</summary>
    public const string TodoAssigneeNotFound = Prefix + "todo.assignee_not_found";
    /// <summary>待办转办目标与当前办理人相同；调用方应指定不同办理人。</summary>
    public const string TodoAssigneeUnchanged = Prefix + "todo.assignee_unchanged";
    /// <summary>待办当前状态不允许该操作；调用方应先确认待办处于活动状态。</summary>
    public const string TodoNotActive = Prefix + "todo.not_active";
    /// <summary>工作流实例已处于终态；调用方不应再尝试驱动状态迁移。</summary>
    public const string InstanceTerminal = Prefix + "instance.terminal";
    /// <summary>工作流实例版本冲突；调用方应刷新后重试。</summary>
    public const string InstanceVersionConflict = Prefix + "instance.version_conflict";
    /// <summary>输入 Schema 无效；调用方应按契约要求修正输入。</summary>
    public const string SchemaInvalid = Prefix + "schema.invalid";
    /// <summary>工作流定义版本冲突；调用方应刷新版本后重试。</summary>
    public const string VersionConflict = Prefix + "version.conflict";
    /// <summary>工作流定义版本未发布；调用方应先发布版本再启动实例。</summary>
    public const string VersionNotPublished = Prefix + "version.not_published";
    /// <summary>仍存在活动的工作流实例；调用方应处理完实例后再执行变更。</summary>
    public const string ActiveInstanceExists = Prefix + "instance.active_exists";
    /// <summary>无权操作该工作流实例；调用方应申请相应权限。</summary>
    public const string InstanceForbidden = Prefix + "instance.forbidden";
    /// <summary>当前可信作用域内不存在该实例或活动。</summary>
    public const string InstanceNotFound = Prefix + "instance.not_found";
    /// <summary>无权操作该待办；调用方应申请相应权限。</summary>
    public const string TodoForbidden = Prefix + "todo.forbidden";
    /// <summary>指定步骤不是当前实例有效执行链上的已完成人工审批节点。</summary>
    public const string TodoReturnTargetInvalid = Prefix + "todo.return_target_invalid";
    /// <summary>抄送记录不存在；调用方应检查抄送标识。</summary>
    public const string CcNotFound = Prefix + "cc.not_found";
    /// <summary>修订版本冲突；调用方应刷新后重试。</summary>
    public const string RevisionConflict = Prefix + "revision.conflict";
    /// <summary>非法的状态迁移；调用方应按状态机规则操作。</summary>
    public const string InvalidTransition = Prefix + "transition.invalid";
    /// <summary>当前作用域内找不到恢复任务。</summary>
    public const string RecoveryNotFound = Prefix + "recovery.not_found";
    /// <summary>任务状态不允许人工重试。</summary>
    public const string RecoveryRetryInvalid = Prefix + "recovery.retry_invalid";
    /// <summary>源条件仍在，对账不能关闭任务。</summary>
    public const string RecoveryReconcileInvalid = Prefix + "recovery.reconcile_invalid";
    /// <summary>加签办理人无效、重复或包含当前办理人。</summary>
    public const string TodoCountersignAssigneeInvalid = Prefix + "todo.countersign_assignee_invalid";
    /// <summary>加签方向键不受支持。</summary>
    public const string TodoCountersignDirectionInvalid = Prefix + "todo.countersign_direction_invalid";
    /// <summary>当前待办已存在活动加签链。</summary>
    public const string TodoCountersignChainActive = Prefix + "todo.countersign_chain_active";
    /// <summary>找不到可取消的活动加签链。</summary>
    public const string TodoCountersignChainNotFound = Prefix + "todo.countersign_chain_not_found";

    /// <summary>节点动作、签名或意见配置无效。</summary>
    public const string DefinitionActionPolicyInvalid = Prefix + "definition.action_policy_invalid";
    /// <summary>发布节点禁止该办理动作。</summary>
    public const string TodoActionForbidden = Prefix + "todo.action_forbidden";
    /// <summary>发布节点要求填写办理意见。</summary>
    public const string TodoCommentRequired = Prefix + "todo.comment_required";
    /// <summary>发布节点要求绑定签名文件证据。</summary>
    public const string TodoSignatureRequired = Prefix + "todo.signature_required";
    /// <summary>签名文件不可用、越权或内容不符合要求。</summary>
    public const string TodoSignatureInvalid = Prefix + "todo.signature_invalid";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        DefinitionNodeTypeUnknown,
        DefinitionNodeTypeUnavailable,
        DefinitionNodeKeyDuplicate,
        DefinitionReferenceDangling,
        DefinitionNodeUnreachable,
        DefinitionEndMissing,
        DefinitionBackEdgeIllegal,
        DefinitionStartInvalid,
        DefinitionSchemaUnsupported,
        DefinitionFieldPolicyInvalid,
        DefinitionCcRecipientsInvalid,
        DefinitionGatewayInvalid,
        DefinitionTimeoutPolicyInvalid,
        DefinitionApprovalPolicyInvalid,
        DefinitionTaskPolicyInvalid,
        DefinitionVotePolicyInvalid,
        DefinitionClaimPolicyInvalid,
        DefinitionWithdrawalPolicyInvalid,
        InstanceWithdrawalBoundaryInvalid,
        DefinitionAssigneePolicyInvalid,
        DefinitionTopologyUnsupported,
        DefinitionDisabled,
        DefinitionArchived,
        DefinitionStatusInvalid,
        VersionInUse,
        DefinitionNotFound,
        DefinitionKeyExists,
        FormFieldTypeUnknown,
        FormFieldKeyDuplicate,
        FormExtensionForbidden,
        FormMoneyScaleInvalid,
        FormChoiceOptionsInvalid,
        FormFieldConstraintsInvalid,
        FormStructureInvalid,
        FormSizeLimitExceeded,
        FormSchemaUnsupported,
        FormNotFound,
        FormDisabled,
        FormArchived,
        FormStatusInvalid,
        FormVersionInUse,
        FormAttachmentInvalid,
        FormKeyExists,
        TodoAssigneeMismatch,
        TodoAssigneeNotFound,
        TodoAssigneeUnchanged,
        TodoNotActive,
        InstanceTerminal,
        InstanceVersionConflict,
        SchemaInvalid,
        VersionConflict,
        VersionNotPublished,
        ActiveInstanceExists,
        InstanceForbidden,
        TodoForbidden,
        TodoReturnTargetInvalid,
        CcNotFound,
        RevisionConflict,
        InvalidTransition,
        RecoveryNotFound,
        RecoveryRetryInvalid,
        RecoveryReconcileInvalid,
        TodoCountersignAssigneeInvalid,
        TodoCountersignDirectionInvalid,
        TodoCountersignChainActive,
        TodoCountersignChainNotFound,
        BusinessOwnerRequired,
        DefinitionBusinessCommandInvalid,
        DefinitionSubprocessInvalid,
        SubprocessActivationInvalid,
        DefinitionActionPolicyInvalid,
        TodoActionForbidden,
        TodoCommentRequired,
        TodoSignatureRequired,
        TodoSignatureInvalid,
    ]);
}
