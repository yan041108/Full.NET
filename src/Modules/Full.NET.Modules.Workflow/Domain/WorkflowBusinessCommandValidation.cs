using System.Text;
using System.Text.Json;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>回执与实际调用、固定根和当前注册动作共同验证；错误输入不能成为跨模块写入授权。</summary>
internal static class WorkflowBusinessCommandValidation
{
    public static bool MatchesDefinition(WorkflowBusinessCommandRecord row, WorkflowDefinitionDraft draft)
    {
        if (draft.Nodes is null || draft.Nodes.Count(node => node.NodeKey == row.NodeKey) != 1) return false;
        var node = draft.Nodes.Single(node => node.NodeKey == row.NodeKey);
        return node.NodeTypeKey == "business.command" && WorkflowBusinessCommandPolicy.TryRead(node.NodeTypeKey, node.Config, out var policy) &&
            policy is not null && policy.ActionKey == row.ActionKey && policy.ActionVersion == row.ActionVersion &&
            node.Config.TryGetProperty("boundBusinessActionHash", out var hash) && WorkflowBusinessCommandPolicy.ValidHash(hash) && hash.GetString() == row.ActionHash &&
            WorkflowBusinessCommandBindings.TryReadFinalizer(draft, row.NodeKey, out _) && node.Config.GetProperty("nextNodeKeys")[0].GetString() == row.ContinuationNodeKey;
    }
    public static WorkflowBusinessCommandBinding? Read(WorkflowBusinessCommandRecord row, WorkflowInstanceRecord instance, WorkflowBusinessActionCatalog catalog)
    {
        if (row.InstanceId != instance.Id || row.TenantScopeKey != instance.TenantScopeKey || row.Revision < 1 || row.Generation is < 1 or > 1000 ||
            Encoding.UTF8.GetByteCount(row.BindingJson) > 8192) return null;
        WorkflowBusinessCommandBinding? binding;
        try { binding = JsonSerializer.Deserialize(row.BindingJson, WorkflowJsonSerializerContext.Default.WorkflowBusinessCommandBinding); }
        catch (JsonException) { return null; }
        var action = catalog.Find(instance.ScopeKey, row.ActionKey, row.ActionVersion, row.ActionHash);
        if (binding is null || action is null || binding.CallId != row.Id || binding.InstanceId != instance.Id || binding.DefinitionVersionId != instance.DefinitionVersionId ||
            binding.ActionKey != row.ActionKey || binding.ActionVersion != row.ActionVersion || binding.ActionHash != row.ActionHash ||
            binding.OwnerKey != action.Definition.OwnerKey || binding.BusinessType != instance.BusinessType || binding.BusinessId != instance.BusinessId ||
            binding.RequestedByUserId != instance.StartedById || action.Definition.BusinessType != instance.BusinessType ||
            WorkflowBusinessCommandHash.Compute(binding) != row.InputHash) return null;
        return binding;
    }
    public static bool ValidResult(WorkflowBusinessCommandResultIntegrationEvent value)
    {
        if (value.CallId == Guid.Empty || value.InstanceId == Guid.Empty || value.InputHash is not { Length: 64 } hash ||
            !hash.All(char.IsAsciiHexDigitLower) || value.Generation is < 1 or > 1000 || value.PhaseKey is not ("execute" or "compensate") ||
            value.ErrorCode is { Length: > 128 } || value.ErrorCode is { } code && !code.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '_' or '.' or '-') ||
            value.OwnerInputHash is { } providedHash && (providedHash.Length != 64 || !providedHash.All(char.IsAsciiHexDigitLower))) return false;
        var ownerHash = value.OwnerInputHash is { Length: 64 } owner && owner.All(char.IsAsciiHexDigitLower);
        return value.OutcomeKey switch {
            "applied" => value.PhaseKey == "execute" && ownerHash && value.AppliedVersion is > 0 && value.CompensatedVersion is null && value.ErrorCode is null,
            "cancelled" => ownerHash && value.AppliedVersion is null && value.CompensatedVersion is null && value.ErrorCode is null,
            "compensated" => ownerHash && value.AppliedVersion is > 0 && value.CompensatedVersion is { } restored && restored > value.AppliedVersion && value.ErrorCode is null,
            "declined" => value.PhaseKey == "execute" && value.ErrorCode is { Length: > 0 } && value.AppliedVersion is null && value.CompensatedVersion is null,
            "unknown" => value.ErrorCode is { Length: > 0 },
            _ => false };
    }
}
