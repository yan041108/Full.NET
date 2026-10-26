using System.Collections.Frozen;
using System.Text.Json;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Serialization;
using Microsoft.Extensions.Configuration;

namespace Full.NET.Modules.Workflow.Execution.Connectors;

/// <summary>部署侧固定版本，定义只持有键和摘要；凭据引用从不包含凭据原文。</summary>
internal sealed record WorkflowHttpConnectorRegistration(string ConnectorKey, int Version, Uri Target,
    IReadOnlySet<string> AllowedScopes, WorkflowFormSchema RequestSchema, WorkflowFormSchema ResponseSchema,
    bool SupportsIdempotency, int TimeoutSeconds, int MaximumAttempts, string? BearerSecretReference);

/// <summary>发布和执行必须命中同一目录版本及配置摘要，不能静默改用最新端点或跨租户凭据。</summary>
internal sealed class WorkflowConnectorCatalog
{
    private readonly FrozenDictionary<(string Key, int Version), WorkflowBoundHttpConnector> entries;

    public WorkflowConnectorCatalog(IEnumerable<WorkflowHttpConnectorRegistration> registrations)
    {
        var values = registrations.ToArray();
        if (values.Length > 64 || values.GroupBy(item => (item.ConnectorKey, item.Version)).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Workflow connector catalog is invalid.");
        entries = values.Select(Freeze).ToFrozenDictionary(item => (item.Definition.ConnectorKey, item.Definition.Version));
    }

    /// <summary>配置读取采用静态字段和源生成Schema，不从定义请求读取URL、凭据或可变服务实例。</summary>
    public static WorkflowConnectorCatalog FromConfiguration(IConfiguration configuration)
    {
        var registrations = new List<WorkflowHttpConnectorRegistration>();
        foreach (var section in configuration.GetSection("Workflow:HttpConnectors").GetChildren())
        {
            if (!int.TryParse(section["Version"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version) ||
                !Uri.TryCreate(section["Target"], UriKind.Absolute, out var target) || section["Key"] is not { } key ||
                !int.TryParse(section["TimeoutSeconds"] ?? "30", out var timeout) || !int.TryParse(section["MaximumAttempts"] ?? "3", out var attempts) ||
                !bool.TryParse(section["SupportsIdempotency"] ?? "false", out var idempotent)) throw new InvalidOperationException("Workflow connector configuration is invalid.");
            var request = ReadSchema(section["RequestSchemaJson"]); var response = ReadSchema(section["ResponseSchemaJson"]);
            var scopes = section.GetSection("AllowedScopes").GetChildren().Select(item => item.Value ?? string.Empty).ToHashSet(StringComparer.Ordinal);
            registrations.Add(new(key, version, target, scopes, request, response, idempotent, timeout, attempts, section["BearerSecretReference"]));
        }
        return new(registrations);
    }

    /// <summary>发布必须重新绑定部署侧目录摘要，客户端提交的旧摘要不能成为权威依据。</summary>
    public bool TryBindDraft(string scope, WorkflowDefinitionDraft draft, WorkflowFormSchema schema, out WorkflowDefinitionDraft? bound)
    {
        bound = null;
        // 原图校验仍由编译器给出原错误；绑定器不能提前解引用非法草稿或扩大图预算。
        if (draft.Nodes is null || draft.Nodes.Count > WorkflowDefinitionCompiler.MaximumNodes || draft.Nodes.Any(node => node is null) ||
            !draft.Nodes.Any(node => node.NodeTypeKey is "integration.trigger" or "integration.async_trigger")) { bound = draft; return true; }
        if (!WorkflowFormCompiler.Compile(schema).IsSuccess) return false;
        var nodes = new List<WorkflowNodeDraft>(draft.Nodes.Count);
        foreach (var node in draft.Nodes)
        {
            if (node.NodeTypeKey is not ("integration.trigger" or "integration.async_trigger")) { nodes.Add(node); continue; }
            if (!WorkflowConnectorPolicy.TryRead(node.Config, schema, out var policy) || !CanBind(scope, policy!, out var connector)) return false;
            var config = WorkflowJsonCanonicalizer.Compile(writer =>
            {
                writer.WriteStartObject(); writer.WriteString("boundConnectorHash", connector!.ContentHash);
                foreach (var property in node.Config.EnumerateObject().Where(item => item.Name != "boundConnectorHash").OrderBy(item => item.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); property.Value.WriteTo(writer); }
                writer.WriteEndObject();
            });
            using var parsed = JsonDocument.Parse(config.CanonicalJson);
            nodes.Add(node with { Config = parsed.RootElement.Clone() });
        }
        bound = draft with { Nodes = nodes };
        return true;
    }

    private static WorkflowFormSchema ReadSchema(string? json)
    {
        try
        {
            return json is not null && System.Text.Encoding.UTF8.GetByteCount(json) <= 131072 &&
                JsonSerializer.Deserialize(json, WorkflowJsonSerializerContext.Default.WorkflowFormSchema) is { } schema
                ? schema : throw new InvalidOperationException("Workflow connector schema is invalid.");
        }
        catch (JsonException) { throw new InvalidOperationException("Workflow connector schema is invalid."); }
    }

    public WorkflowBoundHttpConnector? Find(string scope, string key, int version, string? expectedHash = null) =>
        entries.TryGetValue((key, version), out var value) && value.Definition.AllowedScopes.Contains(scope) &&
        (expectedHash is null || expectedHash == value.ContentHash) ? value : null;

    public IReadOnlyList<WorkflowBoundHttpConnector> ForScope(string scope) => entries.Values.Where(item => item.Definition.AllowedScopes.Contains(scope))
        .OrderBy(item => item.Definition.ConnectorKey, StringComparer.Ordinal).ThenBy(item => item.Definition.Version).ToArray();

    public bool CanBind(string scope, WorkflowConnectorPolicy policy, out WorkflowBoundHttpConnector? connector)
    {
        connector = Find(scope, policy.ConnectorKey, policy.ConnectorVersion);
        if (connector is null || policy.Schema is null) return false;
        var fields = policy.Schema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var request = connector.Definition.RequestSchema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        var response = connector.Definition.ResponseSchema.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.FieldKey, StringComparer.Ordinal);
        return request.Values.Where(field => field.Required).All(field => policy.InputFields.ContainsKey(field.FieldKey)) &&
            policy.InputFields.All(pair => request.TryGetValue(pair.Key, out var target) && fields.TryGetValue(pair.Value, out var source) && target.FieldTypeKey == source.FieldTypeKey) &&
            policy.OutputFields.All(pair => response.TryGetValue(pair.Value, out var source) && fields.TryGetValue(pair.Key, out var target) && target.FieldTypeKey == source.FieldTypeKey);
    }

    private static WorkflowBoundHttpConnector Freeze(WorkflowHttpConnectorRegistration value)
    {
        if (!WorkflowNodeKeyValidator.IsValid(value.ConnectorKey) || value.ConnectorKey.Length > 64 || value.Version < 1 ||
            value.Target is not { IsAbsoluteUri: true } || !WorkflowConnectorNetwork.AllowsTarget(value.Target) ||
            value.AllowedScopes.Count is < 1 or > 256 || value.AllowedScopes.Any(scope => scope != "host" &&
                !(scope.StartsWith("tenant:", StringComparison.Ordinal) && Guid.TryParseExact(scope[7..], "N", out var id) && id != Guid.Empty && scope == $"tenant:{id:N}")) ||
            value.TimeoutSeconds is < 1 or > 120 || value.MaximumAttempts is < 1 or > 5 ||
            value.BearerSecretReference is { } secret && (!secret.StartsWith("env:", StringComparison.Ordinal) || secret.Length is < 5 or > 68 ||
                !System.Text.RegularExpressions.Regex.IsMatch(secret[4..], "^[A-Z][A-Z0-9_]{0,63}$")))
            throw new InvalidOperationException("Workflow connector registration is invalid.");
        var frozen = value with { AllowedScopes = value.AllowedScopes.ToFrozenSet(StringComparer.Ordinal),
            RequestSchema = FreezeSchema(value.RequestSchema), ResponseSchema = FreezeSchema(value.ResponseSchema) };
        var canonical = WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("key", frozen.ConnectorKey); writer.WriteNumber("version", frozen.Version);
            writer.WriteString("target", frozen.Target.AbsoluteUri); writer.WriteBoolean("idempotent", frozen.SupportsIdempotency);
            writer.WriteNumber("timeout", frozen.TimeoutSeconds); writer.WriteNumber("attempts", frozen.MaximumAttempts);
            writer.WriteString("secretReference", frozen.BearerSecretReference); writer.WritePropertyName("scopes"); writer.WriteStartArray();
            foreach (var scope in frozen.AllowedScopes.Order(StringComparer.Ordinal)) writer.WriteStringValue(scope);
            writer.WriteEndArray(); writer.WritePropertyName("request"); JsonSerializer.Serialize(writer, frozen.RequestSchema, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            writer.WritePropertyName("response"); JsonSerializer.Serialize(writer, frozen.ResponseSchema, WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
            writer.WriteEndObject();
        });
        return new(frozen, canonical.ContentHash);
    }

    private static WorkflowFormSchema FreezeSchema(WorkflowFormSchema schema)
    {
        // 目录使用与表单相同的闭合字段类型；复制可变容器，避免注册后被调用方修改。
        var frozen = new WorkflowFormSchema(schema.SchemaVersion, schema.AdapterVersion, Array.AsReadOnly(schema.Sections.Select(section =>
            new WorkflowFormSection(section.SectionKey, Array.AsReadOnly(section.Fields.Select(field => field with {
                Constraints = field.Constraints.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal) }).ToArray()))).ToArray()));
        if (!WorkflowFormCompiler.Compile(frozen).IsSuccess) throw new InvalidOperationException("Workflow connector schema is invalid.");
        return frozen;
    }
}

/// <summary>固定版本配置及摘要共同形成发布绑定，不向客户端返回目标或凭据。</summary>
internal sealed record WorkflowBoundHttpConnector(WorkflowHttpConnectorRegistration Definition, string ContentHash);
