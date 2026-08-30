using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

internal static class WorkflowFormCompiler
{
    private const int MaxSections = 32;
    private const int MaxFieldsPerSection = 64;
    private const int MaxTotalFields = 256;
    private const int MaxStableKeyLength = 64;

    private static readonly HashSet<string> ForbiddenStableKeys =
        new(StringComparer.OrdinalIgnoreCase) { "__proto__", "prototype", "constructor" };

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

    private static readonly IReadOnlyDictionary<string, HashSet<string>> SupportedConstraintKeys =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["text"] = new(StringComparer.Ordinal) { "minLength", "maxLength" },
            ["textarea"] = new(StringComparer.Ordinal) { "minLength", "maxLength" },
            ["integer"] = new(StringComparer.Ordinal) { "minimum", "maximum" },
            ["decimal"] = new(StringComparer.Ordinal) { "scale", "minimum", "maximum" },
            ["money"] = new(StringComparer.Ordinal) { "scale", "minimum", "maximum" },
            ["date"] = new(StringComparer.Ordinal) { "minimum", "maximum" },
            ["time"] = new(StringComparer.Ordinal) { "minimum", "maximum" },
            ["datetime"] = new(StringComparer.Ordinal) { "minimum", "maximum" },
            ["radio"] = new(StringComparer.Ordinal) { "options" },
            ["checkbox"] = new(StringComparer.Ordinal) { "options" },
            ["select"] = new(StringComparer.Ordinal) { "options" },
            ["switch"] = new(StringComparer.Ordinal),
        };

    public static WorkflowCompilationResult Compile(WorkflowFormSchema schema)
    {
        if (schema.SchemaVersion != 1 || schema.AdapterVersion != 1)
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormSchemaUnsupported);
        }

        var structureError = ValidateStructure(schema);
        if (structureError is not null)
        {
            return WorkflowCompilationResult.Failure(structureError);
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

        if (fields.Any(field => field.Constraints.Keys.Any(
                key => !SupportedConstraintKeys[field.FieldTypeKey].Contains(key))))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormFieldConstraintsInvalid);
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
                .Any(field => !WorkflowFormFieldConstraints.TryReadIntegerRange(field, out _, out _)) ||
            fields.Where(field => field.FieldTypeKey is "decimal" or "money")
                .Any(field => !WorkflowFormFieldConstraints.TryReadDecimalConstraints(
                    field,
                    field.FieldTypeKey == "money" ? 4 : 28,
                    out _,
                    out _,
                    out _)) ||
            fields.Where(field => field.FieldTypeKey is "date" or "time" or "datetime")
                .Any(field => !WorkflowFormFieldConstraints.TryReadTemporalRange(
                    field,
                    out _,
                    out _)))
        {
            return WorkflowCompilationResult.Failure(WorkflowErrorCodes.FormFieldConstraintsInvalid);
        }

        return WorkflowCompilationResult.Success(
            WorkflowJsonCanonicalizer.Compile(writer => WriteCanonical(writer, schema)));
    }

    private static string? ValidateStructure(WorkflowFormSchema schema)
    {
        if (schema.Sections is not { Count: > 0 })
        {
            return WorkflowErrorCodes.FormStructureInvalid;
        }

        if (schema.Sections.Count > MaxSections)
        {
            return WorkflowErrorCodes.FormSizeLimitExceeded;
        }

        var sectionKeys = new HashSet<string>(StringComparer.Ordinal);
        var totalFields = 0;
        foreach (var section in schema.Sections)
        {
            if (section is null || !IsStableKey(section.SectionKey) || !sectionKeys.Add(section.SectionKey) ||
                section.Fields is not { Count: > 0 })
            {
                return WorkflowErrorCodes.FormStructureInvalid;
            }

            // 该上限覆盖现有百字段基准，并限制发布编译、客户端渲染与提交校验的最坏成本。
            if (section.Fields.Count > MaxFieldsPerSection ||
                totalFields > MaxTotalFields - section.Fields.Count)
            {
                return WorkflowErrorCodes.FormSizeLimitExceeded;
            }

            totalFields += section.Fields.Count;
            if (section.Fields.Any(field =>
                    field is null || !IsStableKey(field.FieldKey) || field.Constraints is null))
            {
                return WorkflowErrorCodes.FormStructureInvalid;
            }
        }

        return null;
    }

    private static bool IsStableKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxStableKeyLength || ForbiddenStableKeys.Contains(value) ||
            !IsAsciiLetter(value[0]))
        {
            return false;
        }

        foreach (var character in value.AsSpan(1))
        {
            if (!IsAsciiLetter(character) && !char.IsAsciiDigit(character) && character is not ('_' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

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
        return !WorkflowFormFieldConstraints.TryReadDecimalScale(field, 4, out _);
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
