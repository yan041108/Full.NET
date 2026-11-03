using System.Data;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>新校对状态的静态物化覆盖双库时间、整数、二进制载荷及空父修订。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionRowReaderTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Activation_metadata_projection_supplies_every_column_required_by_native_version_reader(bool mysql)
    {
        var now = DateTime.UtcNow;
        var values = new Dictionary<string, object?>
        {
            ["Id"] = Guid.CreateVersion7(), ["DocumentId"] = Guid.CreateVersion7(), ["FileId"] = Guid.CreateVersion7(),
            ["VersionNumber"] = mysql ? (object)2L : 2, ["OriginalFileName"] = "source.txt", ["ContentType"] = "text/plain", ["ParsingPolicyKey"] = "",
            ["ContentHash"] = new string('a', 64), ["SizeBytes"] = 42L, ["UploadedByUserId"] = Guid.CreateVersion7(),
            ["CreatedAtUtc"] = mysql ? (object)now : new DateTimeOffset(now), ["ProcessingStatus"] = "pending_processing"
        };
        foreach (var tenant in new[] { false, true })
        {
            var sql = AiKnowledgeCorrectionActivationSql.MetadataFor(tenant).Text;
            var projection = sql[7..sql.IndexOf(" FROM ", StringComparison.Ordinal)].Split(',');
            var fields = projection.Select(column => System.Text.RegularExpressions.Regex.Match(column, @"(?:\bAS\s+|\.)(\w+)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value).ToArray();
            Assert.IsTrue(fields.All(name => values.ContainsKey(name)));
            using var table = Table(fields.Select(name => (name, values[name])).ToArray());
            using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadKnowledgeVersion(reader);
            Assert.AreEqual(values["Id"], row.Id); Assert.AreEqual("pending_processing", row.ProcessingStatus); Assert.AreEqual(42L, row.SizeBytes);
        }
    }
    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void Correction_rows_preserve_distinct_identities_payloads_and_provider_values(bool mysql, bool initial)
    {
        var sourceId = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7(); var revisionId = Guid.CreateVersion7();
        var requestKey = Guid.CreateVersion7(); var actor = Guid.CreateVersion7(); var parentId = initial ? (Guid?)null : Guid.CreateVersion7();
        var now = DateTime.UtcNow; byte[] original = [1, 2, 3]; byte[] payload = [4, 5];
        using var source = Table(("Id", sourceId), ("VersionId", versionId), ("RequestKey", requestKey), ("ActorUserId", actor),
            ("RequestHash", "request"), ("FileHash", "file"), ("ParserRuntimeId", "runtime"), ("SourceHash", "source"),
            ("InitialPayloadHash", "initial"), ("OriginalPayload", original), ("InitialPayload", payload), ("CreatedAtUtc", mysql ? (object)now : new DateTimeOffset(now)));
        using var sr = source.CreateDataReader(); Assert.IsTrue(sr.Read()); var s = AiBudgetRowReaders.ReadCorrectionSource(sr);
        Assert.AreEqual(sourceId, s.Id); Assert.AreEqual(versionId, s.VersionId); Assert.AreEqual(requestKey, s.RequestKey); Assert.AreEqual(actor, s.ActorUserId);
        Assert.AreEqual("request", s.RequestHash); Assert.AreEqual("file", s.FileHash); Assert.AreEqual("runtime", s.ParserRuntimeId);
        Assert.AreEqual("source", s.SourceHash); Assert.AreEqual("initial", s.InitialPayloadHash);
        CollectionAssert.AreEqual(original, s.OriginalPayload); CollectionAssert.AreEqual(payload, s.InitialPayload); Assert.AreEqual(new DateTimeOffset(now), s.CreatedAtUtc);
        using var revision = Table(("Id", revisionId), ("VersionId", versionId), ("SourceId", sourceId), ("ParentId", parentId),
            ("RequestKey", requestKey), ("RequestHash", "request"), ("Payload", payload), ("PayloadHash", "payload"), ("ReasonText", "校对原因"),
            ("ActorUserId", actor), ("CreatedAtUtc", mysql ? (object)now : new DateTimeOffset(now)),
            ("DocumentVersion", mysql ? (object)7L : 7), ("HeadVersion", mysql ? (object)3L : 3));
        using var rr = revision.CreateDataReader(); Assert.IsTrue(rr.Read()); var r = AiBudgetRowReaders.ReadCorrectionRevision(rr);
        Assert.AreEqual(revisionId, r.Id); Assert.AreEqual(versionId, r.VersionId); Assert.AreEqual(sourceId, r.SourceId); Assert.AreEqual(parentId, r.ParentId);
        Assert.AreEqual(requestKey, r.RequestKey); Assert.AreEqual("request", r.RequestHash); Assert.AreEqual("payload", r.PayloadHash);
        CollectionAssert.AreEqual(payload, r.Payload); Assert.AreEqual("校对原因", r.ReasonText); Assert.AreEqual(actor, r.ActorUserId);
        Assert.AreEqual(7, r.DocumentVersion); Assert.AreEqual(3, r.HeadVersion); Assert.AreEqual(new DateTimeOffset(now), r.CreatedAtUtc);
        using var head = Table(("Id", versionId), ("SourceId", sourceId), ("RevisionId", initial ? null : revisionId), ("Version", mysql ? (object)3L : 3));
        using var hr = head.CreateDataReader(); Assert.IsTrue(hr.Read()); var h = AiBudgetRowReaders.ReadCorrectionHead(hr);
        Assert.AreEqual(versionId, h.Id); Assert.AreEqual(sourceId, h.SourceId); Assert.AreEqual(initial ? null : (Guid?)revisionId, h.RevisionId); Assert.AreEqual(3, h.Version);
    }
    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void Preparation_static_rows_preserve_nullable_leases_and_never_require_secrets_for_global_candidates(bool mysql, bool nullable)
    {
        var id = Guid.CreateVersion7(); var version = Guid.CreateVersion7(); var knowledge = Guid.CreateVersion7(); var document = Guid.CreateVersion7();
        var owner = Guid.CreateVersion7(); var tenant = nullable ? (Guid?)null : Guid.CreateVersion7(); var lease = nullable ? (Guid?)null : Guid.CreateVersion7();
        var now = DateTime.UtcNow; var source = nullable ? (Guid?)null : Guid.CreateVersion7();
        using var table = Table(("Id", id), ("VersionId", version), ("KnowledgeBaseId", knowledge), ("DocumentId", document), ("OwnerUserId", owner), ("TenantId", tenant),
            ("DocumentVersion", mysql ? (object)2L : 2), ("ContentType", "text/plain"), ("IsCurrent", mysql ? (object)1L : 1),
            ("RequestKey", Guid.CreateVersion7()), ("RequestHash", "request"), ("ActorUserId", owner), ("SessionId", Guid.CreateVersion7()),
            ("SessionKind", "refresh"), ("SecurityStamp", "private-stamp"), ("ActorScope", "host"), ("EffectiveScope", "host"), ("StateKey", "running"),
            ("AttemptCount", mysql ? (object)1L : 1), ("LeaseToken", lease), ("LeaseExpiresAtUtc", nullable ? null : mysql ? (object)now : new DateTimeOffset(now)),
            ("ParserRuntimeId", nullable ? null : "runtime"), ("SourceId", source), ("FailureCode", nullable ? null : "failure"),
            ("CreatedAtUtc", mysql ? (object)now : new DateTimeOffset(now)), ("UpdatedAtUtc", mysql ? (object)now : new DateTimeOffset(now)));
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadCorrectionPreparation(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual(version, row.VersionId); Assert.AreEqual(tenant, row.TenantId); Assert.AreEqual(owner, row.ActorUserId);
        Assert.AreEqual("private-stamp", row.SecurityStamp); Assert.AreEqual(lease, row.LeaseToken); Assert.AreEqual(source, row.SourceId);
        Assert.AreEqual(nullable ? null : (DateTimeOffset?)new DateTimeOffset(now), row.LeaseExpiresAtUtc);
        Assert.AreEqual(nullable ? null : "runtime", row.ParserRuntimeId); Assert.AreEqual(new DateTimeOffset(now), row.UpdatedAtUtc);
        using var candidates = Table(("Id", id), ("VersionId", version), ("KnowledgeBaseId", knowledge), ("DocumentId", document), ("OwnerUserId", owner), ("TenantId", tenant),
            ("DocumentVersion", mysql ? (object)2L : 2), ("ContentType", "text/plain"));
        using var scan = candidates.CreateDataReader(); Assert.IsTrue(scan.Read()); var candidate = AiBudgetRowReaders.ReadCorrectionPreparationCandidate(scan);
        Assert.AreEqual(id, candidate.Id); Assert.AreEqual(tenant, candidate.TenantId); Assert.AreEqual(2, candidate.DocumentVersion);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Revision_directory_static_reader_needs_only_metadata_and_preserves_nullable_parent(bool mysql)
    {
        var id = Guid.CreateVersion7(); var version = Guid.CreateVersion7(); var source = Guid.CreateVersion7(); var now = DateTime.UtcNow;
        using var table = Table(("Id", id), ("VersionId", version), ("SourceId", source), ("ParentId", null),
            ("HeadVersion", mysql ? (object)9L : 9), ("DocumentVersion", mysql ? (object)11L : 11), ("PayloadHash", "hash"), ("ReasonText", "核对"),
            ("CreatedAtUtc", mysql ? (object)now : new DateTimeOffset(now)));
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadCorrectionRevisionSummary(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual(version, row.VersionId); Assert.AreEqual(source, row.SourceId); Assert.IsNull(row.ParentId);
        Assert.AreEqual(9, row.HeadVersion); Assert.AreEqual(11, row.DocumentVersion); Assert.AreEqual("核对", row.ReasonText); Assert.AreEqual(new DateTimeOffset(now), row.CreatedAtUtc);
    }
    private static DataTable Table(params (string Name, object? Value)[] fields)
    {
        var table = new DataTable();
        foreach (var (name, value) in fields) table.Columns.Add(name, value?.GetType() ?? typeof(Guid));
        table.Rows.Add(fields.Select(field => field.Value ?? DBNull.Value).ToArray()); return table;
    }
}
