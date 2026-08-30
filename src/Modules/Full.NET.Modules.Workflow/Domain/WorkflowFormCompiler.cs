using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

internal static class WorkflowFormCompiler
{
    private static readonly HashSet<string> SupportedFieldTypes =
        new(StringComparer.Ordinal)
        {
            "text", "textarea", "integer", "decimal", "money", "date",
            "time", "datetime", "radio", "checkbox", "select", "switch",
        };

    private static readonly HashSet<string> ForbiddenExtensionKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "script", "scripts", "function", "functions", "css", "cssCode",
            "html", "iframe", "remoteUrl", "headers", "body", "lifecycle",
            "events", "onCreated", "onMounted", "onBeforeMount", "onUpdated",
        };

    private static readonly HashSet<string> ChoiceFieldTypes =
        new(StringComparer.Ordinal) { "radio", "checkbox", "select" };

    public static WorkflowCompilationResult Compile(WorkflowFormSchema schema)
    {
        if (schema.SchemaVersion != 1 || schema.AdapterVersion != 1)
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormSchemaUnsupported);
        }

        var fields = schema.Sections.SelectMany(section => section.Fields).ToArray();
        if (fields.Any(field => !SupportedFieldTypes.Contains(field.FieldTypeKey)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormFieldTypeUnknown);
        }

        if (fields.GroupBy(field => field.FieldKey, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormFieldKeyDuplicate);
        }

        if (fields.Any(field => field.Constraints.Any(pair => ContainsForbiddenExtension(pair.Key, pair.Value))))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormExtensionForbidden);
        }

        if (fields.Where(field => field.FieldTypeKey == "money").Any(HasInvalidMoneyScale))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormMoneyScaleInvalid);
        }

        if (fields.Where(field => ChoiceFieldTypes.Contains(field.FieldTypeKey))
            .Any(field => !WorkflowFormChoiceOptions.TryRead(field, out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormChoiceOptionsInvalid);
        }

        if (fields.Where(field => field.FieldTypeKey is "text" or "textarea")
                .Any(field => !WorkflowFormFieldConstraints.TryReadTextLength(field, out _, out _)) ||
            fields.Where(field => field.FieldTypeKey == "integer")
                .Any(field => !WorkflowFormFieldConstraints.TryReadIntegerRange(field, out _, out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormFieldConstraintsInvalid);
        }

        return WorkflowCompilationResult.Success(
            WorkflowJsonCanonicalizer.Compile(writer => WriteCanonical(writer, schema)));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, WorkflowFormSchema schema)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", schema.SchemaVersion);
        writer.WriteNumber("adapterVersion", schema.AdapterVersion);
        writer.WritePropertyName("sections");
        writer.WriteStartArray();
        foreach (var section in schema.Sections)
        {
            writer.WriteStartObject();
            writer.WriteString("sectionKey", section.SectionKey);
            writer.WritePropertyName("fields");
            writer.WriteStartArray();
            foreach (var field in section.Fields)
            {
                writer.WriteStartObject();
                writer.WriteString("fieldKey", field.FieldKey);
                writer.WriteString("fieldTypeKey", field.FieldTypeKey);
                writer.WriteBoolean("required", field.Required);
                writer.WritePropertyName("constraints");
                writer.WriteStartObject();
                foreach (var constraint in field.Constraints.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(constraint.Key);
                    WorkflowJsonCanonicalizer.WriteElement(writer, constraint.Value);
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static bool HasInvalidMoneyScale(WorkflowFormField field)
    {
        if (!field.Constraints.TryGetValue("scale", out var scale) ||
            scale.ValueKind != JsonValueKind.Number ||
            !scale.TryGetInt32(out var value))
        {
            return true;
        }

        return value is < 0 or > 4;
    }

    private static bool ContainsForbiddenExtension(string key, JsonElement value)
    {
        if (ForbiddenExtensionKeys.Contains(key) || key.StartsWith("on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject()
                .Any(property => ContainsForbiddenExtension(property.Name, property.Value)),
            JsonValueKind.Array => value.EnumerateArray()
                .Any(item => ContainsForbiddenExtension(string.Empty, item)),
            _ => false,
        };
    }
}
