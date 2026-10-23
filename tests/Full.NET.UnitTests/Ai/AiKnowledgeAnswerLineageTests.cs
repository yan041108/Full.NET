using System.Text.Json.Nodes;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Knowledge Answer Lineage。</summary>
[TestClass]
public sealed class AiKnowledgeAnswerLineageTests
{
    internal static AiKnowledgeIndexStatusRecord Source() => new()
    {
        KnowledgeBaseId = Guid.CreateVersion7(), DocumentId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(),
        OwnerUserId = Guid.CreateVersion7(), DocumentVersion = 2, KnowledgeBaseVersion = 3, FileId = Guid.CreateVersion7(),
        ContentHash = new string('A', 64), TaskId = Guid.CreateVersion7(), ActiveLeaseToken = Guid.CreateVersion7(),
        StateKey = "completed", IsCurrent = 1, HasDerivedData = 1, ActiveReceiptHash = new string('B', 64),
        ActiveManifestHash = new string('C', 64), ActivePlanHash = new string('D', 64), ContentType = "application/pdf"
    };
    internal static AiKnowledgeAnswerDependency Dependency(AiKnowledgeIndexStatusRecord source) => new(source.KnowledgeBaseId,
        source.DocumentId, source.VersionId, source.DocumentVersion, source.KnowledgeBaseVersion, source.FileId,
        source.ContentHash, source.TaskId!.Value, source.ActiveReceiptHash!, source.ActiveManifestHash!, source.ActivePlanHash!);
    internal static AiKnowledgeAnswerLineage Lineage(Guid? tenant = null)
    {
        var a = Source(); var b = Source();
        return new(1, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), tenant,
            [Dependency(a), Dependency(b)], [new(Guid.CreateVersion7(), a.DocumentId, a.VersionId, 2, 3, 10, 5)]);
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Lineage_roundtrip_preserves_unreferenced_dependency_and_trusted_binding(bool tenant)
    {
        var data = Lineage(tenant ? Guid.CreateVersion7() : null); var encoded = AiKnowledgeAnswerLineageCodec.Encode(data);
        var read = AiKnowledgeAnswerLineageCodec.Decode(encoded, data.MessageId, data.SessionId, data.ActorUserId, data.TenantId);
        CollectionAssert.AreEqual(data.Dependencies.ToArray(), read.Dependencies.ToArray());
        CollectionAssert.AreEqual(data.References.ToArray(), read.References.ToArray());
        Assert.HasCount(2, read.Dependencies); Assert.HasCount(1, read.References);
        foreach (var forbidden in new[] { "apiKey", "endpoint", "vector", "text", "leaseToken" })
            Assert.IsFalse(encoded.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void New_answers_use_version_three_for_legacy_and_unified_dependencies(bool unified)
    {
        var row = Source(); row = row with { ActiveTaskId = row.TaskId, ActivePlanHash = unified ? new string('d', 64) : row.ActivePlanHash };
        var chunk = AiKnowledgeRetrievalRuleTests.Chunk("application/pdf", 0, AiKnowledgeRetrievalRuleTests.Vector(1, 0)) with
        { Source = row, SourceLocation = unified ? new(0, "pdf_page", 1, null, null, null, null, null, null, null, null) : null };
        var snapshot = new AiKnowledgeRetrievalSnapshot(Guid.CreateVersion7(), null, false, null!, [row.KnowledgeBaseId], [row], [new(chunk, 1)]);
        var answer = new AiKnowledgeExecutedAnswer(new(snapshot, null!), new("拒答", [], true), Guid.CreateVersion7(), Guid.CreateVersion7());
        var lineage = AiKnowledgeAnswerLineageCodec.Create(answer, Guid.CreateVersion7(), Guid.CreateVersion7());
        var read = AiKnowledgeAnswerLineageCodec.Decode(AiKnowledgeAnswerLineageCodec.Encode(lineage), lineage.MessageId, lineage.SessionId, lineage.ActorUserId, null);
        Assert.AreEqual(3, read.Version); Assert.IsEmpty(read.References); Assert.HasCount(1, read.Dependencies);
        Assert.AreEqual(row.ActivePlanHash, read.Dependencies[0].PlanHash);
    }
    [TestMethod] [DataRow("text/csv")] [DataRow("application/pdf")] [DataRow("image/png")]
    public void Unquoted_unified_source_without_selected_chunks_still_keeps_version_three_dependencies(string mime)
    {
        var row = Source(); row = row with { ContentType = mime, ActiveTaskId = row.TaskId, ActivePlanHash = new string('d', 64) };
        var text = Source(); text = text with { ContentType = "text/plain", ActiveTaskId = text.TaskId };
        var selected = AiKnowledgeRetrievalRuleTests.Chunk("text/plain", 0, AiKnowledgeRetrievalRuleTests.Vector(1, 0)) with { Source = text, PageNumber = null };
        var snapshot = new AiKnowledgeRetrievalSnapshot(Guid.CreateVersion7(), null, false, null!, [row.KnowledgeBaseId, text.KnowledgeBaseId], [row, text], [new(selected, 1)]);
        var answer = new AiKnowledgeExecutedAnswer(new(snapshot, null!), new("仅引用 TXT", [new(Guid.CreateVersion7(), selected)], false), Guid.CreateVersion7(), Guid.CreateVersion7());
        var lineage = AiKnowledgeAnswerLineageCodec.Create(answer, Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.AreEqual(3, lineage.Version); Assert.HasCount(1, lineage.References); Assert.HasCount(2, lineage.Dependencies);
        Assert.AreEqual(text.DocumentId, lineage.References[0].DocumentId);
        var read = AiKnowledgeAnswerLineageCodec.Decode(AiKnowledgeAnswerLineageCodec.Encode(lineage), lineage.MessageId, lineage.SessionId, lineage.ActorUserId, null);
        Assert.AreEqual(row.ActivePlanHash, read.Dependencies.Single(source => source.DocumentId == row.DocumentId).PlanHash);
    }
    [TestMethod]
    public void Version_three_rejects_legacy_location_on_encode_as_well_as_decode()
    {
        var original = Lineage(); var reference = original.References[0] with { PageNumber = null, DocxLocation = new(0, 1, null, null, null) };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Encode(original with { Version = 3, References = [reference] }));
    }
    [TestMethod] [DataRow("message")] [DataRow("session")] [DataRow("actor")] [DataRow("tenant")]
    public void Lineage_cannot_be_copied_to_another_answer_or_scope(string fault)
    {
        var data = Lineage(); var encoded = AiKnowledgeAnswerLineageCodec.Encode(data);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Decode(encoded,
            fault == "message" ? Guid.CreateVersion7() : data.MessageId,
            fault == "session" ? Guid.CreateVersion7() : data.SessionId,
            fault == "actor" ? Guid.CreateVersion7() : data.ActorUserId,
            fault == "tenant" ? Guid.CreateVersion7() : data.TenantId));
    }
    [TestMethod] [DataRow("version")] [DataRow("unknown")] [DataRow("empty")] [DataRow("duplicate")]
    [DataRow("orphan")] [DataRow("hash")] [DataRow("offset")] [DataRow("page")] [DataRow("overflow")]
    [DataRow("missing")] [DataRow("null")] [DataRow("large")] [DataRow("repeatField")] [DataRow("surrogate")]
    public void Corrupt_lineage_fails_closed(string fault)
    {
        var data = Lineage(); var json = JsonNode.Parse(AiKnowledgeAnswerLineageCodec.Encode(data))!.AsObject();
        switch (fault)
        {
            case "version": json["version"] = 4; break;
            case "unknown": json["vector"] = "secret"; break;
            case "empty": json["dependencies"] = new JsonArray(); break;
            case "duplicate": json["dependencies"]!.AsArray().Add(json["dependencies"]![0]!.DeepClone()); break;
            case "orphan": json["references"]![0]!["documentId"] = Guid.CreateVersion7().ToString(); break;
            case "hash": json["dependencies"]![0]!["contentHash"] = "bad"; break;
            case "offset": json["references"]![0]!["offset"] = -1; break;
            case "page": json["references"]![0]!["pageNumber"] = 0; break;
            case "overflow": json["references"]![0]!["offset"] = int.MaxValue; break;
            case "missing": json.Remove("actorUserId"); break;
        }
        var encoded = fault switch { "null" => null, "large" => new string(' ', 65537), "surrogate" => "\ud800",
            "repeatField" => json.ToJsonString().Replace("\"version\":1", "\"version\":1,\"version\":1"), _ => json.ToJsonString() };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Decode(encoded,
            data.MessageId, data.SessionId, data.ActorUserId, data.TenantId));
    }
    [TestMethod] [DataRow("live")] [DataRow("deleted")] [DataRow("cleanup")] [DataRow("current")]
    [DataRow("task")] [DataRow("receipt")] [DataRow("manifest")] [DataRow("plan")] [DataRow("version")]
    public void Dependency_requires_exact_live_completed_generation(string fault)
    {
        var source = Source(); source = source with { ActiveTaskId = source.TaskId }; var dependency = Dependency(source);
        var current = fault switch
        {
            "deleted" => source with { IsDeleted = 1 }, "cleanup" => source with { CleanupPending = 1 },
            "current" => source with { IsCurrent = 0 }, "task" => source with { ActiveTaskId = Guid.CreateVersion7() },
            "receipt" => source with { ActiveReceiptHash = new string('E',64) },
            "manifest" => source with { ActiveManifestHash = new string('E',64) },
            "plan" => source with { ActivePlanHash = new string('E',64) },
            "version" => source with { DocumentVersion = 3 }, _ => source
        };
        Assert.AreEqual(fault == "live", AiKnowledgeAnswerLineageCodec.Matches(dependency, current));
    }
}
