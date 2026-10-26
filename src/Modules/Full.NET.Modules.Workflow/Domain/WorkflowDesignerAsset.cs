using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>设计资产与业务 Schema 分开存储；只验证有界 JSON，不执行或裁剪原版配置。</summary>
internal static class WorkflowDesignerAsset
{
    public const string SourceCommit = "c67479e496bab56a93a3dff168a4f529d8293c67";
    public const int FormatVersion = 3;
    public const int MaximumBytes = 2_000_000;

    public static bool IsValid(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(value.GetRawText()) > MaximumBytes ||
            !value.TryGetProperty("widgetList", out var widgets) || widgets.ValueKind != JsonValueKind.Array ||
            !value.TryGetProperty("formConfig", out var config) || config.ValueKind != JsonValueKind.Object ||
            (config.TryGetProperty("jsonVersion", out var version) && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != FormatVersion))) return false;
        var count = 0;
        if (!Validate(value, 0, ref count) || !ValidateWidgetTree(widgets)) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (widget, _) in Widgets(widgets, "widgetList"))
        {
            if (!widget.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !widget.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Object ||
                widget.TryGetProperty("category", out var category) && category.ValueKind != JsonValueKind.String ||
                widget.TryGetProperty("formItemFlag", out var flag) && flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            if (widget.TryGetProperty("id", out var id) && (id.ValueKind != JsonValueKind.String || !ids.Add(id.GetString()!))) return false;
        }
        return true;
    }

    /// <summary>组件列表不允许无类型杂项；布局表格的行是唯一不带 type 的结构节点。</summary>
    private static bool ValidateWidgetTree(JsonElement list, bool tableRows = false)
    {
        if (list.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return false;
            if (tableRows)
            {
                if (!item.TryGetProperty("cols", out var columns) || !ValidateWidgetTree(columns)) return false;
                continue;
            }
            if (!item.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(type.GetString())) return false;
            foreach (var property in item.EnumerateObject())
                if (property.Name is "widgetList" or "cols" or "tabs" or "rows" && !ValidateWidgetTree(property.Value, property.Name == "rows")) return false;
        }
        return true;
    }

    private static bool Validate(JsonElement value, int depth, ref int count)
    {
        if (depth > 48 || ++count > 50000) return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (!names.Add(property.Name) || property.Name is "__proto__" or "prototype" or "constructor" || !Validate(property.Value, depth + 1, ref count)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) if (!Validate(item, depth + 1, ref count)) return false;
        return true;
    }

    /// <summary>只生成业务校验投影；不反向重建或修改原设计。错误包含组件路径，不包含配置值。</summary>
    public static WorkflowFormSchema? Project(JsonElement design, out string? diagnostic)
    {
        diagnostic = null;
        if (!IsValid(design)) { diagnostic = "designerJson"; return null; }
        var fields = new List<WorkflowFormField>();
        foreach (var (widget, path) in Widgets(design.GetProperty("widgetList"), "widgetList"))
        {
            if (!widget.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String) { diagnostic = path + ".type"; return null; }
            var type = typeElement.GetString();
            if (widget.TryGetProperty("category", out var category) && category.ValueKind == JsonValueKind.String && category.GetString() == "container")
            {
                if (type is not ("grid" or "grid-col" or "table" or "table-cell" or "tab" or "tab-pane" or "card")) { diagnostic = path + ".type"; return null; }
                continue;
            }
            if (type is "static-text" or "html-text" or "button" or "divider" or "alert") continue;
            if (!widget.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Object) { diagnostic = path + ".options"; return null; }
            if (string.IsNullOrWhiteSpace(Read(widget, "id"))) { diagnostic = path + ".id"; return null; }
            // 原版校验和远程选项尚无服务端等价编译器，不能忽略其语义后成功发布。
            foreach (var key in new[] { "validation", "customRule", "onValidate" })
                if (options.TryGetProperty(key, out var rule) && rule.ValueKind != JsonValueKind.Null && rule.GetRawText() is not ("\"\"" or "[]" or "{}")) { diagnostic = path + ".options." + key; return null; }
            if (options.TryGetProperty("dsEnabled", out var dynamicOptions) && dynamicOptions.ValueKind == JsonValueKind.True) { diagnostic = path + ".options.dsEnabled"; return null; }
            var fieldType = type switch { "input" => "text", "textarea" => "textarea", "number" => "decimal", "date" => Read(options, "type") == "datetime" ? "datetime" : "date", "time" => "time", "radio" => "radio", "checkbox" => "checkbox", "select" => "select", "switch" => "switch", _ => null };
            var declared = Read(options, "fullNetFieldType");
            if (declared is not null && (type == "number" && declared is "integer" or "decimal" or "money" || type == "input" && declared == "text")) fieldType = declared;
            if (fieldType is null || (type == "select" && options.TryGetProperty("multiple", out var multiple) && multiple.ValueKind == JsonValueKind.True)) { diagnostic = path + ".type"; return null; }
            // 上游 Number 控件不承诺十进制定点精度，不能把金额作为浮点数悄然发布。
            if (fieldType is "money" or "decimal") { diagnostic = path + ".options.fullNetFieldType: exact-decimal-binding-required"; return null; }
            if (fieldType == "datetime" || fieldType == "date" && (Read(options, "type") is not (null or "date") || Read(options, "valueFormat") is not (null or "YYYY-MM-DD")) ||
                fieldType == "time" && Read(options, "format") is not (null or "HH:mm:ss")) { diagnostic = path + ".options.valueFormat: temporal-binding-required"; return null; }
            var constraints = new Dictionary<string, JsonElement>();
            void Copy(string from, string to) { if (options.TryGetProperty(from, out var value) && value.ValueKind != JsonValueKind.Null) constraints[to] = value.Clone(); }
            if (fieldType is "text" or "textarea") { Copy("minLength", "minLength"); Copy("maxLength", "maxLength"); }
            if (fieldType == "integer")
            {
                if (!options.TryGetProperty("precision", out var precision) || precision.ValueKind != JsonValueKind.Number || !precision.TryGetInt32(out var digits) || digits != 0) { diagnostic = path + ".options.precision"; return null; }
                Copy("min", "minimum"); Copy("max", "maximum");
                foreach (var bound in constraints.Values)
                    if (bound.ValueKind != JsonValueKind.Number || !bound.TryGetInt64(out var limit) || limit is < -9007199254740991L or > 9007199254740991L) { diagnostic = path + ".options: safe-integer-range-required"; return null; }
                // 原版使用 JS Number，服务端也固定安全整数边界，避免授权 API 先保存浏览器不能无损回显的值。
                using var minimum = JsonDocument.Parse("-9007199254740991"); using var maximum = JsonDocument.Parse("9007199254740991");
                constraints.TryAdd("minimum", minimum.RootElement.Clone()); constraints.TryAdd("maximum", maximum.RootElement.Clone());
            }
            if (fieldType is "radio" or "checkbox" or "select")
            {
                if (!options.TryGetProperty("optionItems", out var items) || items.ValueKind != JsonValueKind.Array || items.EnumerateArray().Any(i => !i.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.String)) { diagnostic = path + ".options.optionItems: typed-choice-binding-required"; return null; }
                var values = items.EnumerateArray().Select(i => i.GetProperty("value").GetString()).ToArray();
                constraints["options"] = JsonSerializer.SerializeToElement(values, Serialization.WorkflowJsonSerializerContext.Default.StringArray);
            }
            fields.Add(new(Read(options, "name") ?? "", fieldType, options.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True, constraints));
        }
        var schema = new WorkflowFormSchema(1, 1, [new("main", fields)]);
        var compilation = WorkflowFormCompiler.Compile(schema);
        if (!compilation.IsSuccess) { diagnostic = compilation.ErrorCode; return null; }
        return schema;
    }

    /// <summary>绑定身份、原设计路径和值协议与同一草稿修订一起固化；不从标签推断字段名。</summary>
    public static WorkflowCompiledArtifact CompileBindings(JsonElement design, WorkflowFormSchema schema, long revision)
    {
        var fields = schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var bindings = new JsonArray();
        foreach (var (widget, path) in Widgets(design.GetProperty("widgetList"), "widgetList"))
        {
            if (!widget.TryGetProperty("options", out var options) || Read(options, "name") is not { } key || !fields.TryGetValue(key, out var field) ||
                Read(widget, "category") == "container" || Read(widget, "type") is "static-text" or "html-text" or "divider" or "button" or "alert") continue;
            var codec = field.FieldTypeKey switch { "integer" => "safe-integer", "switch" => "boolean", "checkbox" => "string-array", "date" => "iso-date", "time" => "iso-time", _ => "string" };
            bindings.Add((JsonNode)new JsonObject { ["componentId"] = Read(widget, "id"), ["designPath"] = path, ["fieldKey"] = key,
                ["modelPath"] = new JsonArray(Read(design.GetProperty("formConfig"), "modelName") ?? "formData", key),
                ["fieldType"] = field.FieldTypeKey, ["codec"] = codec });
        }
        var manifest = new JsonObject { ["bindingVersion"] = 1, ["bindingRevision"] = revision,
            ["targets"] = new JsonArray("web"), ["rendererHash"] = WorkflowDesignerRelease.ContentHash,
            ["adapterVersion"] = WorkflowDesignerRelease.AdapterVersion, ["compilerVersion"] = WorkflowDesignerRelease.CompilerVersion,
            ["components"] = JsonNode.Parse(WorkflowDesignerRelease.Components.GetRawText()),
            ["designHash"] = WorkflowJsonCanonicalizer.Compile(design).ContentHash, ["bindings"] = bindings };
        using var unsigned = JsonDocument.Parse(manifest.ToJsonString());
        manifest["bindingHash"] = WorkflowJsonCanonicalizer.Compile(unsigned.RootElement).ContentHash;
        using var document = JsonDocument.Parse(manifest.ToJsonString());
        return WorkflowJsonCanonicalizer.Compile(document.RootElement);
    }

    /// <summary>只投影可见输入；脚本遇隐藏依赖时拒绝输出，避免把完整设计转交办理端。</summary>
    public static JsonElement? CreateRuntimeView(string json, IReadOnlyDictionary<string, string> policies)
    {
        using var document = JsonDocument.Parse(json);
        if (!IsValid(document.RootElement)) return null;
        var root = JsonNode.Parse(json)!.AsObject();
        // 根扩展属于设计资产，不向办理端发送重建记录或未经绑定的外部元数据。
        foreach (var key in root.Select(p => p.Key).Where(k => k is not ("widgetList" or "formConfig")).ToArray()) root.Remove(key);
        bool IsInput(JsonObject obj) => obj["type"] is JsonValue type && type.TryGetValue<string>(out var name) && name is "input" or "textarea" or "number" or "date" or "time" or "radio" or "checkbox" or "select" or "switch";
        bool HasHiddenInput(JsonNode? value) => value is JsonObject obj
            ? IsInput(obj) && obj["options"]?["name"]?.GetValue<string>() is { } name && (!policies.TryGetValue(name, out var policy) || policy == "hidden") || obj.Any(p => HasHiddenInput(p.Value))
            : value is JsonArray array && array.Any(HasHiddenInput);
        var hidden = HasHiddenInput(root);
        bool HasCode(JsonNode? value) => value is JsonObject obj ? obj.Any(p => (p.Key.StartsWith("on", StringComparison.Ordinal) || p.Key is "functions" or "dataSources" or "customRule") && p.Value is not null && p.Value.ToJsonString() is not ("\"\"" or "[]" or "{}" or "null") || HasCode(p.Value)) : value is JsonArray array && array.Any(HasCode);
        if (hidden && HasCode(root)) return null;
        void Prune(JsonNode? value)
        {
            if (value is JsonArray array)
            {
                for (var i = array.Count - 1; i >= 0; i--)
                {
                    var item = array[i];
                    if (item is JsonObject obj && IsInput(obj) && obj["options"]?["name"]?.GetValue<string>() is { } name)
                    {
                        if (!policies.TryGetValue(name, out var policy) || policy == "hidden") { array.RemoveAt(i); continue; }
                        var options = obj["options"]!.AsObject(); options["disabled"] = policy == "readOnly"; options["required"] = policy == "required";
                        // 实际提交值由另一个授权投影提供，旧默认值不能覆盖实例当前值。
                        options.Remove("defaultValue");
                    }
                    Prune(item);
                }
            }
            else if (value is JsonObject obj)
                foreach (var child in obj.Where(p => p.Key is "widgetList" or "cols" or "rows" or "tabs").ToArray()) Prune(child.Value);
        }
        Prune(root);
        using var result = JsonDocument.Parse(root.ToJsonString());
        return result.RootElement.Clone();
    }

    private static string? Read(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static IEnumerable<(JsonElement Widget, string Path)> Widgets(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                foreach (var child in Widgets(item, path + "[" + index + "]")) yield return child;
                index++;
            }
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("type", out _)) yield return (value, path);
            foreach (var property in value.EnumerateObject())
                if (property.Name is "widgetList" or "cols" or "rows" or "tabs")
                    foreach (var child in Widgets(property.Value, path + "." + property.Name)) yield return child;
        }
    }
}
