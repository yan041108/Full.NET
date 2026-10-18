using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>收费委托的原会话、独立权限、顺序累计额度与并发撤销围栏。</summary>
[TestClass]
public sealed class AiKnowledgeIndexGrantStoreTests
{
    [TestMethod][DataRow(false)][DataRow(true)]
    public async Task Granted_task_requires_original_login_and_live_independent_permission(bool scoped)
    {
        var f = new Fixture(scoped);
        Assert.IsTrue(await f.ExecuteAsync());
        Assert.IsFalse(await f.Store.CanExecuteTaskAsync(f.Binding, f.TaskId, f.Session with { SessionId = Guid.CreateVersion7() }, default));
        f.Allowed = false; Assert.IsFalse(await f.ExecuteAsync());
        await f.Commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod][DataRow("revoked")][DataRow("expired")][DataRow("disabled")][DataRow("scope")]
    public async Task Inactive_grant_never_admits_a_paid_batch(string reason)
    {
        var f = new Fixture();
        switch (reason)
        {
            case "revoked": f.Row = f.Row with { IsRevoked = true }; break;
            case "expired": f.Row = f.Row with { ExpiresAtUtc = f.Clock.UtcNow }; break;
            case "disabled": f.Options.Enabled = false; break;
            default: f.Tenant.Id.Returns(Guid.CreateVersion7()); f.Tenant.IsHost.Returns(false); f.Tenant.IsAvailable.Returns(true); break;
        }
        Assert.IsFalse(await f.Store.ReserveBatchAsync(f.Binding, f.TaskId, 0, 10, default));
        await f.Commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Another_document_admission_during_authorization_does_not_revoke_the_existing_task()
    {
        var f = new Fixture(); f.DuringAuthorization = () => f.Row = f.Row with { AdmittedDocuments = 2, Version = 2 };
        Assert.IsTrue(await f.ExecuteAsync());
    }

    [TestMethod]
    public async Task Revocation_during_authorization_stops_the_existing_task()
    {
        var f = new Fixture(); f.DuringAuthorization = () => f.Row = f.Row with { IsRevoked = true, Version = 2 };
        Assert.IsFalse(await f.ExecuteAsync());
    }

    [TestMethod]
    public async Task Batch_reservations_are_sequential_bounded_and_idempotent()
    {
        var f = new Fixture();
        Assert.IsFalse(await f.Store.ReserveBatchAsync(f.Binding, f.TaskId, 1, 10, default));
        Assert.IsTrue(await f.Store.ReserveBatchAsync(f.Binding, f.TaskId, 0, 60, default));
        Assert.AreEqual(60L, f.Row.ReservedInputTokens);
        Assert.IsTrue(await f.Store.ReserveBatchAsync(f.Binding, f.TaskId, 0, 60, default));
        Assert.AreEqual(60L, f.Row.ReservedInputTokens);
        Assert.IsFalse(await f.Store.ReserveBatchAsync(f.Binding, f.TaskId, 1, 41, default));
        Assert.IsTrue(await f.Store.ReserveBatchAsync(f.Binding, f.TaskId, 1, 40, default));
        Assert.AreEqual(100L, f.Row.ReservedInputTokens);
        await f.Commands.Received(2).ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Manual_task_without_grant_keeps_its_existing_admission_path()
    {
        var f = new Fixture(); f.Missing = true; f.Options.Enabled = false;
        Assert.IsTrue(await f.ExecuteAsync());
        Assert.IsTrue(await f.Store.ReserveBatchAsync(f.Binding, f.TaskId, 0, 10, default));
        await f.Authorization.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default!, default);
    }

    [TestMethod]
    public async Task Late_scope_change_cannot_misclassify_granted_task_as_manual()
    {
        var f = new Fixture(); f.DuringRead = () => { f.Tenant.Id.Returns(Guid.CreateVersion7()); f.Tenant.IsHost.Returns(false); f.Tenant.IsAvailable.Returns(true); f.Missing = true; };
        Assert.IsFalse(await f.Store.ReserveBatchAsync(f.Binding, f.TaskId, 0, 10, default));
        await f.Commands.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public void Create_requires_explicit_paid_confirmation_and_absolute_bounded_authority()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new CreateAiKnowledgeIndexGrantRequest(Guid.CreateVersion7(), 1, now.AddHours(1), 4, 100, true);
        Assert.IsTrue(AiKnowledgeIndexGrantPolicy.Valid(request, now));
        Assert.IsFalse(AiKnowledgeIndexGrantPolicy.Valid(request with { ConfirmPaidDispatch = false }, now));
        Assert.IsFalse(AiKnowledgeIndexGrantPolicy.Valid(request with { DocumentLimit = 65 }, now));
        Assert.IsFalse(AiKnowledgeIndexGrantPolicy.Valid(request with { InputTokenLimitPerDocument = 1_000_001 }, now));
        Assert.IsFalse(AiKnowledgeIndexGrantPolicy.Valid(request with { ExpiresAtUtc = now.AddDays(2) }, now));
    }

    [TestMethod]
    public void Expiry_uses_identical_microsecond_identity_in_both_databases()
    {
        var value = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.FromHours(8)).AddTicks(1234567);
        var normalized = AiKnowledgeIndexGrantService.NormalizeExpiry(value);
        Assert.AreEqual(0L, normalized.UtcTicks % 10); Assert.AreEqual(TimeSpan.Zero, normalized.Offset);
        Assert.AreEqual(value.UtcTicks / 10 * 10, normalized.UtcTicks);
        Assert.AreEqual(normalized, AiKnowledgeIndexGrantService.NormalizeExpiry(normalized));
    }

    private sealed class Fixture
    {
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly ICommandTransaction Transaction = Substitute.For<ICommandTransaction>();
        internal readonly IDataTransactionState Transactions = Substitute.For<IDataTransactionState>();
        internal readonly ICurrentTenant Tenant = Substitute.For<ICurrentTenant>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly IIdGenerator Ids = Substitute.For<IIdGenerator>();
        internal readonly IBackgroundSessionAuthorization Authorization = Substitute.For<IBackgroundSessionAuthorization>();
        internal readonly AiKnowledgeIndexGrantOptions Options = new() { Enabled = true };
        internal readonly SessionBindingSnapshot Session;
        internal readonly AiKnowledgeIndexTaskBinding Binding;
        internal readonly Guid TaskId = Guid.CreateVersion7();
        internal AiKnowledgeIndexGrantRecord Row;
        internal readonly AiKnowledgeIndexGrantStore Store;
        internal bool Allowed = true, Missing, InTransaction;
        internal Action? DuringAuthorization, DuringRead;
        internal Fixture(bool scoped = false)
        {
            Guid? tenant = scoped ? Guid.CreateVersion7() : null; Tenant.Id.Returns(tenant); Tenant.IsHost.Returns(!scoped);
            Tenant.IsAvailable.Returns(scoped);
            Clock.UtcNow.Returns(DateTimeOffset.UtcNow); Ids.NewId().Returns(_ => Guid.CreateVersion7());
            Session = new(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(), "stamp", scoped ? "tenant" : "host", scoped ? "tenant" : "host");
            Binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Session.UserId);
            Row = new() { Id = Guid.CreateVersion7(), TenantId = tenant, KnowledgeBaseId = Binding.KnowledgeBaseId, OwnerUserId = Session.UserId,
                ClientRequestId = Guid.CreateVersion7(), KnowledgeBaseVersion = 1, ModelConfigId = Guid.CreateVersion7(), ModelConfigVersion = 1,
                SessionJson = JsonSerializer.Serialize(Session, AiJsonSerializerContext.Default.SessionBindingSnapshot), ExpiresAtUtc = Clock.UtcNow.AddHours(1),
                DocumentLimit = 4, AdmittedDocuments = 1, InputTokenLimitPerDocument = 100, Version = 1, TaskId = TaskId };
            Transactions.HasTransaction.Returns(_ => InTransaction);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexGrantRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => { DuringRead?.Invoke(); return Missing ? null : Row; });
            Authorization.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => { DuringAuthorization?.Invoke(); return Allowed ? new AuthorizedSessionActor(Session.UserId, Session.TenantId, Session.SessionId) : null; });
            Transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>()).Returns(async call =>
            { InTransaction = true; try { return await call.ArgAt<Func<CancellationToken, Task<bool>>>(0)(call.ArgAt<CancellationToken>(1)); } finally { InTransaction = false; } });
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var p = (Dictionary<string, object?>)call.ArgAt<object>(1);
                Row = Row with { ReservedInputTokens = Row.ReservedInputTokens + (long)p["InputTokens"]!, LastBatchOrdinal = (int)p["BatchOrdinal"]! };
                return 1;
            });
            Store = new(Query, Commands, Transaction, Transactions, Tenant, Clock, Ids, Authorization, Microsoft.Extensions.Options.Options.Create(Options));
        }
        internal Task<bool> ExecuteAsync() => Store.CanExecuteTaskAsync(Binding, TaskId, Session, default);
    }
}
