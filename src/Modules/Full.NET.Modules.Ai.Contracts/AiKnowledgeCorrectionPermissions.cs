namespace Full.NET.Modules.Ai.Contracts;

/// <summary>校对原解析属于维护正文；读取和本地解析准备分别授权，均不授予模型外发或发布资格。</summary>
public static class AiKnowledgeCorrectionPermissions
{
    /// <summary>读取有权维护的原解析及人工修订。</summary>
    public const string Read = "ai.knowledge_corrections.read";
    /// <summary>准备或重新解析本人来源，不创建收费模型操作。</summary>
    public const string Prepare = "ai.knowledge_corrections.prepare";
    /// <summary>更正、拆合、确认、补写或排除本人来源，不授予发布资格。</summary>
    public const string Edit = "ai.knowledge_corrections.edit";
    /// <summary>明确恢复历史修订或采用重解析来源，仍须写入新审计及 CAS 版本。</summary>
    public const string Restore = "ai.knowledge_corrections.restore";
}
