using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试分片：AiKnowledgeIndexDocumentActivation。</summary>
public sealed partial class AiKnowledgeIndexDocumentActivationTests
{
    [TestMethod]
    public async Task Outer_read_deadline_waits_for_docx_proof_transaction_before_returning()
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); var row = ReadRow(f);
        f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AiKnowledgeIndexVectorRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { entered.TrySetResult(); return release.Task; });
        using var stop = new CancellationTokenSource();
        var pending = AiKnowledgeIndexPdfAuthorityBoundary.RunAsync(ct => f.Store.IsReadyForReadAsync(row, new(), ct), stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); stop.Cancel();
            // 超过外层 IO 清理预算，事务仍只能由原调用者等待结束。
            await Task.Delay(TimeSpan.FromSeconds(6)); Assert.IsFalse(pending.IsCompleted);
            release.TrySetResult(f.Payloads[0]);
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending); Assert.AreEqual(1, f.Rollbacks);
        }
        finally { release.TrySetResult(f.Payloads[0]); try { await pending; } catch (IOException) { } catch (OperationCanceledException) { } }
    }

    private static AiKnowledgeIndexStatusRecord ReadRow(Fixture f) => new()
    {
        KnowledgeBaseId = f.Binding.KnowledgeBaseId, DocumentId = f.Binding.DocumentId, VersionId = f.Binding.VersionId,
        OwnerUserId = f.Binding.OwnerUserId, DocumentVersion = 1, KnowledgeBaseVersion = 1,
        DataClassification = "internal",
        FileId = f.Source.FileId, ContentType = f.Source.ContentType, ContentHash = f.Source.ContentHash, SizeBytes = f.Source.SizeBytes,
        TaskId = f.TaskId, ActiveTaskId = f.TaskId, ActiveLeaseToken = f.Lease, StateKey = "completed", IsCurrent = 1, HasDerivedData = 1,
        ActiveManifestHash = f.Pointer!.ManifestHash, ActivePlanHash = f.Pointer.PlanHash, ActiveReceiptHash = f.Pointer.ReceiptHash,
        ModelConfigId = f.Source.ModelConfigId, ModelConfigVersion = 1, ProviderKey = f.Source.ModelProviderKey, ModelId = f.Source.ModelId
    };

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Current_docx_reader_does_not_need_original_writer_online(bool tenant)
    {
        var f = new Fixture(tenant); Assert.IsTrue(await f.Activate()); var row = ReadRow(f); var calls = f.Authorizations;
        f.Denied = true; f.Clock.UtcNow.Returns(f.Now.AddHours(1));
        f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        Assert.IsTrue(await f.Store.IsReadyForReadAsync(row, new(), default));
        Assert.AreEqual(calls, f.Authorizations); Assert.AreEqual(2, f.Writes);
        Assert.IsFalse(await f.Store.IsActiveAsync(f.Binding, f.TaskId, f.Lease, default));
    }

    [TestMethod] [DataRow("visibility")] [DataRow("payload")] [DataRow("pointer")] [DataRow("deleted")]
    public async Task Current_docx_read_refuses_incomplete_or_changed_authority(string fault)
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); var row = ReadRow(f);
        f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(fault == "visibility" ? null : row);
        if (fault == "payload") f.Payloads[0] = f.Payloads[0] with { Payload = new byte[8] };
        if (fault == "pointer") f.Pointer = f.Pointer! with { ManifestHash = new string('E', 64) };
        if (fault == "deleted") row = row with { IsDeleted = 1 };
        Assert.IsFalse(await f.Store.IsReadyForReadAsync(row, new(), default)); Assert.AreEqual(2, f.Writes);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Docx_retired_cleanup_runs_in_proof_transaction_and_false_rolls_back(bool accepted)
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate()); var row = ReadRow(f); var pointer = f.Pointer;
        f.Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexStatusRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        var invoked = false;
        Assert.AreEqual(accepted, await f.Store.WithReadyGenerationAsync(row, new(), _ =>
        {
            Assert.IsTrue(f.Transactions.HasTransaction); invoked = true; f.Pointer = null;
            return Task.FromResult(accepted);
        }, default));
        Assert.IsTrue(invoked); Assert.AreEqual(accepted ? null : pointer, f.Pointer);
        Assert.AreEqual(accepted ? 0 : 1, f.Rollbacks);
    }
}
