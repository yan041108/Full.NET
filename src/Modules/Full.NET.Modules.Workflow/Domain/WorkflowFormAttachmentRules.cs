using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>解析附件字段声明式约束，供发布编译与运行时 claim 校验共用。</summary>
internal static class WorkflowFormAttachmentConstraints
{
    public const int MinimumMaxCount = 1;
    public const int MaximumMaxCount = 10;
    public const long MinimumMaxSizeBytes = 1;
    public const long MaximumMaxSizeBytes = 52_428_800;

    public static bool TryRead(
        WorkflowFormField field,
        out int maxCount,
        out long maxSizeBytes,
        out IReadOnlyList<string> allowedExtensions)
    {
        maxCount = 0;
        maxSizeBytes = 0;
        allowedExtensions = [];

        if (field.FieldTypeKey != "attachment" ||
            !field.Constraints.TryGetValue("maxCount", out var maxCountElement) ||
            maxCountElement.ValueKind != JsonValueKind.Number ||
            !maxCountElement.TryGetInt32(out maxCount) ||
            maxCount < MinimumMaxCount ||
            maxCount > MaximumMaxCount ||
            !field.Constraints.TryGetValue("maxSizeBytes", out var maxSizeElement) ||
            maxSizeElement.ValueKind != JsonValueKind.Number ||
            !maxSizeElement.TryGetInt64(out maxSizeBytes) ||
            maxSizeBytes < MinimumMaxSizeBytes ||
            maxSizeBytes > MaximumMaxSizeBytes ||
            !field.Constraints.TryGetValue("allowedExtensions", out var extensionsElement) ||
            extensionsElement.ValueKind != JsonValueKind.Array ||
            extensionsElement.GetArrayLength() == 0)
        {
            return false;
        }

        var extensions = new List<string>();
        foreach (var item in extensionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var extension = item.GetString();
            if (string.IsNullOrWhiteSpace(extension) ||
                extension.Length > 16 ||
                !IsAsciiExtension(extension))
            {
                return false;
            }

            extensions.Add(extension.ToLowerInvariant());
        }

        if (extensions.Distinct(StringComparer.Ordinal).Count() != extensions.Count)
        {
            return false;
        }

        allowedExtensions = extensions;
        return true;
    }

    private static bool IsAsciiExtension(string value)
    {
        foreach (var character in value)
        {
            if (character is < 'a' or > 'z' && character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>从表单值中提取并校验附件字段的文件标识集合。</summary>
internal static class WorkflowFormAttachmentValueRules
{
    public static bool TryReadFileIds(JsonElement value, out Guid[] fileIds)
    {
        fileIds = [];
        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parsed = new List<Guid>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String ||
                !Guid.TryParseExact(item.GetString(), "D", out var fileId) ||
                fileId == Guid.Empty)
            {
                return false;
            }

            parsed.Add(fileId);
        }

        if (parsed.Distinct().Count() != parsed.Count)
        {
            return false;
        }

        fileIds = parsed.ToArray();
        return true;
    }

    public static Dictionary<string, Guid[]> ExtractAttachmentFields(
        WorkflowFormSchema schema,
        IReadOnlyDictionary<string, JsonElement> values)
    {
        var result = new Dictionary<string, Guid[]>(StringComparer.Ordinal);
        foreach (var field in schema.Sections.SelectMany(section => section.Fields))
        {
            if (field.FieldTypeKey != "attachment")
            {
                continue;
            }

            if (!values.TryGetValue(field.FieldKey, out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                result[field.FieldKey] = [];
                continue;
            }

            if (!TryReadFileIds(value, out var fileIds))
            {
                result[field.FieldKey] = [];
                continue;
            }

            result[field.FieldKey] = fileIds;
        }

        return result;
    }

    public static bool MatchesAllowedExtension(string originalFileName, IReadOnlyList<string> allowedExtensions)
    {
        var extension = Path.GetExtension(originalFileName);
        if (extension.Length <= 1)
        {
            return false;
        }

        return allowedExtensions.Contains(extension[1..].ToLowerInvariant(), StringComparer.Ordinal);
    }
}
