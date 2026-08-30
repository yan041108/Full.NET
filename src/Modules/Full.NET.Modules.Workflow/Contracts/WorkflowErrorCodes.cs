namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>
/// 定义工作流编译与运行时使用的稳定机器错误码。
/// </summary>
public static class WorkflowErrorCodes
{
    public const string Prefix = "workflow.";
    public const string DefinitionNodeTypeUnknown = Prefix + "definition.node_type_unknown";
    public const string DefinitionNodeKeyDuplicate = Prefix + "definition.node_key_duplicate";
    public const string DefinitionReferenceDangling = Prefix + "definition.reference_dangling";
    public const string DefinitionNodeUnreachable = Prefix + "definition.node_unreachable";
    public const string DefinitionEndMissing = Prefix + "definition.end_missing";
    public const string DefinitionBackEdgeIllegal = Prefix + "definition.back_edge_illegal";
    public const string DefinitionStartInvalid = Prefix + "definition.start_invalid";
    public const string DefinitionSchemaUnsupported = Prefix + "definition.schema_unsupported";
    public const string DefinitionFieldPolicyInvalid = Prefix + "definition.field_policy_invalid";
    public const string DefinitionNotFound = Prefix + "definition.not_found";
    public const string DefinitionKeyExists = Prefix + "definition.key_exists";
    public const string FormFieldTypeUnknown = Prefix + "form.field_type_unknown";
    public const string FormFieldKeyDuplicate = Prefix + "form.field_key_duplicate";
    public const string FormExtensionForbidden = Prefix + "form.extension_forbidden";
    public const string FormMoneyScaleInvalid = Prefix + "form.money_scale_invalid";
    public const string FormChoiceOptionsInvalid = Prefix + "form.choice_options_invalid";
    public const string FormFieldConstraintsInvalid = Prefix + "form.field_constraints_invalid";
    public const string FormSchemaUnsupported = Prefix + "form.schema_unsupported";
    public const string FormNotFound = Prefix + "form.not_found";
    public const string FormKeyExists = Prefix + "form.key_exists";
    public const string TodoAssigneeMismatch = Prefix + "todo.assignee_mismatch";
    public const string TodoNotActive = Prefix + "todo.not_active";
    public const string InstanceTerminal = Prefix + "instance.terminal";
    public const string InstanceVersionConflict = Prefix + "instance.version_conflict";
    public const string SchemaInvalid = Prefix + "schema.invalid";
    public const string VersionConflict = Prefix + "version.conflict";
    public const string VersionNotPublished = Prefix + "version.not_published";
    public const string ActiveInstanceExists = Prefix + "instance.active_exists";
    public const string InstanceForbidden = Prefix + "instance.forbidden";
    public const string TodoForbidden = Prefix + "todo.forbidden";
    public const string RevisionConflict = Prefix + "revision.conflict";
    public const string InvalidTransition = Prefix + "transition.invalid";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        DefinitionNodeTypeUnknown,
        DefinitionNodeKeyDuplicate,
        DefinitionReferenceDangling,
        DefinitionNodeUnreachable,
        DefinitionEndMissing,
        DefinitionBackEdgeIllegal,
        DefinitionStartInvalid,
        DefinitionSchemaUnsupported,
        DefinitionFieldPolicyInvalid,
        DefinitionNotFound,
        DefinitionKeyExists,
        FormFieldTypeUnknown,
        FormFieldKeyDuplicate,
        FormExtensionForbidden,
        FormMoneyScaleInvalid,
        FormChoiceOptionsInvalid,
        FormFieldConstraintsInvalid,
        FormSchemaUnsupported,
        FormNotFound,
        FormKeyExists,
        TodoAssigneeMismatch,
        TodoNotActive,
        InstanceTerminal,
        InstanceVersionConflict,
        SchemaInvalid,
        VersionConflict,
        VersionNotPublished,
        ActiveInstanceExists,
        InstanceForbidden,
        TodoForbidden,
        RevisionConflict,
        InvalidTransition,
    ]);
}
