namespace Full.NET.Modules.Ai.Contracts;

/// <summary>AI 模块稳定业务错误码。</summary>
public static class AiErrorCodes
{
    /// <summary>模型配置不存在。</summary>
    public const string ModelConfigNotFound = "ai.model_config.not_found";

    /// <summary>模型配置元数据校验失败。</summary>
    public const string ModelConfigInvalid = "ai.model_config.invalid";

    /// <summary>模型配置并发版本冲突。</summary>
    public const string ModelConfigConcurrencyConflict = "ai.model_config.concurrency_conflict";

    /// <summary>模型配置 API 密钥必填。</summary>
    public const string ModelConfigApiKeyRequired = "ai.model_config.api_key_required";

    /// <summary>租户配额不存在。</summary>
    public const string TenantQuotaNotFound = "ai.tenant_quota.not_found";

    /// <summary>租户配额元数据校验失败。</summary>
    public const string TenantQuotaInvalid = "ai.tenant_quota.invalid";

    /// <summary>租户配额并发版本冲突。</summary>
    public const string TenantQuotaConcurrencyConflict = "ai.tenant_quota.concurrency_conflict";

    /// <summary>租户不存在或不可用。</summary>
    public const string TenantNotFound = "ai.tenant.not_found";

    /// <summary>聊天会话不存在。</summary>
    public const string ChatSessionNotFound = "ai.chat_session.not_found";

    /// <summary>聊天会话元数据校验失败。</summary>
    public const string ChatSessionInvalid = "ai.chat_session.invalid";

    /// <summary>聊天会话并发版本冲突。</summary>
    public const string ChatSessionConcurrencyConflict = "ai.chat_session.concurrency_conflict";

    /// <summary>聊天消息校验失败。</summary>
    public const string ChatMessageInvalid = "ai.chat_message.invalid";

    /// <summary>聊天生成正在进行中。</summary>
    public const string ChatGenerationInProgress = "ai.chat_generation.in_progress";

    /// <summary>聊天生成未在进行中。</summary>
    public const string ChatGenerationNotActive = "ai.chat_generation.not_active";

    /// <summary>聊天执行或必要收尾失败。</summary>
    public const string ChatGenerationFailed = "ai.chat_generation.failed";

    /// <summary>租户 AI 配额已用尽。</summary>
    public const string TenantQuotaExceeded = "ai.tenant_quota.exceeded";

    /// <summary>模型配置不可用。</summary>
    public const string ModelConfigUnavailable = "ai.model_config.unavailable";

    /// <summary>Agent Tool 未在静态目录中登记。</summary>
    public const string AgentToolNotFound = "ai.agent_tool.not_found";

    /// <summary>工具副作用已发生但回执未知，需对账。</summary>
    public const string AgentToolReconciliationRequired = "ai.tool.reconciliation_required";

    /// <summary>Agent 运行不存在或不属于当前用户。</summary>
    public const string AgentRunNotFound = "ai.agent_run.not_found";

    /// <summary>Agent 运行请求无效。</summary>
    public const string AgentRunInvalid = "ai.agent_run.invalid";

    /// <summary>Agent 运行时未就绪或已禁用。</summary>
    public const string AgentRuntimeUnavailable = "ai.agent_runtime.unavailable";

    /// <summary>Agent 运行已处于不可取消状态。</summary>
    public const string AgentRunNotCancellable = "ai.agent_run.not_cancellable";

    /// <summary>Agent 运行当前不可恢复入队。</summary>
    public const string AgentRunNotResumable = "ai.agent_run.not_resumable";

    /// <summary>AG-UI 事件游标已过期，客户端需重新同步运行状态。</summary>
    public const string AgentRunEventsCursorExpired = "ai.agent_run.events_cursor_expired";

    /// <summary>Agent 审批不存在或不属于当前用户。</summary>
    public const string AgentApprovalNotFound = "ai.agent_approval.not_found";

    /// <summary>Agent 审批请求无效。</summary>
    public const string AgentApprovalInvalid = "ai.agent_approval.invalid";

    /// <summary>Agent 审批已过期、已消费或不可决定。</summary>
    public const string AgentApprovalNotDecidable = "ai.agent_approval.not_decidable";

    /// <summary>Agent 审批并发版本冲突。</summary>
    public const string AgentApprovalConcurrencyConflict = "ai.agent_approval.concurrency_conflict";

    /// <summary>当前用户无权决定该审批。</summary>
    public const string AgentApprovalNotAuthorized = "ai.agent_approval.not_authorized";

    /// <summary>Agent 委托不存在或不属于当前用户。</summary>
    public const string AgentDelegationNotFound = "ai.agent_delegation.not_found";

    /// <summary>Agent 委托请求无效。</summary>
    public const string AgentDelegationInvalid = "ai.agent_delegation.invalid";

    /// <summary>Agent 委托并发版本冲突。</summary>
    public const string AgentDelegationConcurrencyConflict = "ai.agent_delegation.concurrency_conflict";

    /// <summary>MCP 远端连接不存在。</summary>
    public const string McpRemoteConnectionNotFound = "ai.mcp.remote_connection.not_found";

    /// <summary>MCP 远端连接元数据无效。</summary>
    public const string McpRemoteConnectionInvalid = "ai.mcp.remote_connection.invalid";

    /// <summary>MCP 远端连接键或版本冲突。</summary>
    public const string McpRemoteConnectionConflict = "ai.mcp.remote_connection.conflict";

    /// <summary>MCP 远端连接不可用。</summary>
    public const string McpRemoteConnectionUnavailable = "ai.mcp.remote_connection.unavailable";

    /// <summary>远端 MCP 工具不存在。</summary>
    public const string McpRemoteToolNotFound = "ai.mcp.remote_tool.not_found";

    /// <summary>远端 MCP 工具无法批准。</summary>
    public const string McpRemoteToolInvalid = "ai.mcp.remote_tool.invalid";
}
