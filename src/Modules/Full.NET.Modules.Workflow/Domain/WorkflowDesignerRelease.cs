using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>发布与办理固定实际 Web 产物；只有当前受审构建可以解释原版设计快照。</summary>
internal static class WorkflowDesignerRelease
{
    private static readonly JsonElement Release = Load();
    public static string ContentHash => Release.GetProperty("contentHash").GetString()!;
    public static string AdapterVersion => Release.GetProperty("adapterVersion").GetString()!;
    public static int CompilerVersion => Release.GetProperty("compilerVersion").GetInt32();
    public static JsonElement Components => Release.GetProperty("components");

    private static JsonElement Load()
    {
        using var stream = typeof(WorkflowDesignerRelease).Assembly.GetManifestResourceStream("Full.NET.Workflow.VFormRuntimeRelease.json")
            ?? throw new InvalidOperationException("workflow.vform_runtime_release_missing");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    public static bool Matches(string? bindingJson)
    {
        if (bindingJson is null) return false;
        try
        {
            using var document = JsonDocument.Parse(bindingJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("bindingHash", out var bindingHash)) return false;
            var unsigned = JsonNode.Parse(bindingJson)!.AsObject(); unsigned.Remove("bindingHash");
            using var payload = JsonDocument.Parse(unsigned.ToJsonString());
            if (WorkflowJsonCanonicalizer.Compile(payload.RootElement).ContentHash != bindingHash.GetString()) return false;
            return root.TryGetProperty("rendererHash", out var hash) && hash.GetString() == ContentHash &&
                root.TryGetProperty("compilerVersion", out var compiler) && compiler.GetInt32() == CompilerVersion &&
                root.TryGetProperty("adapterVersion", out var adapter) && adapter.GetString() == AdapterVersion;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return false; }
    }

    /// <summary>只读快照属于 Workflow；查询同时约束版本所属定义的可信租户。</summary>
    public static Task<string?> ReadBindingsAsync(IQueryExecutor query, Guid versionId, WorkflowManagementScope scope, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<string>(WorkflowDesignerAssetSql.FindBindings,
            WorkflowSqlParameters.Create(("FormVersionId", versionId), ("TenantScopeKey", scope.TenantScopeKey)), token);

    public static bool AllowsExecution(string? publication)
    {
        if (Matches(publication)) return true;
        if (publication is null) return false;
        try
        {
            using var document = JsonDocument.Parse(publication);
            var root = document.RootElement;
            // 程序化消费者使用业务 Schema，不依赖 VForm；缺快照的 VForm 资产不得冒充该路径。
            return !root.TryGetProperty("widgetList", out _) && root.TryGetProperty("schemaVersion", out var version) && version.ValueKind == JsonValueKind.Number && version.GetInt32() == 1 && root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return false; }
    }

    public static JsonElement AttachAuthorizedBindings(JsonElement view, string bindingJson, IReadOnlyDictionary<string, string> policies)
    {
        var root = JsonNode.Parse(view.GetRawText())!.AsObject();
        var publication = JsonNode.Parse(bindingJson)!.AsObject();
        // 办理端只收到自己的字段清单，不能通过元数据获知隐藏输入名或默认内容。
        publication["bindings"] = new JsonArray(publication["bindings"]!.AsArray()
            .Where(binding => policies.TryGetValue(binding!["fieldKey"]!.GetValue<string>(), out var policy) && policy != "hidden")
            .Select(binding => binding!.DeepClone()).ToArray());
        publication.Remove("designHash");
        root["fullNetRuntime"] = publication;
        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }
}
