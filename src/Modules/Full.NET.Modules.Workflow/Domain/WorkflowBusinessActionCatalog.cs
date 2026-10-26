using System.Security.Cryptography;
using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>目录由实际模块静态注册；同键版本的不同声明拒绝启动，发布摘要不能由普通请求指定。</summary>
internal sealed class WorkflowBusinessActionCatalog
{
    private readonly IReadOnlyList<WorkflowBoundBusinessAction> definitions;
    public WorkflowBusinessActionCatalog(IEnumerable<WorkflowBusinessActionDefinition> registrations)
    {
        var entries = registrations.Distinct().ToArray();
        if (entries.Length > 64 || entries.Any(item => !WorkflowBusinessCommandPolicy.ValidKey(item.OwnerKey) || !WorkflowBusinessCommandPolicy.ValidKey(item.ActionKey) ||
                item.ActionVersion is < 1 or > 65535 || item.ScopeKey is not ("host" or "tenant") ||
                string.IsNullOrWhiteSpace(item.BusinessType) || item.BusinessType.Length > 64 || item.PlacementKey != "root_finalizer") ||
            entries.GroupBy(item => (item.ActionKey, item.ActionVersion)).Any(group => group.Count() != 1))
            throw new InvalidOperationException("workflow.business_action_catalog_invalid");
        definitions = entries.OrderBy(item => item.ActionKey, StringComparer.Ordinal).ThenBy(item => item.ActionVersion)
            .Select(item => new WorkflowBoundBusinessAction(item, Convert.ToHexStringLower(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(item, WorkflowJsonSerializerContext.Default.WorkflowBusinessActionDefinition))))).ToArray();
    }
    public IReadOnlyList<WorkflowBoundBusinessAction> ForScope(string scopeKey) => definitions.Where(item => item.Definition.ScopeKey == scopeKey).ToArray();
    public WorkflowBoundBusinessAction? Find(string scopeKey, string actionKey, int version, string? hash = null) =>
        definitions.SingleOrDefault(item => item.Definition.ScopeKey == scopeKey && item.Definition.ActionKey == actionKey &&
            item.Definition.ActionVersion == version && (hash is null || item.Hash == hash));
}

/// <summary>实际动作及固定元数据摘要，不包含业务快照或目标记录。</summary>
internal sealed record WorkflowBoundBusinessAction(WorkflowBusinessActionDefinition Definition, string Hash);
