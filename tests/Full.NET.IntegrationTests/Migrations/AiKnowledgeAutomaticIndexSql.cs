using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>历史迁移上界尚无人工校对表和列，冻结其自动解析语义；332 的当前行为单独验证。</summary>
internal static class AiKnowledgeAutomaticIndexSql
{
    internal static string AutomaticSql(this SqlStatement statement) => statement.Text
        .Replace("," + AiKnowledgeIndexCorrectionSql.Columns, "", StringComparison.Ordinal)
        .Replace("," + AiKnowledgeIndexCorrectionSql.Values, "", StringComparison.Ordinal)
        .Replace(AiKnowledgeIndexCorrectionSql.Joins, "", StringComparison.Ordinal)
        .Replace(AiKnowledgeIndexCorrectionSql.Available, "", StringComparison.Ordinal)
        .Replace(AiKnowledgeIndexCorrectionSql.Live, "", StringComparison.Ordinal)
        .Replace(AiKnowledgeIndexCorrectionSql.Live.Replace("fn_ai_knowledge_index_task.", "task.", StringComparison.Ordinal), "", StringComparison.Ordinal)
        .Replace("," + AiKnowledgeIndexCorrectionSql.DispatchColumns, "", StringComparison.Ordinal)
        .Replace("," + AiKnowledgeIndexCorrectionSql.AuthorityColumns, "", StringComparison.Ordinal)
        .Replace(" AND task.CorrectionRevisionId IS NULL", "", StringComparison.Ordinal)
        .Replace(" AND CorrectionRevisionId IS NULL", "", StringComparison.Ordinal);
}
