using System.Text.Json.Nodes;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>FAQ 是有准确修订和完整叶证明的节点，不是可下载的普通文件。</summary>
[TestClass]
public sealed class AiKnowledgeFaqSearchSourceTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [TestMethod]
    public void Frozen_search_source_rejects_type_downgrade_and_wrong_faq_identity()
    {
        var source = new AiKnowledgeSearchSource(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), 2, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Hash)
            { SourceKind = "faq-revision-v1", FaqRevisionId = Guid.CreateVersion7(), FaqId = Guid.CreateVersion7() };
        Assert.IsTrue(AiKnowledgeSearchSources.Valid(source));
        Assert.IsFalse(AiKnowledgeSearchSources.Valid(source with { SourceKind = "document-v1" }));
        Assert.IsFalse(AiKnowledgeSearchSources.Valid(source with { SourceKind = "future-faq-v9" }));
        Assert.IsFalse(AiKnowledgeSearchSources.Valid(source with { FaqId = null }));
        Assert.IsFalse(AiKnowledgeSearchSources.Valid(source with { FaqRevisionId = Guid.Empty }));
        Assert.IsFalse(AiKnowledgeSearchSources.Valid(source with { SourceKind = "document-v1", FaqId = null }));
        Assert.IsTrue(AiKnowledgeSearchSources.Valid(source with { SourceKind = "document-v1", FaqRevisionId = null, FaqId = null }));
    }

    [TestMethod]
    public void Version_five_keeps_faq_and_every_leaf_even_without_a_displayed_citation()
    {
        var value = Lineage(); var encoded = AiKnowledgeAnswerLineageCodec.Encode(value);
        var restored = AiKnowledgeAnswerLineageCodec.Decode(encoded, value.MessageId, value.SessionId, value.ActorUserId, null);
        Assert.AreEqual(5, restored.Version); Assert.AreEqual(0, restored.References.Count);
        Assert.IsNull(restored.Dependencies[0].FileId);
        Assert.AreEqual(value.Dependencies[0].Faq, restored.Dependencies[0].Faq);
        CollectionAssert.AreEqual(value.Dependencies[0].Faq!.Dependencies.ToArray(), restored.Dependencies[0].Faq!.Dependencies.ToArray());
        Assert.IsTrue(restored.Dependencies[0].Faq!.Dependencies.All(leaf => leaf.FileId.HasValue && leaf.Faq is null));
    }

    [TestMethod]
    [DataRow("remove_node")]
    [DataRow("fake_file")]
    [DataRow("legacy_version")]
    [DataRow("unknown_field")]
    [DataRow("nested_faq")]
    [DataRow("missing_model")]
    public void Persisted_faq_cannot_drop_or_forge_its_source_protocol(string mutation)
    {
        var value = Lineage(); var json = JsonNode.Parse(AiKnowledgeAnswerLineageCodec.Encode(value))!;
        var dependency = json["dependencies"]![0]!.AsObject(); var faq = dependency["faq"]!.AsObject();
        switch (mutation)
        {
            case "remove_node": dependency.Remove("faq"); break;
            case "fake_file": dependency["fileId"] = Guid.CreateVersion7().ToString(); break;
            case "legacy_version": json["version"] = 4; break;
            case "unknown_field": faq["optionalAuthorization"] = true; break;
            case "nested_faq": faq["dependencies"]![0]!["faq"] = faq.DeepClone(); break;
            case "missing_model": faq["modelConfigId"] = Guid.Empty.ToString(); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Decode(json.ToJsonString(), value.MessageId, value.SessionId, value.ActorUserId, null));
    }

    [TestMethod]
    public void Full_leaf_budget_rejects_overflow_instead_of_truncating_faq_dependencies()
    {
        var value = Lineage(); var first = value.Dependencies[0];
        var second = first with { DocumentId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(), Faq = first.Faq! with
            { FaqId = Guid.CreateVersion7(), RevisionId = Guid.CreateVersion7(), Dependencies = Enumerable.Range(0, 7).Select(_ => Leaf()).ToArray() } };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Encode(value with { Dependencies = [first, second] }));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false)] [DataRow(DatabaseProvider.SqlServer, true)]
    [DataRow(DatabaseProvider.MySql, false)] [DataRow(DatabaseProvider.MySql, true)]
    public void Combined_candidate_budget_and_faq_branch_keep_ordinary_exclusion_and_complete_leaf_gate(DatabaseProvider provider, bool tenant)
    {
        var statement = AiKnowledgeRetrievalSql.CombinedSourcesFor(provider, tenant, true);
        StringAssert.Contains(statement.Text, "UNION ALL");
        StringAssert.Contains(statement.Text, "FROM (SELECT " + AiKnowledgePublishedSourceSql.Columns + " FROM",
            "普通候选必须包含完整投影，不能受分部静态字段初始化顺序影响。");
        StringAssert.Contains(statement.Text, "UNION ALL SELECT " + AiKnowledgePublishedSourceSql.Columns + " FROM",
            "FAQ 候选同样必须包含完整投影，叶证明的嵌套投影不能替代候选投影。");
        StringAssert.Contains(statement.Text, AiKnowledgeOrdinaryDocumentSql.Visible);
        StringAssert.Contains(statement.Text, "version.FaqRevisionId IS NOT NULL");
        StringAssert.Contains(statement.Text, "f.SourceJson");
        StringAssert.Contains(statement.Text, "source.ActiveReceiptHash=dependency.ReceiptHash");
        Assert.AreEqual(tenant ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, statement.Scope);
        Assert.AreEqual(1, statement.Text.Split(provider == DatabaseProvider.MySql ? "LIMIT 9" : "TOP (9)").Length - 1);
    }

    private static AiKnowledgeAnswerLineage Lineage()
    {
        var source = new AiKnowledgeAnswerDependency(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, null,
            Hash, Guid.CreateVersion7(), Hash, Hash, Hash, Guid.CreateVersion7())
            { Faq = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Hash, Hash, Guid.CreateVersion7(), 1, [Leaf(), Leaf()]) };
        return new(5, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null, [source], []);
    }
    private static AiKnowledgeAnswerDependency Leaf() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1,
        Guid.CreateVersion7(), Hash, Guid.CreateVersion7(), Hash, Hash, Hash, Guid.CreateVersion7());
}
