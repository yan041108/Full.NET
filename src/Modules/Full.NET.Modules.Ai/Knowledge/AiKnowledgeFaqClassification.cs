using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using Full.NET.AI.Abstractions.Models;

namespace Full.NET.Modules.Ai.Knowledge;

internal sealed record AiKnowledgeFaqLibraryProof(Guid Id, int Version, string DataClassification);
internal sealed record AiKnowledgeFaqClassificationResult(string Value, AiKnowledgeBaseRecord Root, IReadOnlyList<AiKnowledgeFaqLibraryProof> Libraries);
/// <summary>原始文档与所有祖先容器共同决定分类；不依赖来源门禁，避免后台核证形成服务环。</summary>
internal sealed class AiKnowledgeFaqClassification(IQueryExecutor query, IDataTransactionState transactions, ICurrentTenant tenant,
    IOptions<DatabaseOptions> database, AiKnowledgeReadAccess access)
{
    /// <summary>只计算该父节点自己的完整来源交集，不把同批其他节点的库当成其依赖。</summary>
    internal static AiKnowledgeFaqClassificationResult? ForParent(Guid knowledge, AiKnowledgeFaqProvenance origin, IReadOnlyList<AiKnowledgeBaseRecord> libraries)
    {
        var result = ForParentLibraries(knowledge, origin.Dependencies.Select(d => d.KnowledgeBaseId).Concat(origin.Graph?.Nodes.Select(n => n.Status.KnowledgeBaseId) ?? []), libraries);
        return !(origin.RequiresFeedbackPermission || origin.PersonalSchedules is not null) || result?.Root.DataClassification == "restricted" ? result : null;
    }
    internal static AiKnowledgeFaqClassificationResult? ForParentLibraries(Guid knowledge, IEnumerable<Guid> sourceLibraries, IReadOnlyList<AiKnowledgeBaseRecord> libraries)
    {
        var ids = sourceLibraries.Append(knowledge).Distinct().ToArray();
        if (knowledge == Guid.Empty || libraries.Select(library => library.Id).Distinct().Count() != libraries.Count
            || ids.Any(id => libraries.All(library => library.Id != id))) return null;
        var own = libraries.Where(library => ids.Contains(library.Id)).ToArray();
        if (own.Any(library => !library.IsEnabled || !AiDataProcessingPolicy.IsKnown(library.DataClassification))) return null;
        return new(AiDataProcessingPolicy.Strongest(own.Select(library => library.DataClassification)), own.Single(library => library.Id == knowledge),
            own.Select(library => new AiKnowledgeFaqLibraryProof(library.Id, library.Version, library.DataClassification)).ToArray());
    }
    /// <summary>父图一次捕获所有可读库的当前分类/版本；最终 SQL 逐一绑定，不能跨门禁缓存。</summary>
    internal async Task<IReadOnlyList<AiKnowledgeBaseRecord>?> ReadLibrariesAsync(IReadOnlyList<Guid> ids, Guid actor, CancellationToken token)
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("FAQ 分类核证不能加入事务。");
        if (ids.Count is < 1 or > 8 || ids.Distinct().Count() != ids.Count) return null;
        var scope = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant);
        var p = await access.ParametersAsync(actor, token).ConfigureAwait(false); var libraries = new List<AiKnowledgeBaseRecord>();
        foreach (var id in ids)
        {
            p["Id"] = id;
            var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
            if (row is not { IsEnabled: true } || Rank(row.DataClassification) < 0) return null;
            libraries.Add(row);
        }
        if (transactions.HasTransaction || tenant.Id != scope || AiKnowledgeScope.IsTenant(tenant) != scoped) throw new UnauthorizedAccessException();
        return libraries;
    }
    internal async Task<AiKnowledgeFaqClassificationResult?> ReadAsync(AiKnowledgeFaqDraftRecord row, Guid actor, CancellationToken token)
    {
        if (transactions.HasTransaction) throw new InvalidOperationException("FAQ 分类核证不能加入事务。");
        var scope = tenant.Id; var scoped = AiKnowledgeScope.IsTenant(tenant);
        var p = await access.ParametersAsync(actor, token).ConfigureAwait(false); p["Id"] = row.KnowledgeBaseId;
        var root = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
        if (root is not { IsEnabled: true } || Rank(root.DataClassification) < 0) return null;
        var origin = AiKnowledgeFaqProvenanceCodec.Decode(row); var rank = Rank(root.DataClassification);
        if ((origin.RequiresFeedbackPermission || origin.PersonalSchedules is not null) && rank != 2) return null;
        var libraries = new List<AiKnowledgeFaqLibraryProof> { new(root.Id, root.Version, root.DataClassification) };
        if (origin.Dependencies.Count > 0)
        {
            p["DependenciesJson"] = JsonSerializer.Serialize(origin.Dependencies.ToArray(), AiKnowledgeAnswerLineageJsonContext.Default.AiKnowledgeAnswerDependencyArray);
            var current = await query.QueryAsync<AiKnowledgeIndexStatusRecord>(AiKnowledgeIndexStatusSql.HistoryFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
            if (current.Count != origin.Dependencies.Count || origin.Dependencies.Any(value => current.Count(other => AiKnowledgeAnswerLineageCodec.Matches(value, other)) != 1)
                || current.Any(value => Rank(value.DataClassification) < 0)) return null;
            rank = Math.Max(rank, current.Max(value => Rank(value.DataClassification)));
        }
        foreach (var id in origin.Dependencies.Select(d => d.KnowledgeBaseId).Concat(origin.Graph?.Nodes.Select(node => node.Status.KnowledgeBaseId) ?? []).Distinct().Where(id => id != root.Id))
        {
            p["Id"] = id;
            var ancestor = await query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(AiKnowledgeSql.ReadFor(database.Value.Provider, scoped), p, token).ConfigureAwait(false);
            if (ancestor is not { IsEnabled: true } || Rank(ancestor.DataClassification) < 0) return null;
            rank = Math.Max(rank, Rank(ancestor.DataClassification));
            libraries.Add(new(ancestor.Id, ancestor.Version, ancestor.DataClassification));
        }
        if (transactions.HasTransaction || tenant.Id != scope || AiKnowledgeScope.IsTenant(tenant) != scoped) throw new UnauthorizedAccessException();
        return new(rank switch { 0 => "public", 1 => "internal", 2 => "restricted", _ => throw new InvalidDataException() }, root, libraries);
    }
    private static int Rank(string value) => value switch { "public" => 0, "internal" => 1, "restricted" => 2, _ => -1 };
}
