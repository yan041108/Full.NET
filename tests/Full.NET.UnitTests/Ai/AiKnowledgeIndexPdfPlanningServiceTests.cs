using System.Security.Cryptography;
using System.Text;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>使用真实来源读取和子进程桥，等待期间的旧授权不能交付完整规划。</summary>
[TestClass]
public sealed partial class AiKnowledgeIndexPdfPlanningServiceTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Owned_files_source_produces_a_complete_page_plan_and_closes_stream(bool scoped)
    {
        using var f = new Fixture(scoped);
        var result = await f.Plan();
        Assert.IsNotNull(result); Assert.AreEqual(f.Session, result.Session);
        Assert.AreEqual(f.Source.FileId, result.Source.FileId); Assert.AreEqual(f.Hash, result.Plan.Identity.ContentHash);
        Assert.AreEqual(2, result.Plan.Identity.PageCount);
        CollectionAssert.AreEqual(new[] { 1, 2 }, result.Plan.Batches.SelectMany(b => b.Chunks).Select(c => c.PageNumber).Distinct().ToArray());
        Assert.IsTrue(f.Stream.Closed); Assert.AreEqual(scoped ? 0 : 1, f.ReferenceCalls);
        Assert.AreEqual(scoped ? 0 : 1, f.HostCalls); Assert.AreEqual(scoped ? 1 : 0, f.TenantCalls);
        Assert.AreEqual(0, f.Commands);
        Assert.AreEqual(f.ModelId, result.ModelBinding.ModelConfigId);
    }

    [TestMethod] [DataRow("ai.knowledge_bases.read")] [DataRow("ai.knowledge_documents.read")]
    [DataRow("ai.knowledge_documents.source_read")] [DataRow("ai.knowledge_documents.upload")]
    [DataRow("ai.knowledge_documents.index_enqueue")]
    public async Task Every_required_permission_prevents_opening_files(string permission)
    {
        using var f = new Fixture(false) { DeniedPermission = permission };
        Assert.IsNull(await f.Plan()); Assert.AreEqual(0, f.HostCalls); Assert.AreEqual(0, f.ReferenceCalls);
    }

    [TestMethod] [DataRow("reference", "permission")] [DataRow("reference", "session")]
    [DataRow("reference", "source")] [DataRow("reference", "model")]
    [DataRow("open", "permission")] [DataRow("open", "session")] [DataRow("open", "source")]
    [DataRow("open", "model")] [DataRow("open", "expiry")]
    [DataRow("open", "binding")]
    [DataRow("read", "permission")] [DataRow("read", "session")] [DataRow("read", "source")]
    [DataRow("read", "model")] [DataRow("read", "expiry")]
    [DataRow("close", "permission")] [DataRow("close", "session")] [DataRow("close", "source")]
    [DataRow("close", "model")] [DataRow("close", "classification")] [DataRow("close", "expiry")]
    public async Task Authority_loss_after_files_wait_never_starts_parser(string stage, string fault)
    {
        using var f = new Fixture(false);
        Action lose = () => f.Lose(fault);
        switch (stage) { case "reference": f.AfterReference = lose; break; case "open": f.AfterOpen = lose; break;
            case "read": f.Stream.AfterRead = lose; break; default: f.Stream.AfterClose = lose; break; }
        Assert.IsNull(await f.Plan()); Assert.IsFalse(File.Exists(f.Started));
        if (stage != "reference") Assert.IsTrue(f.Stream.Closed);
        if (stage == "open") Assert.AreEqual(0, f.Stream.Reads);
    }

    [TestMethod] [DataRow("permission")] [DataRow("session")] [DataRow("source")]
    [DataRow("model")] [DataRow("expiry")] [DataRow("classification")] [DataRow("binding")]
    public async Task Parser_wait_cannot_deliver_a_plan_after_revocation(string fault)
    {
        using var f = new Fixture(false, hold: true);
        var work = f.Plan(); await f.WaitStarted(); f.Lose(fault); f.Release();
        Assert.IsNull(await work); Assert.IsTrue(f.Stream.Closed);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Files_content_mime_mismatch_is_closed_without_reading(bool scoped)
    {
        using var f = new Fixture(scoped) { OpenMime = "text/plain" };
        Assert.IsNull(await f.Plan()); Assert.IsTrue(f.Stream.Closed); Assert.AreEqual(0, f.Stream.Reads);
        Assert.IsFalse(File.Exists(f.Started));
    }

    [TestMethod] [DataRow("id")] [DataRow("size")] [DataRow("hash")] [DataRow("missing")]
    public async Task Host_reference_must_match_frozen_source_before_open(string fault)
    {
        using var f = new Fixture(false);
        f.Reference = fault switch { "id" => f.Reference! with { FileId = Guid.CreateVersion7() },
            "size" => f.Reference! with { SizeBytes = f.Bytes.Length + 1 },
            "hash" => f.Reference! with { ContentHash = new string('b', 64) }, _ => null };
        Assert.IsNull(await f.Plan()); Assert.AreEqual(0, f.HostCalls);
    }

    [TestMethod] [DataRow("hash")] [DataRow("short")] [DataRow("long")]
    public async Task Actual_bytes_are_checked_before_parser_start(string fault)
    {
        using var f = new Fixture(false);
        var bytes = fault switch { "short" => f.Bytes[..^1], "long" => [.. f.Bytes, (byte)'x'], _ => Encoding.UTF8.GetBytes(new string('x', f.Bytes.Length)) };
        f.Stream = new(bytes);
        Assert.IsNull(await f.Plan()); Assert.IsTrue(f.Stream.Closed); Assert.IsFalse(File.Exists(f.Started));
    }

    [TestMethod] [DataRow("mime")] [DataRow("oversize")] [DataRow("disabled")]
    [DataRow("owner")] [DataRow("tenant")] [DataRow("approval")]
    [DataRow("actor-user")] [DataRow("actor-tenant")] [DataRow("actor-session")]
    public async Task Invalid_source_or_approval_never_opens_files(string fault)
    {
        using var f = new Fixture(false); f.InitialFault = fault;
        Assert.IsNull(await f.Plan()); Assert.AreEqual(0, f.HostCalls); Assert.AreEqual(0, f.ReferenceCalls);
    }

    [TestMethod] [DataRow("model-read")] [DataRow("final-authorize")]
    public async Task Authority_query_wait_rechecks_session_and_source(string stage)
    {
        using var f = new Fixture(false);
        if (stage == "model-read") f.AfterModel = () => f.Lose("session");
        else f.AfterAuthorize = () => { if (f.AuthCalls == 10) f.Lose("source"); };
        Assert.IsNull(await f.Plan()); Assert.AreEqual(0, f.HostCalls);
    }

    [TestMethod]
    public async Task Invalid_parser_output_has_no_partial_plan()
    {
        using var f = new Fixture(false, malformed: true);
        Assert.IsNull(await f.Plan()); Assert.IsTrue(f.Stream.Closed); Assert.IsTrue(File.Exists(f.Started));
    }

    [TestMethod] [DataRow("open")] [DataRow("read")] [DataRow("close")]
    public async Task Cancellation_during_owned_source_phase_closes_stream(string stage)
    {
        using var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        if (stage == "open") f.AfterOpen = cancel.Cancel;
        else if (stage == "read") f.Stream.AfterRead = cancel.Cancel;
        else f.Stream.AfterClose = cancel.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Plan(cancel.Token));
        Assert.IsTrue(f.Stream.Closed); Assert.IsFalse(File.Exists(f.Started));
    }

    [TestMethod]
    public async Task Parser_cancellation_waits_for_owned_child_cleanup()
    {
        using var f = new Fixture(false, hold: true); using var cancel = new CancellationTokenSource();
        var work = f.Plan(cancel.Token); await f.WaitStarted(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => work);
        Assert.IsTrue(File.Exists(f.Cleaned)); Assert.IsTrue(f.Stream.Closed);
    }

    [TestMethod]
    public async Task Late_open_after_cancellation_is_owned_and_closed_without_reading()
    {
        using var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        f.OpenGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = f.Plan(cancel.Token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (Volatile.Read(ref f.HostCalls) == 0) await Task.Delay(10, deadline.Token);
        cancel.Cancel(); f.OpenGate.SetResult(Result<HostFileContent>.Success(new(f.Stream, "application/pdf", "source.pdf")));
        await Assert.ThrowsAsync<OperationCanceledException>(() => work);
        Assert.IsTrue(f.Stream.Closed); Assert.AreEqual(0, f.Stream.Reads);
    }

    [TestMethod] [DataRow("open")] [DataRow("read")] [DataRow("parser")]
    public async Task Ambient_transaction_after_wait_cannot_return_plan(string stage)
    {
        using var f = new Fixture(false, hold: stage == "parser");
        Action change = () => f.State.HasTransaction.Returns(true);
        if (stage == "open") f.AfterOpen = change;
        else if (stage == "read") f.Stream.AfterRead = change;
        var work = f.Plan(); if (stage == "parser") { await f.WaitStarted(); change(); f.Release(); }
        await Assert.ThrowsAsync<InvalidOperationException>(() => work); Assert.IsTrue(f.Stream.Closed);
    }

    [TestMethod]
    public async Task Initial_transaction_and_precancellation_prevent_authorization()
    {
        using var f = new Fixture(false); f.State.HasTransaction.Returns(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Plan());
        f.State.HasTransaction.Returns(false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Plan(new(true)));
        Assert.AreEqual(0, f.AuthCalls);
    }

    [TestMethod]
    public async Task Shared_work_deadline_cancels_parser_before_its_private_limit()
    {
        using var f = new Fixture(false, hold: true, workTimeout: 1500);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Plan());
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(8));
        Assert.IsTrue(File.Exists(f.Cleaned)); Assert.IsTrue(f.Stream.Closed);
    }

    [TestMethod] [DataRow(0)] [DataRow(25001)]
    public async Task Invalid_work_budget_fails_before_authorization(int timeout)
    {
        using var f = new Fixture(false, workTimeout: timeout);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Plan()); Assert.AreEqual(0, f.AuthCalls);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Owned_stream_disposal_failure_is_infrastructure_failure(bool wrongMime)
    {
        using var f = new Fixture(false); f.Stream.CloseFailure = true;
        if (wrongMime) f.OpenMime = "text/plain";
        await Assert.ThrowsAsync<IOException>(() => f.Plan()); Assert.IsFalse(File.Exists(f.Started));
    }

    [TestMethod]
    public async Task Files_open_infrastructure_failure_propagates_without_parser_or_writes()
    {
        using var f = new Fixture(false); f.AfterOpen = () => throw new IOException("synthetic open failure");
        await Assert.ThrowsAsync<IOException>(() => f.Plan()); Assert.IsFalse(File.Exists(f.Started)); Assert.AreEqual(0, f.Commands);
    }

    [TestMethod] [DataRow("model")] [DataRow("classification")] [DataRow("organization")]
    public async Task Final_dispatch_wait_cannot_deliver_stale_model_or_classification(string fault)
    {
        using var f = new Fixture(false);
        f.AfterDispatch = () => { if (f.DispatchCalls == 12) { if (fault == "organization") f.Organization = "changed"; else f.Lose(fault); } };
        Assert.IsNull(await f.Plan());
    }

    [TestMethod] [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public async Task Files_cancellation_callbacks_cannot_block_or_fault_caller_cancel(bool scoped, bool throws)
    {
        using var f = new Fixture(scoped); using var cancel = new CancellationTokenSource();
        using var released = new ManualResetEventSlim(); var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeOpen = async ct =>
        {
            await Task.Delay(50);
            using var registration = ct.Register(() => { if (throws) throw new IOException("synthetic callback"); released.Wait(TimeSpan.FromSeconds(10)); });
            registered.TrySetResult(); await opened.Task;
        };
        var work = f.Plan(cancel.Token); await registered.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var cancelling = Task.Run(cancel.Cancel);
        var responsive = await Task.WhenAny(cancelling, Task.Delay(300)) == cancelling;
        released.Set(); opened.TrySetResult(); Exception? callerFailure = null;
        try { await cancelling; } catch (Exception error) { callerFailure = error; }
        await Assert.ThrowsAsync<OperationCanceledException>(() => work);
        Assert.IsTrue(f.Stream.Closed); Assert.AreEqual(0, f.Stream.Reads);
        Assert.IsTrue(responsive, "Files 回调不得阻塞调用者取消"); Assert.IsNull(callerFailure, "Files 回调异常不得逸出调用者取消");
    }

    [TestMethod] [DataRow("model-query")] [DataRow("authorization")] [DataRow("reference")]
    public async Task Noncooperative_authority_port_cannot_hold_the_work_boundary_forever(string port)
    {
        using var f = new Fixture(false, workTimeout: 200); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task> wait = _ => { entered.TrySetResult(); return released.Task; };
        if (port == "model-query") f.BeforeModelAsync = wait;
        else if (port == "authorization") f.BeforeAuthorizeAsync = wait;
        else f.BeforeReferenceAsync = wait;
        var work = f.Plan(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var bounded = await Task.WhenAny(work, Task.Delay(6500)) == work;
        released.TrySetResult(); Exception? failure = null; try { await work; } catch (Exception error) { failure = error; }
        Assert.IsTrue(bounded, "工作期限及独立清理预算之后必须拒绝交付"); Assert.IsInstanceOfType<IOException>(failure);
        Assert.IsFalse(File.Exists(f.Started)); Assert.AreEqual(0, f.Stream.Reads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Revalidation_returns_current_model_without_reopening_pdf(bool scoped)
    {
        using var f = new Fixture(scoped); var prepared = await f.Plan(); Assert.IsNotNull(prepared);
        var reads = f.Stream.Reads; var opens = f.HostCalls + f.TenantCalls;
        var model = await f.Service.ReadCurrentModelAsync(f.Binding, f.TaskId, f.Lease, prepared, CancellationToken.None);
        Assert.IsNotNull(model); Assert.AreEqual(f.ModelId, model.Id); Assert.AreEqual(f.Endpoint, model.EndpointBaseUrl);
        Assert.AreEqual(reads, f.Stream.Reads); Assert.AreEqual(opens, f.HostCalls + f.TenantCalls);
    }

    [TestMethod]
    [DataRow("endpoint")]
    [DataRow("organization")]
    [DataRow("classification")]
    [DataRow("permission")]
    [DataRow("source")]
    public async Task Revalidation_rejects_drift_from_prepared_authority(string drift)
    {
        using var f = new Fixture(false); var prepared = await f.Plan(); Assert.IsNotNull(prepared);
        if (drift == "endpoint") f.Endpoint += "/";
        else if (drift == "organization") f.Organization = "changed";
        else if (drift == "classification") f.Classification = "public";
        else if (drift == "permission") f.DeniedPermission = "ai.knowledge_documents.index_enqueue";
        else f.SourceDenied = true;
        Assert.IsNull(await f.Service.ReadCurrentModelAsync(f.Binding, f.TaskId, f.Lease, prepared, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Pdf_paid_batch_freezes_whole_manifest_before_reservation_and_settles(bool scoped)
    {
        using var f = new Fixture(scoped); var execution = f.Paid();
        var batch = await execution.ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, CancellationToken.None);
        Assert.IsNotNull(batch); Assert.IsNotNull(f.ManifestRow); Assert.AreEqual(2, batch.Preparation.Plan.Identity.PageCount);
        Assert.AreEqual(2, batch.Vectors.Count); Assert.AreEqual(1, f.Generator.Calls); Assert.IsTrue(f.Generator.Disposed);
        Assert.AreEqual(2, f.Generator.Inputs!.Length); Assert.IsTrue(f.Stream.Closed);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "succeeded", CancellationToken.None);
    }

    [TestMethod]
    public async Task Pdf_paid_batch_does_not_dispatch_an_existing_unknown_reservation()
    {
        using var f = new Fixture(false) { Duplicate = true }; var execution = f.Paid();
        Assert.IsNull(await execution.ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, CancellationToken.None));
        Assert.AreEqual(0, f.Generator.Calls); Assert.IsFalse(f.Budget.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "SettleAsync"));
    }

    [TestMethod]
    [DataRow("manifest")]
    [DataRow("reservation")]
    [DataRow("factory")]
    [DataRow("generation")]
    [DataRow("settlement")]
    public async Task Pdf_paid_batch_rechecks_permission_after_each_owned_wait(string stage)
    {
        using var f = new Fixture(false); Action revoke = () => f.DeniedPermission = "ai.knowledge_documents.index_enqueue";
        if (stage == "manifest") f.AfterManifest = revoke;
        else if (stage == "reservation") f.AfterReservation = revoke;
        else if (stage == "factory") f.Factory.BeforeCreate = revoke;
        else if (stage == "generation") f.Generator.BeforeReturn = _ => revoke();
        else f.AfterSettlement = revoke;
        Assert.IsNull(await f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, CancellationToken.None));
        Assert.AreEqual(stage is "generation" or "settlement" ? 1 : 0, f.Generator.Calls);
        if (stage == "manifest") Assert.IsFalse(f.Budget.ReceivedCalls().Any());
        else await f.Budget.Received(1).SettleAsync(f.OperationId, new(stage is "generation" or "settlement" ? 7 : 0, 0),
            stage == "settlement" ? "succeeded" : "cancelled", CancellationToken.None);
    }

    [TestMethod]
    [DataRow("endpoint")]
    [DataRow("organization")]
    [DataRow("classification")]
    public async Task Pdf_paid_batch_rejects_same_version_destination_drift_during_client_creation(string drift)
    {
        using var f = new Fixture(false);
        f.Factory.BeforeCreate = () => { if (drift == "endpoint") f.Endpoint += "/"; else if (drift == "organization") f.Organization = "changed"; else f.Classification = "public"; };
        Assert.IsNull(await f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, CancellationToken.None));
        Assert.AreEqual(0, f.Generator.Calls); Assert.IsTrue(f.Generator.Disposed);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(0, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    [DataRow("count")]
    [DataRow("dimensions")]
    [DataRow("nonfinite")]
    [DataRow("zero")]
    public async Task Pdf_paid_batch_invalid_vectors_preserve_known_usage(string invalid)
    {
        using var f = new Fixture(false); f.Generator.Invalid = invalid;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, CancellationToken.None));
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "failed", CancellationToken.None);
        Assert.IsTrue(f.Generator.Disposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Pdf_paid_batch_failure_before_or_after_charge_boundary_settles_once(bool started)
    {
        using var f = new Fixture(false);
        if (started) f.Generator.BeforeReturn = _ => throw new IOException("synthetic generation failure");
        else f.Factory.BeforeCreate = () => throw new IOException("synthetic client failure");
        await Assert.ThrowsAsync<IOException>(() => f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, CancellationToken.None));
        await f.Budget.Received(1).SettleAsync(f.OperationId, started ? new(null, null) : new(0, 0), "failed", CancellationToken.None);
    }

    [TestMethod]
    public async Task Pdf_paid_batch_does_not_return_vectors_if_settlement_fails()
    {
        using var f = new Fixture(false); f.AfterSettlement = () => throw new IOException("synthetic settlement failure");
        await Assert.ThrowsAsync<IOException>(() => f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, CancellationToken.None));
        Assert.AreEqual(1, f.Generator.Calls); Assert.IsTrue(f.Generator.Disposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Pdf_persistence_recovers_current_batch_without_reopening_or_charging(bool scoped)
    {
        using var f = new Fixture(scoped); var persistence = f.Persistence();
        Assert.IsTrue(await persistence.ExecuteBatchAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        Assert.IsNotNull(f.VectorRow); var reads = f.Stream.Reads;
        Assert.IsTrue(await persistence.ExecuteBatchAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        Assert.AreEqual(reads, f.Stream.Reads); Assert.AreEqual(1, f.Generator.Calls);
        await f.Budget.Received(1).ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>());
        await f.Budget.Received(1).SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), "succeeded", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Pdf_persistence_missing_vectors_do_not_reissue_a_settled_operation()
    {
        using var f = new Fixture(false); var persistence = f.Persistence();
        Assert.IsTrue(await persistence.ExecuteBatchAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        f.VectorRow = null; f.Duplicate = true; f.Stream = new(f.Bytes);
        Assert.IsFalse(await persistence.ExecuteBatchAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        Assert.AreEqual(1, f.Generator.Calls);
        await f.Budget.Received(1).SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("permission")]
    [DataRow("session")]
    [DataRow("source")]
    [DataRow("expiry")]
    public async Task Pdf_persistence_recovery_requires_current_authority(string fault)
    {
        using var f = new Fixture(false); var persistence = f.Persistence();
        Assert.IsTrue(await persistence.ExecuteBatchAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        f.Lose(fault);
        Assert.IsFalse(await persistence.ExecuteBatchAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        Assert.AreEqual(1, f.Generator.Calls);
    }

    [TestMethod]
    [DataRow("factory")]
    [DataRow("generation")]
    public async Task Pdf_paid_batch_cancellation_waits_for_owned_late_client_then_rejects_result(string phase)
    {
        using var f = new Fixture(false); using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Hold(CancellationToken _) { started.TrySetResult(); return release.Task; }
        if (phase == "factory") f.Factory.BeforeCreateAsync = Hold; else f.Generator.BeforeReturnAsync = Hold;
        var pending = f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(8)); cancellation.Cancel();
        await Task.Delay(100); Assert.IsFalse(pending.IsCompleted); Assert.IsFalse(f.Generator.Disposed);
        release.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.IsTrue(f.Generator.Disposed);
        await f.Budget.Received(1).SettleAsync(f.OperationId, phase == "factory" ? new(0, 0) : new(7, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    public async Task Pdf_paid_batch_blocking_provider_callback_does_not_block_caller_cancellation()
    {
        using var f = new Fixture(false); using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Generator.BeforeReturnAsync = async io =>
        { using var registration = io.Register(() => release.Task.GetAwaiter().GetResult()); started.SetResult(); await Task.Delay(Timeout.Infinite, io); };
        var pending = f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await Task.Run(cancellation.Cancel).WaitAsync(TimeSpan.FromSeconds(2));
        release.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.IsTrue(f.Generator.Disposed);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(null, null), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    public async Task Pdf_paid_batch_unconfirmed_settlement_rejects_and_observes_late_failure()
    {
        using var f = new Fixture(false); var execution = f.Paid();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => release.Task);
        try { await Assert.ThrowsAsync<IOException>(() => execution.ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default).WaitAsync(TimeSpan.FromSeconds(9))); }
        finally { release.SetException(new IOException("late settlement failure")); }
        Assert.IsTrue(f.Generator.Disposed);
        await f.Budget.Received(1).ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("factory")]
    [DataRow("generation")]
    [DataRow("disposal")]
    public async Task Pdf_paid_batch_unconfirmed_cleanup_freezes_exact_charge_boundary(string phase)
    {
        using var f = new Fixture(false); using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Hold(CancellationToken _) { started.TrySetResult(); return release.Task; }
        if (phase == "factory") f.Factory.BeforeCreateAsync = Hold;
        else if (phase == "generation") f.Generator.BeforeReturnAsync = Hold;
        else f.Generator.BeforeDispose = () => { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); };
        var pending = f.Paid().ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(8)); cancellation.Cancel();
            await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(9)));
            var expected = phase == "factory" ? new AiOperationUsage(0, 0) : phase == "disposal" ? new(7, 0) : new(null, null);
            await f.Budget.Received(1).SettleAsync(f.OperationId, expected, "failed", CancellationToken.None);
        }
        finally { release.TrySetResult(); }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!f.Generator.Disposed) await Task.Delay(10, cleanup.Token);
        Assert.AreEqual(phase == "factory" ? 0 : 1, f.Generator.Calls);
        await f.Budget.Received(1).SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly byte[] Bytes = Encoding.UTF8.GetBytes("%PDF-1.7\nsynthetic authorized source");
        internal string Hash => Convert.ToHexStringLower(SHA256.HashData(Bytes));
        internal readonly Guid TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7(), FileId = Guid.CreateVersion7(), ModelId = Guid.CreateVersion7();
        internal readonly AiKnowledgeIndexTaskBinding Binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Command = Substitute.For<ICommandExecutor>();
        internal readonly ICommandTransaction Transaction = Substitute.For<ICommandTransaction>();
        internal readonly IIdGenerator Ids = Substitute.For<IIdGenerator>();
        internal readonly IBackgroundSessionAuthorization Authorization = Substitute.For<IBackgroundSessionAuthorization>();
        internal readonly CurrentTenantAccessor Tenant = new();
        internal readonly IAiOperationBudgetStore Budget = Substitute.For<IAiOperationBudgetStore>();
        internal readonly AiModelBindingScope ModelScope = new();
        internal readonly Guid OperationId = Guid.CreateVersion7();
        internal readonly PaidGenerator Generator = new();
        internal readonly PaidFactory Factory;
        internal Action? AfterManifest, AfterReservation, AfterSettlement;
        internal AiKnowledgeIndexTaskStore Tasks = null!;
        internal AiKnowledgeIndexSessionService Sessions = null!;
        internal AiKnowledgeIndexTextManifestRecord? ManifestRow;
        internal AiKnowledgeIndexVectorRecord? VectorRow;
        internal Dictionary<string, object?>? BatchParameters;
        internal bool Duplicate;
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly string Folder = Path.Combine(Path.GetTempPath(), "fullnet-pdf-planning-" + Guid.NewGuid().ToString("N"));
        internal string Started => Path.Combine(Folder, "started");
        internal string Cleaned => Path.Combine(Folder, "cleaned");
        internal readonly AiKnowledgeIndexPdfPlanningService Service;
        internal readonly Guid? TenantId;
        internal SessionBindingSnapshot Session;
        internal readonly SessionBindingSnapshot FrozenSession;
        internal AiKnowledgeIndexDispatchRecord Source;
        internal HostFileReference? Reference;
        internal TrackingStream Stream;
        internal string? DeniedPermission, InitialFault;
        internal string OpenMime = "application/pdf", Classification = "internal", Endpoint = "https://api.openai.com/v1";
        internal string? Organization;
        internal bool SourceDenied;
        internal int ReferenceCalls, HostCalls, TenantCalls, Commands, AuthCalls, DispatchCalls;
        internal Action? AfterReference, AfterOpen, AfterModel, AfterAuthorize;
        internal Action? AfterDispatch;
        internal Func<CancellationToken, Task>? BeforeOpen;
        internal Func<CancellationToken, Task>? BeforeModelAsync, BeforeAuthorizeAsync, BeforeReferenceAsync;
        internal TaskCompletionSource<Result<HostFileContent>>? OpenGate;

        internal Fixture(bool scoped, bool hold = false, bool malformed = false, int workTimeout = 25000, int pageCount = 2)
        {
            Factory = new(Generator);
            TenantId = scoped ? Guid.CreateVersion7() : null; var tenant = Tenant;
            if (TenantId is { } id) tenant.SetTenant(new TenantContext(id, "test", "测试")); else tenant.SetHost();
            Session = new(Binding.OwnerUserId, TenantId, Guid.CreateVersion7(), "stamp", "host", "host");
            FrozenSession = Session;
            Stream = new(Bytes); Clock.UtcNow.Returns(Now);
            Source = new() { TaskId = TaskId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId, DocumentVersion = 1,
                KnowledgeBaseVersion = 1, LeaseToken = Lease, LeaseExpiresAtUtc = Now.AddSeconds(30), ModelConfigId = ModelId,
                ModelConfigVersion = 1, FileId = FileId, ContentHash = Hash, ContentType = "application/pdf", SizeBytes = Bytes.Length };
            Reference = new(FileId, Bytes.Length, Hash.ToUpperInvariant());
            var command = Command; var ids = Ids;
            var transaction = Transaction; var auth = Authorization;
            var tasks = new AiKnowledgeIndexTaskStore(Query, command, tenant, Clock, ids);
            Tasks = tasks; ids.NewId().Returns(OperationId);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => { DispatchCalls++; AfterDispatch?.Invoke(); return SourceDenied ? null : Project(InitialFault is "mime" or "oversize" ? ChangedSource(InitialFault == "mime" ? "text/plain" : "application/pdf", InitialFault == "oversize" ? 5 * 1024 * 1024 + 1 : Bytes.Length) : Source); });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => new()
            { Id = FrozenSession.SessionId, IndexTaskId = TaskId, ActorUserId = FrozenSession.UserId, TenantId = FrozenSession.TenantId, SessionId = FrozenSession.SessionId,
                SessionKind = FrozenSession.SessionKind, SecurityStamp = FrozenSession.SecurityStamp, ActorScope = FrozenSession.ActorScope, EffectiveScope = FrozenSession.EffectiveScope, LeaseExpiresAtUtc = Now.AddSeconds(30) });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            { Assert.AreEqual(scoped ? AiKnowledgeSql.FindTenant : AiKnowledgeSql.FindHost, call.ArgAt<SqlStatement>(0)); return new AiKnowledgeBaseRecord
            { Id = Binding.KnowledgeBaseId, OwnerUserId = InitialFault == "owner" ? Guid.CreateVersion7() : Binding.OwnerUserId,
                TenantId = InitialFault == "tenant" ? Guid.CreateVersion7() : TenantId, Version = 1, IsEnabled = InitialFault != "disabled",
                EmbeddingModelConfigId = InitialFault == "approval" ? Guid.CreateVersion7() : ModelId, EmbeddingModelVersion = 1, DataClassification = Classification }; });
            Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns<Task<AiModelConfigRecord?>>(async _ =>
            { Assert.AreEqual(scoped ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat, _.ArgAt<SqlStatement>(0));
                var model = new AiModelConfigRecord { Id = ModelId, Version = 1, TenantId = TenantId, IsEnabled = true,
                ProviderKey = "openai_compatible", ModelId = "text-embedding-3-small", EndpointBaseUrl = Endpoint, OrganizationId = Organization, ApiKeyProtected = "protected" }; AfterModel?.Invoke();
                if (BeforeModelAsync is not null) await BeforeModelAsync(_.ArgAt<CancellationToken>(2)); return model; });
            auth.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(async call =>
            { Assert.IsFalse(State.HasTransaction); foreach (var permission in call.ArgAt<IReadOnlyCollection<string>>(1)) { AuthCalls++; AfterAuthorize?.Invoke(); } if (BeforeAuthorizeAsync is not null) await BeforeAuthorizeAsync(call.ArgAt<CancellationToken>(2));
                return call.ArgAt<SessionBindingSnapshot>(0) != Session || call.ArgAt<IReadOnlyCollection<string>>(1).Contains(DeniedPermission!) ? null : new AuthorizedSessionActor(
                InitialFault == "actor-user" ? Guid.CreateVersion7() : Session.UserId,
                InitialFault == "actor-tenant" ? Guid.CreateVersion7() : Session.TenantId,
                InitialFault == "actor-session" ? Guid.CreateVersion7() : Session.SessionId); });
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { Commands++; return 1; });
            var sessions = new AiKnowledgeIndexSessionService(Query, command, transaction, State, tenant, Clock, ids, tasks, auth);
            Sessions = sessions;
            var references = Substitute.For<IHostFileReferenceReader>(); var host = Substitute.For<IHostFileContentReader>(); var files = Substitute.For<ITenantResourceFileStore>();
            references.GetReadyReferenceAsync(FileId, Arg.Any<CancellationToken>()).Returns<Task<HostFileReference?>>(async _ => { ReferenceCalls++; AfterReference?.Invoke();
                if (BeforeReferenceAsync is not null) await BeforeReferenceAsync(_.ArgAt<CancellationToken>(1)); return Reference; });
            host.OpenReadyContentAsync(FileId, Arg.Any<CancellationToken>()).Returns(async call =>
            { Assert.IsFalse(State.HasTransaction); Interlocked.Increment(ref HostCalls); AfterOpen?.Invoke(); if (BeforeOpen is not null) await BeforeOpen(call.ArgAt<CancellationToken>(1));
                return OpenGate is null ? Result<HostFileContent>.Success(new(Stream, OpenMime, "untrusted-name.pdf")) : await OpenGate.Task; });
            files.OpenReadyContentAsync("ai", Binding.VersionId, FileId, Arg.Any<CancellationToken>()).Returns(async call =>
            { Assert.IsFalse(State.HasTransaction); TenantCalls++; AfterOpen?.Invoke(); if (BeforeOpen is not null) await BeforeOpen(call.ArgAt<CancellationToken>(3));
                return Result<TenantResourceFileContent>.Success(new(Stream, OpenMime, "untrusted-name.pdf")); });
            Directory.CreateDirectory(Folder);
            var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
            var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.GetFullPath(Path.Combine(p, executable))).FirstOrDefault(File.Exists);
            Assert.IsNotNull(node, "测试依赖真实 Node 子进程，不允许跳过");
            var script = Path.Combine(Folder, "cli.mjs");
            File.WriteAllText(script, Script.Replace("HOLD", hold ? "true" : "false", StringComparison.Ordinal).Replace("MALFORMED", malformed ? "true" : "false", StringComparison.Ordinal), new UTF8Encoding(false));
            if (pageCount > 2) File.WriteAllText(script, File.ReadAllText(script).Replace(
                "pages:[{pageNumber:1,text:'第一页完整内容 hello PDF'},{pageNumber:2,text:'第二页独立内容 world PDF'}]",
                "pages:Array.from({length:" + pageCount + "},(_,i)=>({pageNumber:i+1,text:'分页完整内容 hello PDF'}))", StringComparison.Ordinal), new UTF8Encoding(false));
            Service = new(Query, tenant, State, tasks, sessions, auth, references, host, files,
                new(node, script, "sha256:" + new string('a', 64), 10000), workTimeout);
        }
        internal Task<AiKnowledgeIndexPdfPreparation?> Plan(CancellationToken token = default) => Service.PlanAsync(Binding, TaskId, Lease, token);
        internal AiKnowledgeIndexPdfEmbeddingService Paid()
        {
            Transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(c => c.ArgAt<Func<CancellationToken, Task<bool>>>(0)(c.ArgAt<CancellationToken>(1)));
            Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(c =>
            {
                Commands++; var p = (Dictionary<string, object?>)c[1]!;
                if (p.ContainsKey("Payload") && (string)p["FormatKey"]! == AiKnowledgeIndexPdfVectorCodec.FormatKey)
                    VectorRow = new() { Id = (Guid)p["OperationId"]!, IndexTaskId = TaskId, BatchOrdinal = (int)p["BatchOrdinal"]!,
                        ModelConfigId = ModelId, ModelConfigVersion = 1, ProviderKey = (string)p["ProviderKey"]!, ModelId = (string)p["ModelId"]!,
                        Dimensions = (int)p["Dimensions"]!, InputCount = (int)p["InputCount"]!, RequestHash = (string)p["RequestHash"]!,
                        FormatKey = (string)p["FormatKey"]!, DistanceKey = (string)p["DistanceKey"]!, PayloadHash = (string)p["PayloadHash"]!, Payload = (byte[])p["Payload"]! };
                else if (p.ContainsKey("Payload")) ManifestRow = new() { Id = TaskId, FormatKey = (string)p["FormatKey"]!,
                    PayloadHash = (string)p["PayloadHash"]!, Payload = (byte[])p["Payload"]!, CreatedAtUtc = Now };
                else BatchParameters = new(p); if (p.ContainsKey("Payload")) AfterManifest?.Invoke(); return 1;
            });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTextManifestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => ManifestRow);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexBatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => BatchParameters is null ? null : new AiKnowledgeIndexBatchRecord
            { Id = OperationId, IndexTaskId = TaskId, BatchOrdinal = (int)BatchParameters["BatchOrdinal"]!, InputCount = (int)BatchParameters["InputCount"]!, RequestHash = (string)BatchParameters["RequestHash"]! });
            Budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
            { Assert.IsNotNull(ManifestRow, "预算预留前必须已冻结完整清单"); Assert.IsTrue(Stream.Closed); AfterReservation?.Invoke(); return new AiOperationReservation(OperationId, !Duplicate, Duplicate ? "unknown" : "reserved", 100, null, null); });
            Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
            { AfterSettlement?.Invoke(); return Task.CompletedTask; });
            var manifests = new AiKnowledgeIndexPdfManifestStore(Query, Command, Transaction, State, Tenant, Clock, Tasks, Sessions, Authorization);
            var batches = new AiKnowledgeIndexBatchStore(Query, Command, Tasks, Ids);
            return new(Service, manifests, new(Query, Tasks, batches, Budget), Budget, State, [Factory], ModelScope);
        }
        internal AiKnowledgeIndexPdfPersistenceService Persistence()
        {
            var execution = Paid();
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => VectorRow);
            var vectors = new AiKnowledgeIndexPdfVectorStore(Query, Command, Transaction, State, Tenant, Clock, Tasks, Sessions,
                Authorization, Options.Create(new DatabaseOptions()));
            return new(execution, vectors);
        }
        internal async Task WaitStarted()
        { using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8)); while (!File.Exists(Started)) await Task.Delay(20, deadline.Token); }
        internal void Release() => File.WriteAllText(Path.Combine(Folder, "release"), "1");
        internal void Lose(string fault)
        { switch (fault) { case "permission": DeniedPermission = "ai.knowledge_documents.source_read"; break;
                case "session": Session = Session with { SecurityStamp = "revoked" }; break;
                case "source": SourceDenied = true; break; case "binding": Source = ChangedSource("application/pdf", Bytes.Length, Guid.CreateVersion7()); break;
                case "expiry": Clock.UtcNow.Returns(Now.AddSeconds(30)); break;
                case "classification": Classification = "restricted"; break; default: Endpoint = "https://api.openai.com/v1/"; break; } }
        private AiKnowledgeIndexDispatchRecord ChangedSource(string mime, long size, Guid? fileId = null) => new() { TaskId = TaskId, DocumentId = Binding.DocumentId,
            VersionId = Binding.VersionId, DocumentVersion = 1, KnowledgeBaseVersion = 1, LeaseToken = Lease, LeaseExpiresAtUtc = Now.AddSeconds(30),
            ModelConfigId = ModelId, ModelConfigVersion = 1, FileId = fileId ?? FileId, ContentHash = Hash, ContentType = mime, SizeBytes = size };
        private AiKnowledgeIndexDispatchRecord Project(AiKnowledgeIndexDispatchRecord row) => new()
        { TaskId = row.TaskId, DocumentId = row.DocumentId, VersionId = row.VersionId, DocumentVersion = row.DocumentVersion,
            KnowledgeBaseVersion = row.KnowledgeBaseVersion, ModelConfigId = row.ModelConfigId, ModelConfigVersion = row.ModelConfigVersion,
            LeaseToken = row.LeaseToken, LeaseExpiresAtUtc = row.LeaseExpiresAtUtc, FileId = row.FileId, ContentHash = row.ContentHash,
            ContentType = row.ContentType, SizeBytes = row.SizeBytes, DataClassification = Classification, ModelProviderKey = "openai_compatible",
            ModelId = "text-embedding-3-small", ModelEndpointBaseUrl = Endpoint, ModelOrganizationId = Organization };
        public void Dispose() { ModelScope.Dispose(); Stream.Dispose(); Directory.Delete(Folder, true); }
        private const string Script = """
            import { writeFileSync, existsSync } from 'node:fs';
            import { fileURLToPath } from 'node:url'; import path from 'node:path';
            const folder = path.dirname(fileURLToPath(import.meta.url)); const args = process.argv.slice(2);
            const value = key => args[args.indexOf(key) + 1]; let count = 0; let interval;
            process.stdin.on('data', bytes => {
              count += bytes.length; if (count !== Number(value('--size-bytes'))) return;
              writeFileSync(path.join(folder, 'started'), '1');
              const publish = () => {
                const wire = MALFORMED ? '{}' : JSON.stringify({protocolVersion:1,parserVersion:'pdfpig-0.1.16-content-order-v1',
                  contentHash:value('--content-hash'),pages:[{pageNumber:1,text:'第一页完整内容 hello PDF'},{pageNumber:2,text:'第二页独立内容 world PDF'}]});
                process.stdout.write(wire, () => process.exit(0));
              };
              if (!HOLD) publish(); else interval = setInterval(() => { if(existsSync(path.join(folder,'release'))) {clearInterval(interval);publish();} }, 10);
            });
            process.stdin.on('end', () => { clearInterval(interval);writeFileSync(path.join(folder,'cleaned'),'1');process.exit(1); });
            process.stdin.resume();
            """;
    }

    private sealed class PaidFactory(PaidGenerator generator) : IAiModelClientFactory
    {
        internal Action? BeforeCreate;
        internal Func<CancellationToken, Task>? BeforeCreateAsync;
        public string ProviderKey => "openai_compatible";
        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken token) => throw new NotSupportedException();
        public async ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken token)
        { BeforeCreate?.Invoke(); if (BeforeCreateAsync is not null) await BeforeCreateAsync(token); return generator; }
    }
    private sealed class PaidGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        internal UsageDetails? UsageOverride;
        internal int Calls; internal bool Disposed; internal string[]? Inputs;
        internal string? Invalid;
        internal Action<CancellationToken>? BeforeReturn;
        internal Func<CancellationToken, Task>? BeforeReturnAsync;
        internal Action? BeforeDispose;
        public object? GetService(Type type, object? key = null) => null;
        public void Dispose() { BeforeDispose?.Invoke(); Disposed = true; }
        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++; Inputs = values.ToArray();
            BeforeReturn?.Invoke(cancellationToken);
            if (BeforeReturnAsync is not null) await BeforeReturnAsync(cancellationToken);
            return new GeneratedEmbeddings<Embedding<float>>(Invalid == "count" ? [] : Inputs.Select(_ => { var v = new float[Invalid == "dimensions" ? 3 : 1536]; v[0] = Invalid == "nonfinite" ? float.NaN : Invalid == "zero" ? 0 : 1; return new Embedding<float>(v); }))
            { Usage = UsageOverride ?? new UsageDetails { InputTokenCount = 7, OutputTokenCount = 0 } };
        }
    }
    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        internal bool Closed;
        internal bool CloseFailure;
        internal int Reads;
        internal Action? AfterRead, AfterClose;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { var result = base.ReadAsync(buffer, token); Reads++; var after = AfterRead; AfterRead = null; after?.Invoke(); return result; }
        protected override void Dispose(bool disposing)
        { base.Dispose(disposing); if (Closed) return; Closed = true; AfterClose?.Invoke(); if (CloseFailure) throw new IOException("synthetic close failure"); }
    }
}
