using System.Text.Json.Nodes;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

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
            case "version": json["version"] = 2; break;
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
