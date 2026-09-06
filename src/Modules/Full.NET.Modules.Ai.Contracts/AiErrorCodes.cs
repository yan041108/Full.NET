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

    /// <summary>租户 AI 配额已用尽。</summary>
    public const string TenantQuotaExceeded = "ai.tenant_quota.exceeded";

    /// <summary>模型配置不可用。</summary>
    public const string ModelConfigUnavailable = "ai.model_config.unavailable";
}
