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
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.AI;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>文件来源、会话授权和预算事实必须共同约束真实 GenerateAsync 调用。</summary>
[TestClass]
public sealed class AiKnowledgeIndexEmbeddingTests
{
    [TestMethod]
    public async Task Index_permission_revocation_stops_background_dispatch()
    {
        using var f = new Fixture(false) { DeniedPermission = "ai.knowledge_documents.index_enqueue" };
        Assert.IsNull(await f.Execute()); Assert.IsNull(f.OpenedStream); Assert.IsFalse(f.Budget.ReceivedCalls().Any());
    }
    [TestMethod]
    public async Task Legacy_task_without_persisted_session_never_dispatches()
    {
        using var f = new Fixture(false) { NoSession = true };
        Assert.IsNull(await f.Execute()); Assert.IsNull(f.OpenedStream); Assert.AreEqual(0, f.Factory.Calls);
        Assert.IsFalse(f.Budget.ReceivedCalls().Any());
    }

    [TestMethod] [DataRow("factory")] [DataRow("response")]
    public async Task Persisted_session_changed_during_wait_cannot_deliver(string wait)
    {
        using var f = new Fixture(false);
        if (wait == "factory") f.Factory.BeforeCreate = () => f.StoredSession = f.Session with { SessionId = Guid.CreateVersion7() };
        else f.Generator.BeforeReturn = _ => f.StoredSession = f.Session with { SecurityStamp = "changed" };
        Assert.IsNull(await f.Execute()); Assert.AreEqual(wait == "factory" ? 0 : 1, f.Generator.Calls);
        await f.Budget.Received(1).SettleAsync(f.OperationId, wait == "factory" ? new(0, 0) : new(7, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    public async Task Ambient_transaction_is_rejected_before_query_or_file_io()
    {
        using var f = new Fixture(false); f.Transactions.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Execute());
        Assert.IsFalse(f.Query.ReceivedCalls().Any()); Assert.IsNull(f.OpenedStream); Assert.IsFalse(f.Budget.ReceivedCalls().Any());
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Trusted_file_batch_dispatches_once_and_settles_known_usage(bool tenant)
    {
        using var f = new Fixture(tenant);
        var result = await f.Execute();
        Assert.IsNotNull(result); Assert.AreEqual(f.OperationId, result.OperationId); Assert.HasCount(1, result.Chunks);
        Assert.IsNotNull(result.Plan); Assert.AreEqual(1, result.Plan.BatchCount); Assert.AreEqual(1, result.Plan.ChunkCount);
        Assert.AreEqual(64, result.Plan.PlanHash.Length); Assert.AreEqual(64, result.BatchSourceHash.Length);
        Assert.AreEqual(f.Text, result.Chunks[0].Source.EmbeddingText); Assert.AreEqual(1536, result.Chunks[0].Vector.Length);
        Assert.AreEqual(f.Source.FileId, result.Source.FileId); Assert.AreEqual(1, f.Generator.Calls);
        Assert.IsTrue(f.OpenedStream!.Disposed); Assert.IsTrue(f.Generator.Disposed);
        Assert.IsNotNull(f.Factory.Binding?.CredentialReference);
        Assert.AreEqual(f.Model.Id, f.Factory.Binding!.ConfigId); Assert.AreEqual(f.Model.Version, f.Factory.Binding.Version);
        Assert.AreEqual(f.Model.EndpointBaseUrl, f.Factory.Binding.Endpoint.AbsoluteUri.TrimEnd('/'));
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "succeeded", CancellationToken.None);
        foreach (var permission in new[] { AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.Upload })
            Assert.IsTrue(f.Authorization.ReceivedCalls().Any(call => Equals(call.GetArguments()[1], permission)));
        f.Generator.LastVector![0] = 99;
        Assert.AreEqual(1f, result.Chunks[0].Vector.Span[0]);
        if (tenant) await f.TenantFiles.Received(1).OpenReadyContentAsync("ai", f.Binding.VersionId, f.Source.FileId, Arg.Any<CancellationToken>());
        else await f.HostFiles.Received(1).OpenReadyContentAsync(f.Source.FileId, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Last_batch_reports_whole_generation_instead_of_only_its_own_chunk_count()
    {
        using var f = new Fixture(false, string.Concat(Enumerable.Repeat("a ", 12000)));
        var result = await f.Execute(ordinal: 1); Assert.IsNotNull(result); Assert.IsNotNull(result.Plan);
        Assert.AreEqual(2, result.Plan.BatchCount); Assert.IsTrue(result.Plan.ChunkCount > 16);
        Assert.AreEqual(result.Plan.ChunkCount - 16, result.Chunks.Count); Assert.AreEqual(16, result.Chunks[0].Source.Sequence);
        Assert.AreEqual(1, f.Generator.Calls); Assert.AreEqual(64, result.BatchSourceHash.Length);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "succeeded", CancellationToken.None);
    }

    [TestMethod] [DataRow("owner")] [DataRow("tenant")] [DataRow("session")]
    public async Task Wrong_session_binding_never_reads_files_or_reserves(string changed)
    {
        using var f = new Fixture(false);
        var session = changed switch { "owner" => f.Session with { UserId = Guid.CreateVersion7() }, "tenant" => f.Session with { TenantId = Guid.CreateVersion7() }, _ => f.Session with { SessionId = Guid.Empty } };
        Assert.IsNull(await f.Execute(session)); Assert.IsNull(f.OpenedStream); Assert.IsFalse(f.Budget.ReceivedCalls().Any());
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Permission_denied_closes_before_source_read(bool tenant)
    {
        using var f = new Fixture(tenant) { Denied = true };
        Assert.IsNull(await f.Execute()); Assert.IsNull(f.OpenedStream); Assert.IsFalse(f.Budget.ReceivedCalls().Any());
    }

    [TestMethod] [DataRow(false, "hash")] [DataRow(true, "hash")] [DataRow(false, "size")] [DataRow(true, "size")] [DataRow(false, "type")] [DataRow(true, "type")]
    public async Task Source_mismatch_cannot_reach_budget_or_provider(bool tenant, string changed)
    {
        using var f = new Fixture(tenant);
        f.Source = f.NewSource(hash: changed == "hash" ? new string('A', 64) : null,
            size: changed == "size" ? f.Bytes.Length + 1 : null, type: changed == "type" ? "application/pdf" : null);
        Assert.IsNull(await f.Execute()); Assert.AreEqual(0, f.Generator.Calls); Assert.IsFalse(f.Budget.ReceivedCalls().Any());
        if (f.OpenedStream is not null) Assert.IsTrue(f.OpenedStream.Disposed);
    }

    [TestMethod]
    public async Task Lease_lost_while_opening_disposes_without_reading_or_reserving()
    {
        using var f = new Fixture(false) { AfterOpen = () => { } };
        f.AfterOpen = () => f.SourceDenied = true;
        Assert.IsNull(await f.Execute()); Assert.IsTrue(f.OpenedStream!.Disposed); Assert.AreEqual(0, f.OpenedStream.Reads);
        Assert.IsFalse(f.Budget.ReceivedCalls().Any());
    }

    [TestMethod] [DataRow("unknown")] [DataRow("reserved")] [DataRow("known")]
    public async Task Duplicate_reservation_never_constructs_generator_or_settles(string status)
    {
        using var f = new Fixture(false) { Duplicate = true, Status = status };
        Assert.IsNull(await f.Execute()); Assert.AreEqual(0, f.Factory.Calls);
        Assert.IsFalse(f.Budget.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "SettleAsync"));
    }

    [TestMethod] [DataRow("authority")] [DataRow("permission")] [DataRow("model")]
    public async Task Revoked_during_generator_creation_settles_zero_without_dispatch(string changed)
    {
        using var f = new Fixture(false);
        f.Factory.BeforeCreate = () => { if (changed == "authority") f.SourceDenied = true; else if (changed == "permission") f.Denied = true; else f.Model = f.NewModel(version: 2); };
        Assert.IsNull(await f.Execute()); Assert.AreEqual(0, f.Generator.Calls); Assert.IsTrue(f.Generator.Disposed);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(0, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    public async Task Factory_failure_before_generate_has_known_zero_usage()
    {
        using var f = new Fixture(false);
        f.Factory.BeforeCreate = () => throw new IOException("creation failed");
        await Assert.ThrowsExactlyAsync<IOException>(() => f.Execute());
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(0, 0), "failed", CancellationToken.None);
    }

    [TestMethod]
    public async Task Cancellation_before_generate_settles_zero_and_disposes()
    {
        using var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        f.Factory.BeforeCreate = () => cancel.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Execute(token: cancel.Token));
        Assert.AreEqual(0, f.Generator.Calls); Assert.IsTrue(f.Generator.Disposed);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(0, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    public async Task Unknown_data_classification_never_reaches_provider()
    {
        using var f = new Fixture(false) { Classification = "unknown" };
        Assert.IsNull(await f.Execute()); Assert.AreEqual(0, f.Factory.Calls); Assert.IsFalse(f.Budget.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Later_batch_preserves_source_sequence_and_bounded_exact_inputs()
    {
        using var f = new Fixture(false, string.Concat(Enumerable.Repeat("测试分块 hello 世界 0190ABC123 ", 4000)));
        var result = await f.Execute(ordinal: 1);
        Assert.IsNotNull(result); Assert.AreEqual(1, result.BatchOrdinal);
        Assert.IsTrue(result.Chunks[0].Source.Sequence >= 16); Assert.IsTrue(result.Chunks.Count is > 0 and <= 16);
        Assert.IsTrue(result.Chunks.Sum(item => item.Source.EmbeddingText.Length) <= 32000);
        CollectionAssert.AreEqual(result.Chunks.Select(item => item.Source.EmbeddingText).ToArray(), f.Generator.Inputs!);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task No_response_after_generate_preserves_unknown_usage(bool cancel)
    {
        using var f = new Fixture(false);
        f.Generator.BeforeReturn = _ => { if (cancel) throw new OperationCanceledException(); throw new IOException("response lost"); };
        if (cancel) await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Execute());
        else await Assert.ThrowsExactlyAsync<IOException>(() => f.Execute());
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(null, null), cancel ? "cancelled" : "failed", CancellationToken.None);
        Assert.IsTrue(f.Generator.Disposed);
    }

    [TestMethod]
    public async Task Completed_response_without_metering_keeps_unknown_even_on_success()
    {
        using var f = new Fixture(false); f.Generator.Tokens = null;
        Assert.IsNotNull(await f.Execute());
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(null, 0), "succeeded", CancellationToken.None);
    }

    [TestMethod] [DataRow("count")] [DataRow("dimensions")] [DataRow("nonfinite")] [DataRow("zero")]
    public async Task Invalid_vectors_reject_output_but_keep_reported_usage(string invalid)
    {
        using var f = new Fixture(false); f.Generator.Invalid = invalid;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Execute());
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "failed", CancellationToken.None);
    }

    [TestMethod]
    public async Task Invalid_metering_is_unknown_and_cannot_deliver_vectors()
    {
        using var f = new Fixture(false); f.Generator.Tokens = -1;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => f.Execute());
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(null, null), "failed", CancellationToken.None);
    }

    [TestMethod]
    public async Task Lost_authority_after_response_keeps_usage_but_hides_vectors()
    {
        using var f = new Fixture(false); f.Generator.BeforeReturn = _ => f.SourceDenied = true;
        Assert.IsNull(await f.Execute());
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    public async Task Cancellation_after_response_settles_known_usage_with_uncancelled_token()
    {
        using var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        f.Generator.BeforeReturn = _ => cancel.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Execute(token: cancel.Token));
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    public async Task Settlement_failure_propagates_instead_of_reporting_success()
    {
        using var f = new Fixture(false);
        f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("settlement failed")));
        await Assert.ThrowsExactlyAsync<IOException>(() => f.Execute());
    }

    [TestMethod] [DataRow("authority")] [DataRow("permission")] [DataRow("lease")] [DataRow("model")]
    public async Task Authority_lost_during_settlement_keeps_usage_but_hides_vectors(string changed)
    {
        using var f = new Fixture(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            entered.SetResult(); await release.Task;
        });
        var running = f.Execute(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (changed == "authority") f.SourceDenied = true;
        else if (changed == "permission") f.Denied = true;
        else if (changed == "lease") f.Clock.UtcNow.Returns(f.Source.LeaseExpiresAtUtc);
        else f.Model = f.NewModel(version: 2);
        release.SetResult(); Assert.IsNull(await running);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "succeeded", CancellationToken.None);
        Assert.AreEqual(1, f.Generator.Calls);
    }

    [TestMethod]
    public async Task Cancellation_during_settlement_keeps_usage_without_returning_vectors()
    {
        using var f = new Fixture(false); using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async _ => { entered.SetResult(); await release.Task; });
        var running = f.Execute(token: cancel.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); cancel.Cancel(); release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => running);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "succeeded", CancellationToken.None);
        Assert.AreEqual(1, f.Generator.Calls);
    }

    [TestMethod]
    public async Task Session_revoked_during_final_binding_read_cannot_deliver_settled_vectors()
    {
        using var f = new Fixture(false); var settled = false; var reads = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => { settled = true; return Task.CompletedTask; });
        f.BeforeSessionRead = async () => { if (settled && ++reads == 2) { entered.SetResult(); await release.Task; } };
        var running = f.Execute(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); f.Denied = true; release.SetResult();
        Assert.IsNull(await running);
        await f.Budget.Received(1).SettleAsync(f.OperationId, new(7, 0), "succeeded", CancellationToken.None);
        Assert.AreEqual(1, f.Generator.Calls);
    }

    [TestMethod]
    public async Task Plan_refusal_prevents_budget_reservation_and_model_dispatch()
    {
        using var f = new Fixture(false); var called = false;
        Assert.IsNull(await f.Execute(beforeDispatch: (plan, _) => { called = true; Assert.AreEqual(1, plan.Summary.ChunkCount); return Task.FromResult(false); }));
        Assert.IsTrue(called); Assert.AreEqual(0, f.Generator.Calls);
        await f.Budget.DidNotReceive().ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Plan_storage_failure_propagates_before_any_charge()
    {
        using var f = new Fixture(false);
        await Assert.ThrowsExactlyAsync<IOException>(() => f.Execute(beforeDispatch: (_, _) => throw new IOException("plan unavailable")));
        Assert.AreEqual(0, f.Generator.Calls);
        await f.Budget.DidNotReceive().ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Plan_storage_wait_cannot_reuse_previous_authority()
    {
        using var f = new Fixture(false);
        Assert.IsNull(await f.Execute(beforeDispatch: (_, _) => { f.SourceDenied = true; return Task.FromResult(true); }));
        Assert.AreEqual(0, f.Generator.Calls);
        await f.Budget.DidNotReceive().ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Complete_plan_is_delivered_to_storage_before_last_batch_dispatch()
    {
        using var f = new Fixture(false, string.Concat(Enumerable.Repeat("a ", 12000))); AiKnowledgeIndexTextPlan? seen = null;
        var result = await f.Execute(ordinal: 1, beforeDispatch: (plan, _) => { seen = plan; return Task.FromResult(true); });
        Assert.IsNotNull(result); Assert.IsNotNull(seen); Assert.AreEqual(2, seen.Summary.BatchCount);
        Assert.AreEqual(seen.Summary, result.Plan); Assert.AreEqual(1, f.Generator.Calls);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Text;
        internal readonly byte[] Bytes;
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Command = Substitute.For<ICommandExecutor>();
        internal readonly IDataTransactionState Transactions = Substitute.For<IDataTransactionState>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly IAiOperationBudgetStore Budget = Substitute.For<IAiOperationBudgetStore>();
        internal readonly IBackgroundSessionAuthorization Authorization = Substitute.For<IBackgroundSessionAuthorization>();
        internal readonly IHostFileReferenceReader References = Substitute.For<IHostFileReferenceReader>();
        internal readonly IHostFileContentReader HostFiles = Substitute.For<IHostFileContentReader>();
        internal readonly ITenantResourceFileStore TenantFiles = Substitute.For<ITenantResourceFileStore>();
        internal readonly Guid OperationId = Guid.CreateVersion7(), TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7(), FileId = Guid.CreateVersion7(), ModelId = Guid.CreateVersion7();
        internal readonly AiKnowledgeIndexTaskBinding Binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        internal readonly SessionBindingSnapshot Session;
        internal AiKnowledgeIndexDispatchRecord Source;
        internal AiModelConfigRecord Model;
        internal readonly TestGenerator Generator = new();
        internal readonly TestFactory Factory;
        internal TestStream? OpenedStream;
        internal Action? AfterOpen;
        internal Func<Task>? BeforeSessionRead;
        internal bool Denied, SourceDenied, Duplicate, NoSession;
        internal string? DeniedPermission;
        internal SessionBindingSnapshot? StoredSession;
        internal string Status = "reserved";
        internal string Classification = "internal";
        private readonly AiModelBindingScope modelScope = new();
        private readonly AiKnowledgeIndexEmbeddingService service;
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        private Dictionary<string, object?>? persisted;

        internal Fixture(bool tenantScope, string? text = null)
        {
            Text = text ?? "精确来源 original text";
            Bytes = Encoding.UTF8.GetBytes(Text);
            var tenant = new CurrentTenantAccessor();
            Guid? tenantId = tenantScope ? Guid.CreateVersion7() : null;
            if (tenantId is { } id) tenant.SetTenant(new TenantContext(id, "test", "测试")); else tenant.SetHost();
            Session = new(Binding.OwnerUserId, tenantId, Guid.CreateVersion7(), "stamp", tenantScope ? "tenant" : "host", tenantScope ? "tenant" : "host");
            StoredSession = Session;
            Source = NewSource(); Model = NewModel(); Factory = new(Generator);
            Clock.UtcNow.Returns(now);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(OperationId);
            var tasks = new AiKnowledgeIndexTaskStore(Query, Command, tenant, Clock, ids);
            var batches = new AiKnowledgeIndexBatchStore(Query, Command, tasks, ids);
            var sessions = new AiKnowledgeIndexSessionService(Query, Command, Substitute.For<ICommandTransaction>(), Transactions, tenant, Clock, ids, tasks, Authorization);
            service = new(Query, tenant, Transactions, tasks, sessions, new(Query, tasks, batches, Budget), Budget, Authorization, References, HostFiles, TenantFiles, [Factory], modelScope);
            Authorization.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Denied || call.ArgAt<string>(1) == DeniedPermission ? null : new AuthorizedSessionActor(Session.UserId, Session.TenantId, Session.SessionId));
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(async _ =>
            {
                if (BeforeSessionRead is not null) await BeforeSessionRead();
                return NoSession ? null : new AiKnowledgeIndexSessionRecord { Id = OperationId, IndexTaskId = TaskId, ActorUserId = StoredSession!.UserId,
                    TenantId = StoredSession.TenantId, SessionId = StoredSession.SessionId, SessionKind = StoredSession.SessionKind,
                    SecurityStamp = StoredSession.SecurityStamp, ActorScope = StoredSession.ActorScope, EffectiveScope = StoredSession.EffectiveScope,
                    LeaseExpiresAtUtc = Source.LeaseExpiresAtUtc };
            });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => SourceDenied ? null : Source);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeBaseRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => new AiKnowledgeBaseRecord { Id = Binding.KnowledgeBaseId, Version = 1, OwnerUserId = Binding.OwnerUserId, TenantId = tenantId, IsEnabled = true, DataClassification = Classification, EmbeddingModelConfigId = ModelId, EmbeddingModelVersion = 1 });
            Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Model);
            Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call => { persisted = (Dictionary<string, object?>)call[1]!; return 1; });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexBatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => new AiKnowledgeIndexBatchRecord { Id = OperationId, IndexTaskId = TaskId, BatchOrdinal = (int)persisted!["BatchOrdinal"]!, InputCount = (int)persisted["InputCount"]!, RequestHash = (string)persisted["RequestHash"]! });
            Budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(_ => new AiOperationReservation(OperationId, !Duplicate, Status, 100, null, null));
            References.GetReadyReferenceAsync(FileId, Arg.Any<CancellationToken>()).Returns(_ => new HostFileReference(FileId, Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes))));
            HostFiles.OpenReadyContentAsync(FileId, Arg.Any<CancellationToken>()).Returns(_ => { OpenedStream = new(Bytes); AfterOpen?.Invoke(); return Result<HostFileContent>.Success(new(OpenedStream, "text/plain", "source.txt")); });
            TenantFiles.OpenReadyContentAsync("ai", Binding.VersionId, FileId, Arg.Any<CancellationToken>()).Returns(_ => { OpenedStream = new(Bytes); AfterOpen?.Invoke(); return Result<TenantResourceFileContent>.Success(new(OpenedStream, "text/plain", "source.txt")); });
        }
        internal AiKnowledgeIndexDispatchRecord NewSource(string? hash = null, int? size = null, string? type = null) => new() { TaskId = TaskId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId, DocumentVersion = 1, KnowledgeBaseVersion = 1, LeaseToken = Lease, LeaseExpiresAtUtc = now.AddSeconds(30), ModelConfigId = ModelId, ModelConfigVersion = 1, FileId = FileId, ContentHash = hash ?? Convert.ToHexString(SHA256.HashData(Bytes)), ContentType = type ?? "text/plain", SizeBytes = size ?? Bytes.Length };
        internal AiModelConfigRecord NewModel(int version = 1) => new() { Id = ModelId, Version = version, ProviderKey = "openai_compatible", ModelId = "text-embedding-3-small", EndpointBaseUrl = "https://api.openai.com/v1", IsEnabled = true, ApiKeyProtected = "protected" };
        internal Task<AiKnowledgeIndexEmbeddingBatch?> Execute(SessionBindingSnapshot? session = null, CancellationToken token = default, int ordinal = 0,
            Func<AiKnowledgeIndexTextPlan, CancellationToken, Task<bool>>? beforeDispatch = null)
        {
            // 测试改变存储行，不再向生产恢复 API 传入新的会话身份。
            if (session is not null) StoredSession = session;
            return service.ExecuteTextBatchAsync(Binding, TaskId, Lease, ordinal, token, beforeDispatch);
        }
        public void Dispose() => modelScope.Dispose();
    }
    private sealed class TestStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        internal bool Disposed; internal int Reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { Reads++; return base.ReadAsync(buffer, cancellationToken); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class TestFactory(TestGenerator generator) : IAiModelClientFactory
    {
        public string ProviderKey => "openai_compatible";
        internal int Calls; internal ModelBinding? Binding; internal Action? BeforeCreate;
        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken cancellationToken) { Calls++; Binding = binding; BeforeCreate?.Invoke(); return ValueTask.FromResult<IEmbeddingGenerator<string, Embedding<float>>>(generator); }
    }
    private sealed class TestGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        internal int Calls; internal bool Disposed; internal long? Tokens = 7; internal string? Invalid; internal Action<CancellationToken>? BeforeReturn; internal float[]? LastVector;
        internal string[]? Inputs;
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => Disposed = true;
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++; Assert.IsTrue(cancellationToken.CanBeCanceled); var inputs = values.ToArray(); Inputs = inputs;
            var embeddings = inputs.Select(_ => { var vector = new float[Invalid == "dimensions" ? 3 : 1536]; vector[0] = Invalid == "nonfinite" ? float.NaN : Invalid == "zero" ? 0 : 1; LastVector = vector; return new Embedding<float>(vector); }).ToArray();
            BeforeReturn?.Invoke(cancellationToken);
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(Invalid == "count" ? [] : embeddings) { Usage = Tokens is { } count ? new UsageDetails { InputTokenCount = count, OutputTokenCount = 0 } : null });
        }
    }
}
