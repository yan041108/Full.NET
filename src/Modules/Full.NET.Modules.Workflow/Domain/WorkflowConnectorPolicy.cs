using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>连接器节点只引用固定目录版本和顶层字段映射，不携带网络目标、凭据或可执行表达式。</summary>
internal sealed record WorkflowConnectorPolicy(string ConnectorKey, int ConnectorVersion,
    IReadOnlyDictionary<string, string> InputFields, IReadOnlyDictionary<string, string> OutputFields,
    IReadOnlyDictionary<string, string> FieldPolicies, WorkflowFormSchema? Schema)
{
    internal const int MaximumMappings = 16;
    internal const int MaximumResponseBytes = 65536;
    public bool CanRead(string fieldKey) => FieldPolicies.TryGetValue(fieldKey, out var access) && access != "hidden";

    public static bool TryRead(JsonElement config, WorkflowFormSchema? schema, out WorkflowConnectorPolicy? policy)
    {
        policy = null;
        if (!UniqueObject(config) || !config.TryGetProperty("connectorPolicy", out var connector) || !UniqueObject(connector) ||
            connector.EnumerateObject().Any(item => item.Name is not ("connectorKey" or "connectorVersion" or "inputFields" or "outputFields")) ||
            !ReadKey(connector, "connectorKey", out var key) || !connector.TryGetProperty("connectorVersion", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number < 1 ||
            !ReadMap(connector, "inputFields", out var inputs) || !ReadMap(connector, "outputFields", out var outputs) ||
            !config.TryGetProperty("fieldPolicies", out var permissions) || !UniqueObject(permissions)) return false;
        var access = new Dictionary<string, string>(StringComparer.Ordinal);
        var fields = schema?.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        foreach (var field in permissions.EnumerateObject())
        {
            if (!ValidKey(field.Name) || fields is not null && !fields.ContainsKey(field.Name) || field.Value.ValueKind != JsonValueKind.String ||
                field.Value.GetString() is not ("hidden" or "readOnly" or "editable" or "required")) return false;
            access.Add(field.Name, field.Value.GetString()!);
        }
        if (inputs!.Values.Any(field => !access.TryGetValue(field, out var mode) || mode == "hidden") ||
            outputs!.Keys.Any(field => !access.TryGetValue(field, out var mode) || mode is not ("editable" or "required"))) return false;
        policy = new(key!, number, inputs, outputs, access, schema);
        return true;
    }

    /// <summary>只发送已授权的固定输入字段；缺少映射源不能用最新表单或默认值偷偷替代。</summary>
    public bool TryCreateInput(IReadOnlyDictionary<string, JsonElement> values, out string? json)
    {
        json = null;
        if (Schema is null || !WorkflowFormValueValidator.Validate(Schema, values) || InputFields.Values.Any(field => !values.ContainsKey(field))) return false;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var mapping in InputFields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            { writer.WritePropertyName(mapping.Key); values[mapping.Value].WriteTo(writer); }
            writer.WriteEndObject();
        }
        if (buffer.WrittenCount > 131072) return false;
        json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        return true;
    }

    /// <summary>结果仅按固定顶层属性回填，复用本地字段规则的类型、必填、大小及节点权限检查。</summary>
    public bool TryMapOutput(IReadOnlyDictionary<string, JsonElement> input, string responseJson,
        out WorkflowFieldMutationOutput? output)
    {
        output = null;
        if (Schema is null || Encoding.UTF8.GetByteCount(responseJson) > MaximumResponseBytes || !WorkflowFormValueValidator.Validate(Schema, input)) return false;
        try
        {
            using var response = JsonDocument.Parse(responseJson, new JsonDocumentOptions { MaxDepth = 32 });
            if (!UniqueObject(response.RootElement)) return false;
            if (OutputFields.Count == 0)
            {
                output = new(input.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal), new Dictionary<string, JsonElement>(), false);
                return true;
            }
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject(); writer.WritePropertyName("fieldPolicies"); writer.WriteStartObject();
                foreach (var access in FieldPolicies.OrderBy(pair => pair.Key, StringComparer.Ordinal)) writer.WriteString(access.Key, access.Value);
                writer.WriteEndObject(); writer.WritePropertyName("mutationPolicy"); writer.WriteStartObject();
                writer.WritePropertyName("assignments"); writer.WriteStartArray();
                foreach (var mapping in OutputFields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!response.RootElement.TryGetProperty(mapping.Value, out var value)) return false;
                    writer.WriteStartObject(); writer.WriteString("fieldKey", mapping.Key); writer.WritePropertyName("value"); value.WriteTo(writer); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteEndObject();
            }
            using var config = JsonDocument.Parse(buffer.WrittenMemory);
            return WorkflowFieldMutationPolicy.TryRead("data.modify", config.RootElement, Schema, out var mutation) && mutation!.TryApply(input, out output);
        }
        catch (JsonException) { return false; }
    }

    private static bool ReadMap(JsonElement value, string property, out IReadOnlyDictionary<string, string>? result)
    {
        result = null;
        if (!value.TryGetProperty(property, out var map) || !UniqueObject(map) || map.EnumerateObject().Count() > MaximumMappings) return false;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in map.EnumerateObject())
        {
            if (!ValidKey(item.Name) || item.Value.ValueKind != JsonValueKind.String || !ValidKey(item.Value.GetString())) return false;
            fields.Add(item.Name, item.Value.GetString()!);
        }
        result = fields;
        return true;
    }
    private static bool ReadKey(JsonElement value, string property, out string? key)
    {
        key = null;
        if (!value.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.String || !ValidKey(item.GetString())) return false;
        key = item.GetString(); return true;
    }
    private static bool ValidKey(string? key) => key?.Length <= 64 && WorkflowNodeKeyValidator.IsValid(key) && key is not ("__proto__" or "prototype" or "constructor");
    private static bool UniqueObject(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count();
}
