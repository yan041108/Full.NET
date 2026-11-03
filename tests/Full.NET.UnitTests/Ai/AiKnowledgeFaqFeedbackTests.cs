using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Data.Abstractions;

namespace Full.NET.UnitTests.Ai;

/// <summary>私人回答不能经字段删除、父图或旧协议降级；当前反馈资格始终由真实联合查询核证。</summary>
[TestClass]
public sealed class AiKnowledgeFaqFeedbackTests
{
    private static readonly string Hash = new('A', 64);
    private static Guid Id() => Guid.CreateVersion7();
    private static AiKnowledgeAnswerDependency Leaf() => new(Id(), Id(), Id(), 1, 1, Id(), Hash, Id(), Hash, Hash, Hash, Id());
    private static AiKnowledgeFaqFeedbackBinding Binding() => new(Id(), 1, Id(), 2, Id(), Id(), Id(), null, Hash, Hash, Hash, Hash);
    private static AiKnowledgeFaqProvenance Origin() => new(3, "feedback_derived", null, [Leaf()]) { Feedback = Binding() };
    private static AiKnowledgeFaqDraftRecord Row(AiKnowledgeFaqProvenance source, string? json = null)
    {
        json ??= AiKnowledgeFaqProvenanceCodec.Encode(source);
        return new() { Id = Id(), ReservedDocumentId = Id(), KnowledgeBaseId = Id(), TenantId = source.Feedback?.TenantId,
            OriginKey = source.Origin, SourceJson = json, SourceHash = AiKnowledgeFaqDraftPolicy.Hash(json) };
    }
    [TestMethod]
    public void Feedback_origin_roundtrip_preserves_private_binding_and_all_unquoted_dependencies()
    {
        var origin = Origin(); var row = Row(origin); var decoded = AiKnowledgeFaqProvenanceCodec.Decode(row);
        Assert.AreEqual(origin.Feedback, decoded.Feedback); CollectionAssert.AreEqual(origin.Dependencies.ToArray(), decoded.Dependencies.ToArray());
        Assert.IsTrue(decoded.RequiresFeedbackPermission); StringAssert.Contains(row.SourceJson, "\"feedbackPermission\":true");
        var publicJson = JsonSerializer.Serialize(origin.Feedback!.Public(), AiJsonSerializerContext.Default.AiKnowledgeFaqFeedbackSource);
        Assert.IsFalse(publicJson.Contains("ownerUserId", StringComparison.OrdinalIgnoreCase)); Assert.IsFalse(publicJson.Contains("sessionId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(publicJson.Contains("question", StringComparison.OrdinalIgnoreCase));
    }
    [TestMethod]
    [DataRow("feedback")] [DataRow("feedbackId")] [DataRow("reviewId")] [DataRow("messageId")] [DataRow("ownerUserId")]
    [DataRow("sessionId")] [DataRow("lineageHash")] [DataRow("answerHash")] [DataRow("lineageSqlServerHash")] [DataRow("answerSqlServerHash")]
    [DataRow("feedbackVersion")] [DataRow("reviewVersion")] [DataRow("marker")] [DataRow("false_marker")] [DataRow("unknown")]
    [DataRow("version")] [DataRow("kind")] [DataRow("empty_dependencies")] [DataRow("tenant")]
    public void Feedback_origin_rejects_removed_forged_or_downgraded_fields(string fault)
    {
        var origin = Origin(); var json = JsonNode.Parse(AiKnowledgeFaqProvenanceCodec.Encode(origin))!.AsObject();
        if (fault == "feedback") json.Remove("feedback");
        else if (fault == "marker") json.Remove("feedbackPermission");
        else if (fault == "false_marker") json["feedbackPermission"] = false;
        else if (fault == "unknown") json["feedback"]!["unknown"] = "not allowed";
        else if (fault == "version") json["version"] = 2;
        else if (fault == "kind") json["origin"] = "document_derived";
        else if (fault == "empty_dependencies") json["dependencies"] = new JsonArray();
        else if (fault == "tenant") json["feedback"]!["tenantId"] = Id().ToString("D");
        else if (fault.EndsWith("Version", StringComparison.Ordinal)) json["feedback"]![fault] = 0;
        else json["feedback"]!.AsObject().Remove(fault);
        var row = Row(origin, json.ToJsonString());
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqProvenanceCodec.Decode(row));
    }
    [TestMethod]
    public void Inherited_feedback_marker_is_derived_from_real_parent_nodes_and_cannot_be_erased()
    {
        var leaf = Leaf(); var node = AiKnowledgeFaqGraphTests.Node();
        node = node with { Status = node.Status with { DataClassification = "restricted" }, Source = node.Source with { Dependencies = [leaf], FeedbackDerived = true } };
        var origin = new AiKnowledgeFaqProvenance(2, "faq_derived", null, [leaf]) { Graph = new(1, [node.Status.DocumentId], [node]) };
        var row = Row(origin); var decoded = AiKnowledgeFaqProvenanceCodec.Decode(row);
        Assert.IsTrue(decoded.RequiresFeedbackPermission); Assert.IsNull(decoded.Feedback);
        var altered = JsonNode.Parse(row.SourceJson)!.AsObject(); altered.Remove("feedbackPermission");
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqProvenanceCodec.Decode(Row(origin, altered.ToJsonString())));
    }
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void Private_history_uses_v7_and_old_protocols_cannot_hide_direct_or_ancestor_source(bool inherited)
    {
        var leaf = Leaf(); var node = AiKnowledgeFaqGraphTests.Node();
        node = node with { Status = node.Status with { DataClassification = "restricted" }, Source = node.Source with { Dependencies = [leaf], FeedbackDerived = true } };
        var source = new AiKnowledgeFaqAnswerSource(Id(), Id(), Hash, Hash, Id(), 1, [leaf])
        { FeedbackDerived = !inherited, Graph = inherited ? new(1, [node.Status.DocumentId], [node]) : null };
        var dependency = new AiKnowledgeAnswerDependency(Id(), Id(), Id(), 1, 1, null, Hash, Id(), Hash, Hash, Hash, Id()) { Faq = source };
        var lineage = new AiKnowledgeAnswerLineage(7, Id(), Id(), Id(), null, [dependency], []);
        var json = AiKnowledgeAnswerLineageCodec.Encode(lineage);
        Assert.AreEqual(7, AiKnowledgeAnswerLineageCodec.Decode(json, lineage.MessageId, lineage.SessionId, lineage.ActorUserId, null).Version);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Encode(lineage with { Version = inherited ? 6 : 5 }));
    }
    [TestMethod]
    [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public void Joint_feedback_predicate_rechecks_review_message_scope_and_both_hash_encodings(bool mysql, bool tenant)
    {
        var sql = AiKnowledgeFaqFeedbackSql.PendingGraphFor(mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer, tenant).Text;
        foreach (var required in new[] { "feedback.Version", "review.Version", "review.DecisionKey='approved'", "message.RoleKey='assistant'", "message.StatusKey='completed'",
            "message.KnowledgeLineage", "message.Content", "k.Version=@TargetKnowledgeVersion", "@FaqFeedbackReadAllowed", "@FaqParentBindingsJson" }) StringAssert.Contains(sql, required);
        Assert.IsFalse(sql.Contains("fn_identity", StringComparison.Ordinal)); Assert.IsFalse(sql.Contains("RoleKey='user'", StringComparison.Ordinal));
        StringAssert.Contains(sql, tenant ? "feedback.TenantId=@TenantId" : "feedback.TenantId IS NULL");
        StringAssert.Contains(sql, mysql ? "SHA2(" : "HASHBYTES('SHA2_256'");
    }
    [TestMethod]
    public void Feedback_fingerprint_preserves_non_ascii_newlines_and_distinguishes_provider_encodings()
    {
        const string answer = "私人回答😀\r\n第二行";
        Assert.AreNotEqual(AiKnowledgeFaqDraftPolicy.Hash(answer), AiKnowledgeFaqFeedbackBinding.UnicodeHash(answer));
        Assert.AreNotEqual(AiKnowledgeFaqFeedbackBinding.UnicodeHash(answer), AiKnowledgeFaqFeedbackBinding.UnicodeHash(answer.Replace("\r\n", "\n", StringComparison.Ordinal)));
        Assert.ThrowsExactly<System.Text.EncoderFallbackException>(() => AiKnowledgeFaqFeedbackBinding.UnicodeHash("\ud800"));
    }
}
