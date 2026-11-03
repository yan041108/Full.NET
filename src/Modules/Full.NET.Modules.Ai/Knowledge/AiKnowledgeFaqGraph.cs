using System.Text.Json;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>父节点保持准确发布身份与全部文档叶；边只引用同一有界图中的文档身份，不使用递归 JSON。</summary>
internal sealed record AiKnowledgeFaqParentNode(AiKnowledgeIndexStatusRecord Status, AiKnowledgeFaqAnswerSource Source,
    IReadOnlyList<Guid> ParentDocumentIds, AiKnowledgeFaqParentPublicationProof Publication);
internal sealed record AiKnowledgeFaqGraph(int Version, IReadOnlyList<Guid> Roots, IReadOnlyList<AiKnowledgeFaqParentNode> Nodes);
/// <summary>持久化服务器已完整验证的父发布身份和摘要；来源身份不能代替当前授权。</summary>
internal sealed record AiKnowledgeFaqParentPublicationProof(Guid Id, int Version, string SnapshotHash);
/// <summary>仅供最终 SQL 核证的当前批准模型，不进入持久来源或公开响应。</summary>
internal sealed record AiKnowledgeFaqParentAuthorization(AiKnowledgeFaqParentNode Node, AiModelConfigRecord Model, string ModelFingerprint, AiKnowledgeBaseRecord Knowledge, IReadOnlyList<AiKnowledgeFaqLibraryProof> Libraries, AiKnowledgeFaqParentPublicationProof Publication);
internal sealed record AiKnowledgeFaqNodeRead(AiKnowledgeFaqAnswerSource Source, AiModelConfigRecord Model, AiKnowledgeBaseRecord Knowledge, IReadOnlyList<AiKnowledgeFaqLibraryProof> Libraries, AiKnowledgeFaqParentPublicationProof Publication);

internal static class AiKnowledgeFaqGraphs
{
    internal const int MaximumNodes = 4;
    internal const int MaximumDepth = 4;
    internal const int MaximumDependencies = 8;
    private static readonly Guid ValidationId = new("01900000-0000-7000-8000-000000000001");

    internal static AiKnowledgeFaqProvenance Capture(AiKnowledgeRetrievalSnapshot snapshot)
        => Capture(snapshot.Sources, snapshot.FaqSources);
    /// <summary>复用已重新核证的完整回答来源，不按展示引用子集捕获父图。</summary>
    internal static AiKnowledgeFaqProvenance Capture(IReadOnlyList<AiKnowledgeIndexStatusRecord> sources, IReadOnlyDictionary<Guid, AiKnowledgeFaqAnswerSource> faqSources)
    {
        var parents = new Dictionary<Guid, AiKnowledgeFaqParentNode>();
        var leaves = new Dictionary<Guid, AiKnowledgeAnswerDependency>();
        var roots = new List<Guid>();
        foreach (var status in sources)
        {
            if (status.FaqRevisionId is null) { AddLeaf(AiKnowledgeFaqProvenanceCodec.Dependency(status)); continue; }
            if (!faqSources.TryGetValue(status.DocumentId, out var source)) throw Invalid();
            roots.Add(status.DocumentId);
            foreach (var leaf in source.Dependencies) AddLeaf(leaf);
            foreach (var ancestor in source.Graph?.Nodes ?? []) AddNode(ancestor);
            AddNode(new(status, source with { Graph = null }, source.Graph?.Roots ?? [], source.CurrentPublication ?? throw Invalid()));
        }
        if (roots.Count == 0) throw Invalid();
        var dependencies = leaves.Values.OrderBy(d => d.DocumentId.ToString("N"), StringComparer.Ordinal).ToArray();
        var graph = new AiKnowledgeFaqGraph(1, roots.OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray(),
            parents.Values.OrderBy(n => n.Status.DocumentId.ToString("N"), StringComparer.Ordinal).ToArray());
        Validate(graph, dependencies);
        var schedules = parents.Values.SelectMany(value => value.Source.PersonalSchedules ?? []).ToArray();
        var business = schedules.Length == 0 ? null : AiKnowledgeFaqBusinessSources.Freeze(schedules);
        AiKnowledgeFaqBusinessSources.Validate(business, dependencies.Length, graph);
        return new(business is null ? 2 : 4, "faq_derived", null, dependencies) { Graph = graph, PersonalSchedules = business };
        void AddLeaf(AiKnowledgeAnswerDependency leaf)
        { if (leaves.TryGetValue(leaf.DocumentId, out var old) && old != leaf) throw Invalid(); leaves[leaf.DocumentId] = leaf; }
        void AddNode(AiKnowledgeFaqParentNode node)
        {
            if (parents.TryGetValue(node.Status.DocumentId, out var old) && (old.Status != node.Status
                || !AiKnowledgeFaqPublishedSourceReader.Same(old.Source, node.Source) || !old.ParentDocumentIds.SequenceEqual(node.ParentDocumentIds) || old.Publication != node.Publication)) throw Invalid();
            parents[node.Status.DocumentId] = node;
        }
    }

    internal static AiKnowledgeFaqSourceGraph? PublicGraph(AiKnowledgeFaqGraph? graph) => graph is null ? null
        : new(graph.Roots, graph.Nodes.Select(n => new AiKnowledgeFaqSourceNode(Public(n.Status, n.Source.FaqId, n.Source.RevisionId), n.ParentDocumentIds)).ToArray());

    internal static void Validate(AiKnowledgeFaqGraph? graph, IReadOnlyList<AiKnowledgeAnswerDependency> leaves,
        Guid? ownFaq = null, Guid? ownDocument = null)
    {
        if (graph is null) return;
        if (graph.Version != 1 || graph.Roots is not { Count: >= 1 and <= MaximumNodes } || graph.Nodes is not { Count: >= 1 and <= MaximumNodes }
            || graph.Roots.Distinct().Count() != graph.Roots.Count || graph.Nodes.Any(n => n is null || n.Status is null || n.Source is null)
            || graph.Nodes.Select(n => n.Status.DocumentId).Distinct().Count() != graph.Nodes.Count) throw Invalid();
        var nodes = graph.Nodes.ToDictionary(n => n.Status.DocumentId);
        if (nodes.Count + leaves.Select(l => l.DocumentId).Distinct().Count() > MaximumDependencies
            || leaves.Any(l => nodes.ContainsKey(l.DocumentId)) || graph.Nodes.Select(n => n.Source.FaqId).Distinct().Count() != graph.Nodes.Count) throw Invalid();
        foreach (var node in graph.Nodes)
        {
            if (node.Source.Graph is not null || node.Source.FaqId == ownFaq || node.Status.DocumentId == ownDocument
                || node.Publication is null || node.Publication.Id != node.Status.PublicationId || node.Publication.Version < 1
                || node.Publication.SnapshotHash is not { Length: 64 } || !node.Publication.SnapshotHash.All(Uri.IsHexDigit)
                || node.Status.OwnerUserId == Guid.Empty || node.Status.DataClassification is not ("public" or "internal" or "restricted")
                || node.ParentDocumentIds is null || node.ParentDocumentIds.Count > MaximumNodes
                || node.ParentDocumentIds.Distinct().Count() != node.ParentDocumentIds.Count
                || node.Source.Dependencies is null || node.Source.Dependencies.Any(d => !leaves.Contains(d))) throw Invalid();
            var dependency = Dependency(node);
            // 既有 v5 验证节点身份及纯文档叶；节点本身不携带另一个图，因而不会递归调用本验证。
            var business = node.Source.PersonalSchedules;
            _ = AiKnowledgeAnswerLineageCodec.Encode(new(business is not null ? 8 : node.Source.FeedbackDerived ? 7 : 5,
                ValidationId, ValidationId, business?.FirstOrDefault()?.OwnerUserId ?? ValidationId, business?.FirstOrDefault()?.TenantId, [dependency], [])
                { PersonalSchedules = business, PersonalScheduleReferences = business is null ? null : [] });
            if (!AiKnowledgeAnswerLineageCodec.Matches(dependency, node.Status)) throw Invalid();
            foreach (var parent in node.ParentDocumentIds)
                if (!nodes.TryGetValue(parent, out var ancestor) || ancestor.Source.Dependencies.Any(d => !node.Source.Dependencies.Contains(d))
                    || ancestor.Source.PersonalSchedules?.Any(value => node.Source.PersonalSchedules?.Contains(value) != true) == true) throw Invalid();
        }
        var visited = new HashSet<Guid>();
        foreach (var root in graph.Roots) Visit(root, [], 1);
        if (visited.Count != nodes.Count) throw Invalid();
        void Visit(Guid id, HashSet<Guid> path, int depth)
        {
            if (depth > MaximumDepth || !nodes.TryGetValue(id, out var node) || !path.Add(id)) throw Invalid();
            visited.Add(id);
            foreach (var parent in node.ParentDocumentIds) Visit(parent, new(path), depth + 1);
        }
    }

    internal static AiKnowledgeAnswerDependency Dependency(AiKnowledgeFaqParentNode node) => new(node.Status.KnowledgeBaseId,
        node.Status.DocumentId, node.Status.VersionId, node.Status.DocumentVersion, node.Status.KnowledgeBaseVersion, null,
        node.Status.ContentHash, node.Status.TaskId ?? Guid.Empty, node.Status.ActiveReceiptHash ?? "", node.Status.ActiveManifestHash ?? "",
        node.Status.ActivePlanHash ?? "", node.Status.PublicationId) { Faq = node.Source };

    internal static AiKnowledgeSearchSource Public(AiKnowledgeIndexStatusRecord status, Guid faq, Guid revision) =>
        new(status.KnowledgeBaseId, status.KnowledgeBaseVersion, status.DocumentId, status.DocumentVersion, status.VersionId,
            status.PublicationId ?? throw Invalid(), status.TaskId ?? throw Invalid(), status.ActiveManifestHash ?? throw Invalid())
        { SourceKind = "faq-revision-v1", FaqId = faq, FaqRevisionId = revision };

    internal static IReadOnlyList<AiKnowledgeSearchSource> Public(AiKnowledgeFaqProvenance origin) =>
        origin.Dependencies.Select(d => new AiKnowledgeSearchSource(d.KnowledgeBaseId, d.KnowledgeBaseVersion, d.DocumentId,
            d.DocumentVersion, d.VersionId, d.PublicationId ?? throw Invalid(), d.TaskId, d.ManifestHash))
        .Concat(origin.Graph?.Nodes.Select(n => Public(n.Status, n.Source.FaqId, n.Source.RevisionId)) ?? []).ToArray();

    internal static bool Same(AiKnowledgeFaqGraph? first, AiKnowledgeFaqGraph? second) =>
        JsonSerializer.Serialize(first, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqGraph) == JsonSerializer.Serialize(second, AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqGraph);

    internal static AiKnowledgeFaqGraph? Ancestors(AiKnowledgeFaqGraph graph, AiKnowledgeFaqParentNode node)
    {
        if (node.ParentDocumentIds.Count == 0) return null;
        var ids = new HashSet<Guid>();
        void Include(Guid id) { if (!ids.Add(id)) return; foreach (var parent in graph.Nodes.Single(n => n.Status.DocumentId == id).ParentDocumentIds) Include(parent); }
        foreach (var id in node.ParentDocumentIds) Include(id);
        return new(1, node.ParentDocumentIds, graph.Nodes.Where(n => ids.Contains(n.Status.DocumentId)).ToArray());
    }
    internal static InvalidDataException Invalid() => new("FAQ 父节点图无效、超限、截断或不再具备当前发布资格。");
}
