using System.Data;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>预算准入只能使用持久化批次身份；未知或重复凭据不得释放为零或重新授权。</summary>
[TestClass]
public sealed class AiKnowledgeIndexBudgetTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task New_reservation_uses_persisted_operation_and_trusted_model(bool tenantScope)
    {
        var fixture = new Fixture(tenantScope);
        var result = await fixture.ReserveAsync();
        Assert.IsNotNull(result); Assert.IsNotNull(result.Source); Assert.IsTrue(result.Reservation.IsNew);
        Assert.AreEqual(fixture.OperationId, result.Request.OperationId);
        Assert.AreEqual(fixture.Model.Id, result.Request.ModelConfigId);
        Assert.AreEqual("embedding", result.Request.Kind); Assert.AreEqual(0L, result.Request.OutputTokenLimit);
        Assert.AreEqual(AiEmbeddingBudgetRequest.Create(fixture.OperationId, fixture.Model, fixture.Inputs), result.Request);
        Assert.IsTrue(fixture.PersistedBeforeReserve);
        Assert.AreEqual(2, fixture.DispatchReads);
        var modelSql = fixture.Query.ReceivedCalls().Single(call => call.GetMethodInfo().GetGenericArguments().Contains(typeof(AiModelConfigRecord))).GetArguments();
        Assert.AreEqual(tenantScope ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, ((SqlStatement)modelSql[0]!).Scope);
        Assert.IsFalse(((Dictionary<string, object?>)modelSql[1]!).ContainsKey("TenantId"));
    }

    [TestMethod]
    [DataRow("reserved")]
    [DataRow("unknown")]
    [DataRow("known")]
    public async Task Existing_reservation_never_returns_dispatch_source_or_settles(string status)
    {
        var fixture = new Fixture(false);
        fixture.Budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AiOperationReservation(fixture.OperationId, false, status, 100, null, null));
        var result = await fixture.ReserveAsync();
        Assert.IsNotNull(result); Assert.IsNull(result.Source); Assert.IsFalse(result.Reservation.IsNew);
        Assert.AreEqual(1, fixture.DispatchReads);
        Assert.IsFalse(fixture.Budget.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "SettleAsync"));
    }

    [TestMethod]
    public async Task Authority_lost_after_new_reservation_settles_known_zero_without_cancellation()
    {
        var fixture = new Fixture(false) { LoseAuthority = true };
        Assert.IsNull(await fixture.ReserveAsync());
        await fixture.Budget.Received(1).SettleAsync(fixture.OperationId, new AiOperationUsage(0, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    public async Task Cancelled_after_reservation_settles_zero_and_propagates_cancellation()
    {
        var fixture = new Fixture(false);
        using var cancellation = new CancellationTokenSource();
        fixture.Budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            cancellation.Cancel();
            return new AiOperationReservation(fixture.OperationId, true, "reserved", 100, null, null);
        });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.ReserveAsync(cancellation.Token));
        await fixture.Budget.Received(1).SettleAsync(fixture.OperationId, new AiOperationUsage(0, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    public async Task Unknown_reservation_response_is_not_inferred_as_zero_usage()
    {
        var fixture = new Fixture(false);
        fixture.Budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromException<AiOperationReservation>(new IOException("lost response")));
        await Assert.ThrowsExactlyAsync<IOException>(() => fixture.ReserveAsync());
        Assert.IsFalse(fixture.Budget.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "SettleAsync"));
    }

    [TestMethod]
    public async Task Batch_conflict_prevents_any_budget_reservation()
    {
        var fixture = new Fixture(false) { WrongBatchHash = true };
        Assert.IsNull(await fixture.ReserveAsync());
        Assert.IsFalse(fixture.Budget.ReceivedCalls().Any());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Classified_duplicate_batch_recovers_the_same_operation(bool tenantScope)
    {
        var fixture = new Fixture(tenantScope);
        fixture.Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            fixture.Capture(call[1]);
            return Task.FromException<int>(new DataCommandException(DataCommandFailureKind.UniqueConstraint, new IOException("collision")));
        });
        var hash = new string('A', 64);
        var row = await fixture.Batches.PrepareAsync(fixture.Binding, fixture.TaskId, fixture.Lease, 0, hash, 2, CancellationToken.None);
        Assert.IsNotNull(row); Assert.AreEqual(fixture.OperationId, row.Id); Assert.AreEqual(hash, row.RequestHash);
    }

    [TestMethod]
    [DataRow(-1, 1, "valid")]
    [DataRow(4096, 1, "valid")]
    [DataRow(0, 0, "valid")]
    [DataRow(0, 17, "valid")]
    [DataRow(0, 1, "bad")]
    public async Task Invalid_batch_is_rejected_before_sql(int ordinal, int count, string hash)
    {
        var fixture = new Fixture(false);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Batches.PrepareAsync(fixture.Binding, fixture.TaskId, fixture.Lease,
            ordinal, hash == "valid" ? new string('A', 64) : "wrong", count, CancellationToken.None));
        Assert.IsFalse(fixture.Query.ReceivedCalls().Any()); Assert.IsFalse(fixture.Command.ReceivedCalls().Any());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Native_reader_preserves_batch_operation_identity(bool mysql)
    {
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid)); table.Columns.Add("IndexTaskId", typeof(Guid));
        table.Columns.Add("BatchOrdinal", typeof(int)); table.Columns.Add("RequestHash", typeof(string));
        table.Columns.Add("InputCount", typeof(int)); table.Columns.Add("CreatedAtUtc", mysql ? typeof(DateTime) : typeof(DateTimeOffset));
        var id = Guid.CreateVersion7(); var task = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
        table.Rows.Add(id, task, 4095, new string('A', 64), 16, mysql ? DateTime.SpecifyKind(now.UtcDateTime, DateTimeKind.Unspecified) : (object)now);
        using var reader = table.CreateDataReader(); Assert.IsTrue(reader.Read());
        var row = AiBudgetRowReaders.ReadKnowledgeIndexBatch(reader);
        Assert.AreEqual(id, row.Id); Assert.AreEqual(task, row.IndexTaskId); Assert.AreEqual(4095, row.BatchOrdinal);
        Assert.AreEqual(new string('A', 64), row.RequestHash); Assert.AreEqual(16, row.InputCount); Assert.AreEqual(now, row.CreatedAtUtc);
    }

    private sealed class Fixture
    {
        internal readonly IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Command = Substitute.For<ICommandExecutor>();
        internal readonly IAiOperationBudgetStore Budget = Substitute.For<IAiOperationBudgetStore>();
        internal readonly Guid TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7(), OperationId = Guid.CreateVersion7();
        internal readonly AiKnowledgeIndexTaskBinding Binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        internal readonly AiModelConfigRecord Model = new() { Id = Guid.CreateVersion7(), Version = 1, IsEnabled = true, ProviderKey = "ollama", ModelId = "model", EndpointBaseUrl = "https://provider.test" };
        internal readonly string[] Inputs = ["精确输入", "第二段"];
        internal readonly AiKnowledgeIndexBatchStore Batches;
        private readonly AiKnowledgeIndexBudgetService service;
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        private Dictionary<string, object?>? persisted;
        internal int DispatchReads;
        internal bool LoseAuthority, WrongBatchHash, PersistedBeforeReserve;

        internal Fixture(bool tenantScope)
        {
            var tenant = new CurrentTenantAccessor();
            if (tenantScope) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "test", "Test")); else tenant.SetHost();
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(OperationId);
            var tasks = new AiKnowledgeIndexTaskStore(Query, Command, tenant, clock, ids);
            Batches = new(Query, Command, tasks, ids); service = new(Query, tasks, Batches, Budget);
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                DispatchReads++;
                return LoseAuthority && DispatchReads > 1 ? null : new AiKnowledgeIndexDispatchRecord
                {
                    TaskId = TaskId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId,
                    DocumentVersion = 1, KnowledgeBaseVersion = 1, ModelConfigId = Model.Id, ModelConfigVersion = 1,
                    LeaseToken = Lease, LeaseExpiresAtUtc = now.AddSeconds(30),
                    DataClassification = "internal", ModelProviderKey = Model.ProviderKey, ModelId = Model.ModelId,
                    ModelEndpointBaseUrl = Model.EndpointBaseUrl, ModelOrganizationId = Model.OrganizationId,
                };
            });
            Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Model);
            Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call => { Capture(call[1]); return 1; });
            Query.QuerySingleOrDefaultAsync<AiKnowledgeIndexBatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => new AiKnowledgeIndexBatchRecord
            {
                Id = OperationId, IndexTaskId = TaskId, BatchOrdinal = (int)persisted!["BatchOrdinal"]!,
                RequestHash = WrongBatchHash ? new string('F', 64) : (string)persisted["RequestHash"]!, InputCount = (int)persisted["InputCount"]!,
            });
            Budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                PersistedBeforeReserve = persisted is not null;
                return new AiOperationReservation(OperationId, true, "reserved", 100, null, null);
            });
        }
        internal void Capture(object? parameters) => persisted = (Dictionary<string, object?>)parameters!;
        internal Task<AiKnowledgeIndexBudgetAdmission?> ReserveAsync(CancellationToken token = default) => service.ReserveAsync(Binding, TaskId, Lease, 0, Inputs, token);
    }
}
