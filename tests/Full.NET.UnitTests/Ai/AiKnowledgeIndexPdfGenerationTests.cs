using System.Security.Cryptography;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod] [DataRow("activation", false)] [DataRow("generation", false)] [DataRow("completed", false)]
    [DataRow("activation", true)] [DataRow("generation", true)] [DataRow("completed", true)]
    public async Task Pdf_authority_ignoring_cancellation_cannot_hold_generation_or_completed_recovery_forever(string route, bool synchronous)
    {
        var f = new Fixture(false);
        if (route == "completed") Assert.IsTrue(await f.Activate());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeAuthorization = _ =>
        {
            started.TrySetResult();
            if (synchronous) { release.Task.GetAwaiter().GetResult(); return Task.CompletedTask; }
            return release.Task;
        };
        using var cancellation = new CancellationTokenSource();
        var pending = Task.Run(() => route == "activation" ? f.Activate(cancellation.Token)
            : route == "generation" ? f.Consumer.ActivateGenerationAsync(f.Binding, f.TaskId, f.Lease, cancellation.Token)
            : f.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, cancellation.Token));
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
            var rejected = false;
            try { await pending.WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (Exception exception)
            {
                // 清理已确认则传播取消，未确认则报告 IO 拒绝；工作等待本身不能再次超时。
                Assert.IsTrue(exception is IOException or OperationCanceledException, exception.ToString()); rejected = true;
            }
            Assert.IsTrue(rejected);
        }
        finally { release.TrySetResult(); }
        Assert.AreEqual(route == "completed" ? 2 : 0, f.Writes);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Whole_pdf_consumer_recovers_all_batches_and_completed_lease_without_execution(bool tenant)
    {
        var f = new Fixture(tenant);
        Assert.IsTrue(await f.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.IsTrue(f.Completed); Assert.IsNotNull(f.Pointer); Assert.AreEqual(2, f.Writes);
        f.Clock.UtcNow.Returns(f.Now.AddMinutes(1));
        Assert.IsTrue(await f.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.AreEqual(2, f.Writes);
    }

    [TestMethod] [DataRow("source")] [DataRow("permission")] [DataRow("manifest")] [DataRow("receipt")]
    public async Task Whole_pdf_consumer_never_activates_incomplete_or_unauthorized_generation(string fault)
    {
        var f = new Fixture(false);
        if (fault == "source") f.SourceDenied = true;
        else if (fault == "permission") f.Denied = true;
        else if (fault == "manifest") f.ManifestRow = f.ManifestRow with { Payload = new byte[8] };
        else f.Receipts = f.Receipts.Take(1).ToArray();
        Assert.IsFalse(await f.Consumer.ActivateGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.IsFalse(f.Completed); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("cancellation")] [DataRow("ambient")]
    public async Task Whole_pdf_consumer_propagates_cancellation_and_rejects_ambient_transaction(string fault)
    {
        var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        if (fault == "cancellation") cancel.Cancel(); else f.Transactions.HasTransaction.Returns(true);
        if (fault == "cancellation") await Assert.ThrowsAsync<OperationCanceledException>(() => f.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, cancel.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => f.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, cancel.Token));
        Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("endpoint")] [DataRow("organization")] [DataRow("classification")]
    public async Task Pdf_activation_rejects_same_version_binding_drift_during_locked_authority(string fault)
    {
        var f = new Fixture(false);
        f.AtAuthority = () => f.Source = ChangedSource(f.Source, fault);
        Assert.IsFalse(await f.Activate()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("bytes")] [DataRow("rehash")] [DataRow("missing")] [DataRow("request")]
    public async Task Pdf_activation_revalidates_actual_earlier_batch_and_full_request_binding(string fault)
    {
        var f = new Fixture(false); f.AtPayload = () =>
        {
            if (f.PayloadReads != 1) return;
            var row = f.Payloads[0];
            if (fault == "missing") { f.Payloads = f.Payloads.Skip(1).ToArray(); return; }
            f.Payloads[0] = fault == "request" ? row with { RequestHash = new string('0', 64) }
                : row with { Payload = new byte[8], PayloadHash = fault == "rehash" ? Convert.ToHexString(SHA256.HashData(new byte[8])) : row.PayloadHash };
        };
        Assert.IsFalse(await f.Activate()); Assert.AreEqual(0, f.Writes);
    }

    [TestMethod] [DataRow("endpoint")] [DataRow("organization")] [DataRow("payload")]
    public async Task Pdf_completed_retry_rechecks_current_model_fingerprint_and_actual_payload(string fault)
    {
        var f = new Fixture(false); Assert.IsTrue(await f.Activate());
        if (fault == "payload") f.Payloads[0] = f.Payloads[0] with { Payload = new byte[8] };
        else f.Source = ChangedSource(f.Source, fault);
        Assert.IsFalse(await f.Consumer.ExecuteGenerationAsync(f.Binding, f.TaskId, f.Lease, default));
        Assert.AreEqual(2, f.Writes);
    }

    private static AiKnowledgeIndexDispatchRecord ChangedSource(AiKnowledgeIndexDispatchRecord row, string fault) => new()
    {
        TaskId = row.TaskId, DocumentId = row.DocumentId, VersionId = row.VersionId, DocumentVersion = row.DocumentVersion,
        KnowledgeBaseVersion = row.KnowledgeBaseVersion, ModelConfigId = row.ModelConfigId, ModelConfigVersion = row.ModelConfigVersion,
        LeaseToken = row.LeaseToken, LeaseExpiresAtUtc = row.LeaseExpiresAtUtc, FileId = row.FileId, ContentHash = row.ContentHash,
        ContentType = row.ContentType, SizeBytes = row.SizeBytes, ModelProviderKey = row.ModelProviderKey, ModelId = row.ModelId,
        ModelEndpointBaseUrl = fault == "endpoint" ? "https://other.example/v1" : row.ModelEndpointBaseUrl,
        ModelOrganizationId = fault == "organization" ? "other" : row.ModelOrganizationId,
        DataClassification = fault == "classification" ? "restricted" : row.DataClassification
    };
}
