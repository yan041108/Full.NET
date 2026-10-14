using System.Data;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>验证原生运行、租约与检查点投影，避免 JIT 反射物化掩盖原生闭包缺失。</summary>
[TestClass]
public sealed class AiAgentRunRowReaderTests
{
    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void Full_run_preserves_binding_fencing_and_nullable_provider_values(bool mysql, bool absent)
    {
        using var table = new DataTable(); var now = DateTime.UtcNow;
        var names = new[] { "Id", "TenantId", "ActorUserId", "SessionId", "AuthorizationBindingId" };
        var ids = names.Select(_ => Guid.CreateVersion7()).ToArray();
        foreach (var name in names) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "ScopeKey", "SessionKind", "DefinitionKey", "SecurityStamp", "ActorScope", "EffectiveScope", "StatusKey", "BudgetJson", "LeaseOwner", "TraceParent" }) table.Columns.Add(name, typeof(string));
        foreach (var name in new[] { "Version", "LeaseEpoch", "DefinitionVersion" }) table.Columns.Add(name, mysql ? typeof(long) : typeof(decimal));
        foreach (var name in new[] { "DeadlineAtUtc", "LeaseExpiresAtUtc", "CreatedAtUtc", "UpdatedAtUtc" }) table.Columns.Add(name, mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        var data = table.NewRow(); for (var i = 0; i < names.Length; i++) data[names[i]] = ids[i];
        data["ScopeKey"] = "host"; data["SessionKind"] = "refresh"; data["DefinitionKey"] = "fullnet-single-text-v1";
        data["SecurityStamp"] = "stamp"; data["ActorScope"] = "actor"; data["EffectiveScope"] = "effective";
        data["StatusKey"] = "running"; data["BudgetJson"] = "{\"PromptBinding\":{\"Epoch\":3}}"; data["LeaseOwner"] = "worker";
        data["TraceParent"] = absent ? DBNull.Value : "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01";
        data["Version"] = 4_000_000_001L; data["LeaseEpoch"] = 4_000_000_002L; data["DefinitionVersion"] = 2;
        foreach (var name in new[] { "DeadlineAtUtc", "LeaseExpiresAtUtc", "CreatedAtUtc", "UpdatedAtUtc" }) data[name] = mysql ? (object)now : new DateTimeOffset(now);
        if (absent) foreach (var name in new[] { "TenantId", "LeaseOwner", "LeaseExpiresAtUtc" }) data[name] = DBNull.Value;
        table.Rows.Add(data); using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var row = AiBudgetRowReaders.ReadAgentRun(reader);
        Assert.AreEqual(ids[0], row.Id); Assert.AreEqual(absent ? null : (Guid?)ids[1], row.TenantId);
        Assert.AreEqual(ids[2], row.ActorUserId); Assert.AreEqual(ids[3], row.SessionId); Assert.AreEqual(ids[4], row.AuthorizationBindingId);
        Assert.AreEqual("host", row.ScopeKey); Assert.AreEqual("refresh", row.SessionKind); Assert.AreEqual("fullnet-single-text-v1", row.DefinitionKey);
        Assert.AreEqual("stamp", row.SecurityStamp); Assert.AreEqual("actor", row.ActorScope); Assert.AreEqual("effective", row.EffectiveScope);
        Assert.AreEqual("running", row.StatusKey); Assert.AreEqual(data["BudgetJson"], row.BudgetJson);
        Assert.AreEqual(absent ? null : (string)data["TraceParent"], row.TraceParent);
        Assert.AreEqual(4_000_000_001L, row.Version); Assert.AreEqual(4_000_000_002L, row.LeaseEpoch); Assert.AreEqual(2, row.DefinitionVersion);
        Assert.AreEqual(absent ? null : "worker", row.LeaseOwner); Assert.AreEqual(absent ? null : (DateTimeOffset?)new DateTimeOffset(now), row.LeaseExpiresAtUtc);
        Assert.AreEqual(new DateTimeOffset(now), row.DeadlineAtUtc); Assert.AreEqual(new DateTimeOffset(now), row.CreatedAtUtc); Assert.AreEqual(new DateTimeOffset(now), row.UpdatedAtUtc);
    }

    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void Lease_projection_reads_reordered_four_columns_without_full_run_fields(bool mysql, bool absent)
    {
        using var table = new DataTable(); var id = Guid.CreateVersion7(); var now = DateTime.UtcNow;
        table.Columns.Add("LeaseExpiresAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        table.Columns.Add("LeaseEpoch", typeof(long)); table.Columns.Add("Id", typeof(Guid)); table.Columns.Add("Version", typeof(long));
        table.Rows.Add(absent ? DBNull.Value : mysql ? (object)now : new DateTimeOffset(now), 4_000_000_002L, id, 4_000_000_001L);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadAgentRun(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual(4_000_000_001L, row.Version); Assert.AreEqual(4_000_000_002L, row.LeaseEpoch);
        Assert.AreEqual(absent ? null : (DateTimeOffset?)new DateTimeOffset(now), row.LeaseExpiresAtUtc);
        Assert.AreEqual(string.Empty, row.ScopeKey); Assert.AreEqual(string.Empty, row.BudgetJson);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public void Checkpoint_projection_preserves_ciphertext_checksum_and_provider_integer_shapes(bool mysql)
    {
        using var table = new DataTable(); var id = Guid.CreateVersion7(); var runId = Guid.CreateVersion7();
        foreach (var name in new[] { "CheckpointId", "RunId" }) table.Columns.Add(name, typeof(Guid));
        foreach (var name in new[] { "Sequence", "FormatVersion", "DefinitionVersion" }) table.Columns.Add(name, mysql ? typeof(long) : typeof(decimal));
        foreach (var name in new[] { "FrameworkVersion", "PayloadProtected", "Checksum" }) table.Columns.Add(name, typeof(string));
        table.Rows.Add(id, runId, 4_000_000_001L, 2, 3, "framework", "ciphertext", "checksum");
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read()); var row = AiBudgetRowReaders.ReadAgentCheckpoint(reader);
        Assert.AreEqual(id, row.CheckpointId); Assert.AreEqual(runId, row.RunId); Assert.AreEqual(4_000_000_001L, row.Sequence);
        Assert.AreEqual(2, row.FormatVersion); Assert.AreEqual(3, row.DefinitionVersion); Assert.AreEqual("framework", row.FrameworkVersion);
        Assert.AreEqual("ciphertext", row.PayloadProtected); Assert.AreEqual("checksum", row.Checksum);
    }
}
