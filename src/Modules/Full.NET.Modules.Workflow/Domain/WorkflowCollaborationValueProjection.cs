using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>字段值保持闭合Schema；附件只有在本地准备阶段或已验证能力后才允许进入投影。</summary>
internal static class WorkflowCollaborationValueProjection
{
    internal static bool Supports(WorkflowFormField field, bool attachments) => field.FieldTypeKey is
        "text" or "textarea" or "integer" or "decimal" or "money" or "switch" or "date" or "time" or "datetime" or "radio" or "select" or "checkbox" ||
        field.FieldTypeKey == "subtable" && WorkflowFormSubtableConstraints.TryRead(field, out _, out _) ||
        attachments && field.FieldTypeKey == "attachment" && WorkflowFormAttachmentConstraints.TryRead(field, out _, out _, out _);
    internal static bool Valid(WorkflowFormField field, JsonElement value, bool attachments) => Supports(field, attachments) &&
        (value.ValueKind == JsonValueKind.Null ? !field.Required : WorkflowFormValueValidator.IsFieldValueValid(field, value));
}
