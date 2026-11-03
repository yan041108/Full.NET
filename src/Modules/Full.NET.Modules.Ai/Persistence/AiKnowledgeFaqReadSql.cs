using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>FAQ 继承知识库的明确读者范围，并由调用者叠加 FAQ 权限和全部来源交集；不借用普通文档协议。</summary>
internal static class AiKnowledgeFaqReadSql
{
    internal static string Audience(bool mysql, bool scoped)
    {
        var knowledge = AiKnowledgeSql.ReadFor(mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer, scoped).Text;
        // 仅静态构建固定语句，不拼接用户输入；外围语句仍显式保留可信租户谓词。
        return "EXISTS (SELECT 1 FROM (" + knowledge.Replace("@Id", "@KnowledgeBaseId", StringComparison.Ordinal)
            + ") AS faqAudience WHERE faqAudience.Id=knowledge.Id)";
    }
    internal static string AudienceForRoot(bool mysql, bool scoped) => Audience(mysql, scoped).Replace("faqAudience.Id=knowledge.Id", "faqAudience.Id=k.Id", StringComparison.Ordinal);

    /// <summary>多库召回先筛选当前知识库读者；移除固定单库身份条件，仍与外层库身份相关联。</summary>
    internal static string AudienceForSelection(bool mysql, bool scoped)
    {
        var read = AiKnowledgeSql.ReadFor(mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer, scoped).Text;
        return "EXISTS (SELECT 1 FROM (" + read.Replace("WHERE Id = @Id AND ", "WHERE ", StringComparison.Ordinal)
            + ") AS faqAudience WHERE faqAudience.Id=knowledge.Id)";
    }
}
