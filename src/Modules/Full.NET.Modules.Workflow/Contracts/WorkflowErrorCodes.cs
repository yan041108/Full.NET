namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>
/// 定义工作流编译与运行时使用的稳定机器错误码。
/// </summary>
public static class WorkflowErrorCodes
{
    public const string Prefix = "workflow.";
    public const string DefinitionNodeTypeUnknown = Prefix + "definition.node_type_unknown";
    public const string DefinitionNodeTypeUnavailable = Prefix + "definition.node_type_unavailable";
    public const string DefinitionNodeKeyDuplicate = Prefix + "definition.node_key_duplicate";
    public const string DefinitionReferenceDangling = Prefix + "definition.reference_dangling";
    public const string DefinitionNodeUnreachable = Prefix + "definition.node_unreachable";
    public const string DefinitionEndMissing = Prefix + "definition.end_missing";
    public const string DefinitionBackEdgeIllegal = Prefix + "definition.back_edge_illegal";
    public const string DefinitionStartInvalid = Prefix + "definition.start_invalid";
    public const string DefinitionSchemaUnsupported = Prefix + "definition.schema_unsupported";
    public const string DefinitionFieldPolicyInvalid = Prefix + "definition.field_policy_invalid";
    public const string DefinitionCcRecipientsInvalid = Prefix + "definition.cc_recipients_invalid";
    public const string DefinitionGatewayInvalid = Prefix + "definition.gateway_invalid";
    /// <summary>审批待办超时、催办或升级策略无效。</summary>
    public const string DefinitionTimeoutPolicyInvalid = Prefix + "definition.timeout_policy_invalid";
    public const string DefinitionTopologyUnsupported = Prefix + "definition.topology_unsupported";
    public const string DefinitionNotFound = Prefix + "definition.not_found";
    public const string DefinitionKeyExists = Prefix + "definition.key_exists";
    public const string FormFieldTypeUnknown = Prefix + "form.field_type_unknown";
    public const string FormFieldKeyDuplicate = Prefix + "form.field_key_duplicate";
    public const string FormExtensionForbidden = Prefix + "form.extension_forbidden";
    public const string FormMoneyScaleInvalid = Prefix + "form.money_scale_invalid";
    public const string FormChoiceOptionsInvalid = Prefix + "form.choice_options_invalid";
    public const string FormFieldConstraintsInvalid = Prefix + "form.field_constraints_invalid";
    public const string FormStructureInvalid = Prefix + "form.structure_invalid";
    public const string FormSizeLimitExceeded = Prefix + "form.size_limit_exceeded";
    public const string FormSchemaUnsupported = Prefix + "form.schema_unsupported";
    public const string FormNotFound = Prefix + "form.not_found";
    public const string FormKeyExists = Prefix + "form.key_exists";
    public const string TodoAssigneeMismatch = Prefix + "todo.assignee_mismatch";
    public const string TodoAssigneeNotFound = Prefix + "todo.assignee_not_found";
    public const string TodoAssigneeUnchanged = Prefix + "todo.assignee_unchanged";
    public const string TodoNotActive = Prefix + "todo.not_active";
    public const string InstanceTerminal = Prefix + "instance.terminal";
    public const string InstanceVersionConflict = Prefix + "instance.version_conflict";
    public const string SchemaInvalid = Prefix + "schema.invalid";
    public const string VersionConflict = Prefix + "version.conflict";
    public const string VersionNotPublished = Prefix + "version.not_published";
    public const string ActiveInstanceExists = Prefix + "instance.active_exists";
    public const string InstanceForbidden = Prefix + "instance.forbidden";
    public const string TodoForbidden = Prefix + "todo.forbidden";
    /// <summary>指定步骤不是当前实例有效执行链上的已完成人工审批节点。</summary>
    public const string TodoReturnTargetInvalid = Prefix + "todo.return_target_invalid";
    public const string CcNotFound = Prefix + "cc.not_found";
    public const string RevisionConflict = Prefix + "revision.conflict";
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
        DefinitionTopologyUnsupported,
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
    ]);
}
