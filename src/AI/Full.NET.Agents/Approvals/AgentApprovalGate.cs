using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Full.NET.Agents.Approvals;

/// <summary>审批绑定校验；参数哈希与版本不匹配时失败关闭，不自动重放。</summary>
public static class AgentApprovalGate
{
    /// <summary>当前审批绑定策略版本；消费时必须与绑定快照一致，否则拒绝执行。</summary>
    public const int CurrentPolicyVersion = 1;

    /// <summary>
    /// 规范化参数 JSON 的 SHA-256 十六进制摘要。
    /// </summary>
    /// <param name="arguments">待计算摘要的工具参数 JSON；未定义时返回固定占位串。</param>
    /// <returns>参数原始文本的 SHA-256 十六进制摘要；参数未定义时返回 "unavailable"。</returns>
    public static string ComputeArgumentsHash(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.Undefined)
        {
            return "unavailable";
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(arguments.GetRawText())));
    }

    /// <summary>
    /// 消费前核对 Run/Operation/工具/参数/主体/决策/期限绑定。
    /// </summary>
    /// <param name="binding">审批绑定快照；必须已持久化且未消费。</param>
    /// <param name="operationId">当前操作标识，须与绑定一致。</param>
    /// <param name="runId">Agent Run 标识；无 Run 上下文时为 <see langword="null"/>。</param>
    /// <param name="toolName">工具名，使用 Ordinal 比较。</param>
    /// <param name="toolVersion">工具版本号，须与绑定一致。</param>
    /// <param name="arguments">工具参数 JSON，将重新计算摘要与绑定比对。</param>
    /// <param name="actorUserId">当前操作用户标识，须与绑定申请人一致。</param>
    /// <param name="actorTenantId">当前租户标识；宿主上下文时为 <see langword="null"/>。</param>
    /// <param name="now">当前时间（UTC），用于判断是否过期。</param>
    /// <param name="errorCode">校验失败时输出稳定错误码；成功时为空字符串。</param>
    /// <returns>全部绑定条件均满足时为 <see langword="true"/>；任何一项不匹配即失败关闭，不自动重放。</returns>
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
/// <remarks>决策键字符串发布后不可改名或删除；新增决策只能追加。</remarks>
public static class AgentApprovalDecisionKeys
{
    /// <summary>审批待定，尚未作出决策。</summary>
    public const string Pending = "pending";

    /// <summary>审批通过，允许执行。</summary>
    public const string Approved = "approved";

    /// <summary>审批拒绝，禁止执行。</summary>
    public const string Denied = "denied";
}

/// <summary>消费前绑定的审批快照。</summary>
/// <remarks>
/// 字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。
/// 消费校验为失败关闭语义：任何一项绑定不匹配即拒绝执行，不自动重放。
/// </remarks>
/// <param name="Id">审批绑定记录唯一标识。</param>
/// <param name="RunId">关联的 Agent Run 标识。</param>
/// <param name="OperationId">关联的操作标识。</param>
/// <param name="TenantId">申请时的租户标识；宿主上下文为 <see langword="null"/>。</param>
/// <param name="ToolName">申请执行的工具名。</param>
/// <param name="ToolVersion">申请执行的工具版本号。</param>
/// <param name="ArgumentsHash">申请时工具参数的 SHA-256 摘要。</param>
/// <param name="PolicyVersion">绑定创建时的策略版本；消费时须与当前版本一致。</param>
/// <param name="RequestedBy">申请人用户标识。</param>
/// <param name="DecisionKey">审批决策键，取值见 <see cref="AgentApprovalDecisionKeys"/>。</param>
/// <param name="ExpiresAtUtc">绑定过期时间（UTC）；过期后不可消费。</param>
/// <param name="ConsumedAtUtc">消费时间（UTC）；已消费时不可再次消费。</param>
/// <param name="Version">绑定记录乐观并发版本号。</param>
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
