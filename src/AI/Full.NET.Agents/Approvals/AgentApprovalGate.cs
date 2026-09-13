using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Full.NET.Agents.Approvals;

/// <summary>审批绑定校验；参数哈希与版本不匹配时失败关闭，不自动重放。</summary>
public static class AgentApprovalGate
{
    public const int CurrentPolicyVersion = 1;

    /// <summary>规范化参数 JSON 的 SHA-256 十六进制摘要。</summary>
    public static string ComputeArgumentsHash(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.Undefined)
        {
            return "unavailable";
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.GetRawText())));
    }

    /// <summary>消费前核对 Run/Operation/工具/参数/主体/决策/期限绑定。</summary>
    public static bool TryValidateForConsume(
        AgentApprovalBinding binding,
        Guid operationId,
        Guid? runId,
        string toolName,
        int toolVersion,
        JsonElement arguments,
        Guid actorUserId,
        Guid? actorTenantId,
        DateTimeOffset now,
        out string errorCode)
    {
        errorCode = string.Empty;
        if (binding.OperationId != operationId)
        {
            errorCode = "ai.agent_approval.operation_mismatch";
            return false;
        }

        if (binding.RunId != runId)
        {
            errorCode = "ai.agent_approval.run_mismatch";
            return false;
        }

        if (!string.Equals(binding.ToolName, toolName, StringComparison.Ordinal))
        {
            errorCode = "ai.agent_approval.tool_mismatch";
            return false;
        }

        if (binding.ToolVersion != toolVersion)
        {
            errorCode = "ai.agent_approval.tool_version_mismatch";
            return false;
        }

        if (!string.Equals(binding.ArgumentsHash, ComputeArgumentsHash(arguments), StringComparison.Ordinal))
        {
            errorCode = "ai.agent_approval.arguments_mismatch";
            return false;
        }

        if (binding.RequestedBy != actorUserId)
        {
            errorCode = "ai.agent_approval.actor_mismatch";
            return false;
        }

        if (binding.TenantId != actorTenantId)
        {
            errorCode = "ai.agent_approval.tenant_mismatch";
            return false;
        }

        if (!string.Equals(binding.DecisionKey, AgentApprovalDecisionKeys.Approved, StringComparison.Ordinal))
        {
            errorCode = binding.DecisionKey switch
            {
                AgentApprovalDecisionKeys.Pending => "ai.agent_approval.pending",
                AgentApprovalDecisionKeys.Denied => "ai.agent_approval.denied",
                _ => "ai.agent_approval.invalid_decision",
            };
            return false;
        }

        if (binding.ExpiresAtUtc <= now)
        {
            errorCode = "ai.agent_approval.expired";
            return false;
        }

        if (binding.ConsumedAtUtc is not null)
        {
            errorCode = "ai.agent_approval.already_consumed";
            return false;
        }

        if (binding.PolicyVersion != CurrentPolicyVersion)
        {
            errorCode = "ai.agent_approval.policy_incompatible";
            return false;
        }

        return true;
    }
}

/// <summary>审批决策键。</summary>
public static class AgentApprovalDecisionKeys
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Denied = "denied";
}

/// <summary>消费前绑定的审批快照。</summary>
public sealed record AgentApprovalBinding(
    Guid Id,
    Guid RunId,
    Guid OperationId,
    Guid? TenantId,
    string ToolName,
    int ToolVersion,
    string ArgumentsHash,
    int PolicyVersion,
    Guid RequestedBy,
    string DecisionKey,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    long Version);
