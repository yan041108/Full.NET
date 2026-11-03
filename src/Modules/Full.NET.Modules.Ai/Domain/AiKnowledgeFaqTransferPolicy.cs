using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>传输包是当前权威记录的封闭快照，不能通过删除来源字段或重算包摘要获取新权威。</summary>
internal static class AiKnowledgeFaqTransferPolicy
{
    internal const string Format = "faq-transfer-v4";
    internal const int MaximumItems = 8;
    internal const int MaximumBytes = 512 * 1024;
    /// <summary>同库复制保留相同容器和全部依赖；跨库不能用低分类容器重新定义独立来源。</summary>
    internal static bool CanPlace(string source, string target, bool sameKnowledgeBase)
    {
        static int Rank(string value) => value switch { "public" => 0, "internal" => 1, "restricted" => 2, _ => -1 };
        var minimum = Rank(source); var destination = Rank(target);
        return minimum >= 0 && destination >= 0 && (sameKnowledgeBase || destination >= minimum);
    }

    internal static string Digest(AiKnowledgeFaqTransferPackage package)
    {
        if (package is null || package.Format != Format || package.Items is not { Count: >= 1 and <= MaximumItems }
            || package.Items.Any(value => value is null) || package.Items.Select(value => value.FaqId).Distinct().Count() != package.Items.Count
            || package.Items.Select(value => value.KnowledgeBaseId).Distinct().Count() != 1) throw Invalid();
        foreach (var item in package.Items)
        {
            if (item.FaqId == Guid.Empty || item.KnowledgeBaseId == Guid.Empty || item.RevisionId == Guid.Empty || item.HeadVersion < 1
                || !Hash(item.ContentHash) || !Hash(item.SourceHash) || item.Sources is not { Count: <= 8 }
                || item.Sources.Any(source => source is null) || item.Origin is not ("independent" or "document_derived" or "faq_derived" or "feedback_derived")
                || (item.Origin == "faq_derived" ? item.Graph is null : item.Origin != "feedback_derived" && item.Graph is not null)
                || (item.Origin == "feedback_derived") != (item.Feedback is not null)) throw Invalid();
            if (item.PersonalSchedules is { } business)
            {
                if (item.Origin == "independent" || AiPersonalScheduleAnswerSources.Freeze(business) is null
                    || item.Sources.Count + business.Count > 8) throw Invalid();
            }
            if (item.Feedback is { } feedback && (new[] { feedback.FeedbackId, feedback.ReviewId, feedback.MessageId }.Any(id => id == Guid.Empty)
                || feedback.FeedbackVersion < 1 || feedback.ReviewVersion < 1 || !Hash(feedback.LineageHash) || !Hash(feedback.AnswerHash))) throw Invalid();
            _ = AiKnowledgeFaqDraftPolicy.Encode(item.Content);
            if (item.Origin == "independent" ? item.Sources.Count != 0 || item.Basis is null || AiKnowledgeFaqDraftPolicy.Basis(item.Basis) != item.Basis
                : item.Basis is not null || item.Sources.Count == 0) throw Invalid();
            ValidateGraph(item);
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(package, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqTransferPackage);
        if (bytes.Length > MaximumBytes) throw Invalid();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    internal static AiKnowledgeFaqTransferItem From(AiKnowledgeFaqDraftWorkspace value) => new(value.Id, value.KnowledgeBaseId,
            value.Revision.Id, value.HeadVersion, value.Origin, value.Basis, value.SourceHash, value.Revision.ContentHash, value.Revision.Content, value.Sources)
            { Graph = value.Graph, Feedback = value.Feedback, PersonalSchedules = value.PersonalSchedules };

    internal static bool Same(AiKnowledgeFaqTransferItem first, AiKnowledgeFaqTransferItem second) =>
        JsonSerializer.Serialize(first, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqTransferItem)
        == JsonSerializer.Serialize(second, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqTransferItem);
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static void ValidateGraph(AiKnowledgeFaqTransferItem item)
    {
        if (item.Graph is not { } graph) return;
        if (graph.Nodes is not { Count: >= 1 and <= 4 } || graph.Roots is not { Count: >= 1 and <= 4 } || graph.Nodes.Any(n => n is null || n.Source is null)
            || graph.Nodes.Select(n => n.Source.DocumentId).Distinct().Count() != graph.Nodes.Count || graph.Roots.Distinct().Count() != graph.Roots.Count) throw Invalid();
        var nodes = graph.Nodes.ToDictionary(n => n.Source.DocumentId); var seen = new HashSet<Guid>();
        foreach (var node in graph.Nodes)
            if (node.Source.SourceKind != "faq-revision-v1" || node.Source.FaqId is null || node.Source.FaqRevisionId is null || !item.Sources.Contains(node.Source)
                || node.ParentDocumentIds is null || node.ParentDocumentIds.Count > 4 || node.ParentDocumentIds.Distinct().Count() != node.ParentDocumentIds.Count) throw Invalid();
        foreach (var id in graph.Roots) Visit(id, [], 1);
        if (seen.Count != nodes.Count || item.Sources.Count(s => s.SourceKind == "faq-revision-v1") != nodes.Count) throw Invalid();
        void Visit(Guid id, HashSet<Guid> path, int depth)
        {
            if (depth > 4 || !nodes.TryGetValue(id, out var node) || !path.Add(id)) throw Invalid(); seen.Add(id);
            foreach (var parent in node.ParentDocumentIds) Visit(parent, new(path), depth + 1);
        }
    }
    private static InvalidDataException Invalid() => new("FAQ 传输包无效、超限或来源类型不受支持。");
}
