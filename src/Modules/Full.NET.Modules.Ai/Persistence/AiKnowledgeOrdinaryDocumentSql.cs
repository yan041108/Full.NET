namespace Full.NET.Modules.Ai.Persistence;

/// <summary>普通文档协议不具备 FAQ 修订和完整谱系证明，不能凭所有者或已有物化记录绕过来源边界。</summary>
internal static class AiKnowledgeOrdinaryDocumentSql
{
    // 绑定身份全局唯一；故意不按调用者租户过滤 FAQ 根，损坏或跨库绑定也必须拒绝。
    internal const string Visible = "NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_faq AS managedFaq WHERE managedFaq.ReservedDocumentId=document.Id)";
    internal const string Writable = "NOT EXISTS (SELECT 1 FROM fn_ai_knowledge_faq AS managedFaq WHERE managedFaq.ReservedDocumentId=@DocumentId)";
}
