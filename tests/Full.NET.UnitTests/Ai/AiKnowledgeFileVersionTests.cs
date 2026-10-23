using System.Data;
using System.Text;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>文件上传预算由真实流验证；原生物化读取固定字段和可空恢复状态。</summary>
[TestClass]
public sealed class AiKnowledgeFileVersionTests
{
    /// <summary>数据边界包装唯一键冲突后，复用已提交的完全匹配操作，而非抛出 500。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Concurrent_retry_key_recovers_classified_unique_conflict(bool tenantScope)
    {
        var tenant = new CurrentTenantAccessor();
        if (tenantScope) tenant.SetTenant(new TenantContext(Guid.NewGuid(), "acme", "Acme")); else tenant.SetHost();
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("原文件"));
        using var payload = (await AiKnowledgeUploadBuffer.ReadAsync(stream, "text/plain", CancellationToken.None)).Value!;
        var existing = new AiKnowledgeImportRecord { Id = Guid.NewGuid(), ExpectedDocumentVersion = 1,
            OriginalFileName = "source.txt", ContentType = "text/plain", ContentHash = payload.Hash, SizeBytes = payload.Size };
        query.QuerySingleOrDefaultAsync<AiKnowledgeImportRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((AiKnowledgeImportRecord?)null, existing);
        query.QuerySingleOrDefaultAsync<AiKnowledgeDocumentRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new AiKnowledgeDocumentRecord { Version = 1 });
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<int>(new DataCommandException(DataCommandFailureKind.UniqueConstraint, new IOException("Unique key collision"))));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.CreateVersion7());
        var store = new AiKnowledgeImportStore(query, command, Substitute.For<ICommandTransaction>(), tenant, Substitute.For<IClock>(), ids, Options.Create(new DatabaseOptions()), Substitute.For<IOutboxWriter>());
        var result = await store.StartAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "source.txt", "text/plain", payload, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(existing.Id, result.Value!.Id);
    }
    [TestMethod]
    [DataRow("source.txt", "text/plain", true)]
    [DataRow("source.md", "text/markdown", true)]
    [DataRow("source.pdf", "application/pdf", true)]
    [DataRow("source.pdf", "text/plain", false)]
    [DataRow("../source.txt", "text/plain", false)]
    [DataRow("C:source.txt", "text/plain", false)]
    [DataRow("source.html", "text/html", true)]
    [DataRow("source\n.txt", "text/plain", false)]
    public void File_policy_requires_safe_matching_name_and_type(string name, string type, bool valid) =>
        Assert.AreEqual(valid, AiKnowledgeFilePolicy.Validate(name, type, 1, Guid.NewGuid(), 1) is null);

    [TestMethod]
    [DataRow("empty")]
    [DataRow("utf8")]
    [DataRow("pdf")]
    [DataRow("limit")]
    public async Task Actual_stream_rejects_empty_invalid_encoding_signature_and_overflow(string invalid)
    {
        byte[] bytes = invalid switch { "empty" => [], "utf8" => [0xff, 0xfe], "pdf" => Encoding.UTF8.GetBytes("PDF"), _ => new byte[AiKnowledgeFilePolicy.MaxBytes + 1] };
        using var source = new MemoryStream(bytes);
        var result = await AiKnowledgeUploadBuffer.ReadAsync(source, invalid == "pdf" ? "application/pdf" : "text/plain", CancellationToken.None);
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Native_import_reader_preserves_null_and_persisted_lease(bool mysql)
    {
        using var table = new DataTable();
        foreach (var column in new[] { "Id", "DocumentId", "VersionId", "RequestKey", "ActorUserId", "FileId", "LeaseToken" }) table.Columns.Add(column, typeof(Guid));
        table.Columns.Add("ExpectedDocumentVersion", typeof(int));
        foreach (var column in new[] { "OriginalFileName", "ContentType", "ParsingPolicyKey", "ContentHash", "StateKey", "FailureCode" }) table.Columns.Add(column, typeof(string));
        table.Columns.Add("SizeBytes", typeof(long));
        foreach (var column in new[] { "CreatedAtUtc", "UpdatedAtUtc", "LeaseExpiresAtUtc" }) table.Columns.Add(column, mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        var row = table.NewRow();
        foreach (var column in new[] { "Id", "DocumentId", "VersionId", "RequestKey", "ActorUserId" }) row[column] = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();
        row["FileId"] = mysql ? fileId : DBNull.Value;
        row["LeaseToken"] = mysql ? Guid.CreateVersion7() : DBNull.Value;
        row["ExpectedDocumentVersion"] = 7;
        row["OriginalFileName"] = "source.txt";
        row["ContentType"] = "text/plain";
        row["ParsingPolicyKey"] = "default";
        row["ContentHash"] = new string('A', 64);
        row["SizeBytes"] = 4L;
        row["StateKey"] = "pending_confirmation";
        row["FailureCode"] = mysql ? "files.test" : DBNull.Value;
        var now = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        row["CreatedAtUtc"] = mysql ? now.UtcDateTime : (object)now;
        row["UpdatedAtUtc"] = mysql ? now.UtcDateTime : (object)now;
        row["LeaseExpiresAtUtc"] = mysql ? now.UtcDateTime : DBNull.Value;
        table.Rows.Add(row);
        using var reader = table.CreateDataReader();
        Assert.IsTrue(reader.Read());
        var record = AiBudgetRowReaders.ReadKnowledgeImport(reader);
        Assert.AreEqual(7, record.ExpectedDocumentVersion);
        Assert.AreEqual("default", record.ParsingPolicyKey);
        Assert.AreEqual(mysql ? fileId : null, record.FileId);
        Assert.AreEqual(mysql ? now : null, record.LeaseExpiresAtUtc);
        Assert.AreEqual(now, record.CreatedAtUtc);
        Assert.AreEqual(mysql ? "files.test" : null, record.FailureCode);
    }
}
