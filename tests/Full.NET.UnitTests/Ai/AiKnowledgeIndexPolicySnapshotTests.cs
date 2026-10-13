using System.Security.Cryptography;
using System.Text;
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

/// <summary>分类与实际目的地必须沿同一准入快照保持一致；拒绝和既有 unknown 不能产生新派发资格。</summary>
[TestClass]
public sealed class AiKnowledgeIndexPolicySnapshotTests
{
    [TestMethod]
    [DataRow("text", "")] [DataRow("text", "INTERNAL")] [DataRow("text", "internal ")] [DataRow("text", "unknown")] [DataRow("text", null)]
    [DataRow("pdf", "")] [DataRow("pdf", "INTERNAL")] [DataRow("pdf", "internal ")] [DataRow("pdf", "unknown")] [DataRow("pdf", null)]
    [DataRow("docx", "")] [DataRow("docx", "INTERNAL")] [DataRow("docx", "internal ")] [DataRow("docx", "unknown")] [DataRow("docx", null)]
    public void Index_policy_chunking_rejects_unknown_classification(string format, string? classification)
    {
        var result = Chunk(format, classification);
        Assert.IsFalse(result.Success); Assert.AreEqual(AiKnowledgeModelChunkFailure.ModelNotApproved, result.Failure);
    }

    [TestMethod]
    [DataRow("text", "public")] [DataRow("text", "internal")] [DataRow("text", "restricted")]
    [DataRow("pdf", "public")] [DataRow("pdf", "internal")] [DataRow("pdf", "restricted")]
    [DataRow("docx", "public")] [DataRow("docx", "internal")] [DataRow("docx", "restricted")]
    public void Index_policy_known_classification_preserves_approved_model(string format, string classification)
    {
        Assert.IsTrue(Chunk(format, classification).Success);
    }

    [TestMethod]
    [DataRow(false, "classification")] [DataRow(true, "classification")]
    [DataRow(false, "provider")] [DataRow(true, "provider")]
    [DataRow(false, "model")] [DataRow(true, "model")]
    [DataRow(false, "endpoint")] [DataRow(true, "endpoint")]
    [DataRow(false, "organization")] [DataRow(true, "organization")]
    public async Task Index_policy_projection_mismatch_rejects_before_identity_and_budget(bool scoped, string fault)
    {
        var f = new Fixture(scoped); f.Current = f.Source(fault, invalidClassification: true);
        Assert.IsNull(await f.Reserve()); Assert.AreEqual(0, f.Ids.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count()); Assert.AreEqual(0, f.Budget.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false, "classification")] [DataRow(true, "classification")]
    [DataRow(false, "provider")] [DataRow(true, "provider")]
    [DataRow(false, "model")] [DataRow(true, "model")]
    [DataRow(false, "endpoint")] [DataRow(true, "endpoint")]
    [DataRow(false, "organization")] [DataRow(true, "organization")]
    public async Task Index_policy_drift_during_new_reservation_settles_known_zero(bool scoped, string fault)
    {
        var f = new Fixture(scoped) { AfterReservation = fault };
        Assert.IsNull(await f.Reserve()); Assert.IsTrue(f.Persisted);
        await f.Budget.Received(1).ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>());
        await f.Budget.Received(1).SettleAsync(f.OperationId, new AiOperationUsage(0, 0), "cancelled", CancellationToken.None);
    }

    [TestMethod]
    [DataRow("classification")] [DataRow("provider")] [DataRow("model")] [DataRow("endpoint")] [DataRow("organization")]
    public void Index_policy_shared_snapshot_comparison_detects_policy_drift(string fault)
    {
        var f = new Fixture(false);
        Assert.IsFalse(AiKnowledgeIndexEmbeddingService.SameSource(f.Current, f.Source(fault)));
    }

    [TestMethod]
    [DataRow(false, "public")] [DataRow(true, "public")]
    [DataRow(false, "internal")] [DataRow(true, "internal")]
    [DataRow(false, "restricted")] [DataRow(true, "restricted")]
    public async Task Index_policy_known_classification_keeps_original_budget_request(bool scoped, string classification)
    {
        var f = new Fixture(scoped); f.Current = f.Source(classification: classification);
        var result = await f.Reserve(); Assert.IsNotNull(result); Assert.IsNotNull(result.Source);
        Assert.AreEqual(AiEmbeddingBudgetRequest.Create(f.OperationId, f.Model, ["payload"]), result.Request);
        await f.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Index_policy_existing_unknown_cannot_be_released_as_zero_after_drift(bool scoped)
    {
        var f = new Fixture(scoped) { AfterReservation = "classification", Existing = true };
        var result = await f.Reserve(); Assert.IsNotNull(result); Assert.IsNull(result.Source);
        Assert.IsFalse(result.Reservation.IsNew); Assert.AreEqual("unknown", result.Reservation.UsageStatus);
        await f.Budget.DidNotReceive().SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Index_policy_valid_lease_renewal_does_not_change_policy_identity()
    {
        var f = new Fixture(false) { AfterReservation = "renewal" };
        Assert.IsNotNull((await f.Reserve())?.Source);
    }

    [TestMethod]
    public async Task Index_policy_zero_settlement_failure_prevents_admission()
    {
        var f = new Fixture(false) { AfterReservation = "endpoint" }; var original = new IOException("cleanup failed");
        f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(original));
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<IOException>(f.Reserve));
    }

    [TestMethod]
    public async Task Index_policy_uri_equivalence_cannot_replace_the_exact_budget_binding()
    {
        var f = new Fixture(false); f.Current = f.Source("equivalent_endpoint");
        Assert.IsNull(await f.Reserve()); Assert.AreEqual(0, f.Budget.ReceivedCalls().Count());
    }

    private static (bool Success, AiKnowledgeModelChunkFailure Failure) Chunk(string format, string? classification)
    {
        var model = new AiModelConfigRecord { Id = Guid.CreateVersion7(), Version = 1, IsEnabled = true,
            ProviderKey = "openai_compatible", ModelId = "text-embedding-3-small", EndpointBaseUrl = "https://api.openai.com/v1" };
        var knowledge = new AiKnowledgeBaseRecord { Id = Guid.CreateVersion7(), Version = 1, IsEnabled = true,
            DataClassification = classification!, EmbeddingModelConfigId = model.Id, EmbeddingModelVersion = model.Version };
        const string text = "hello"; var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        if (format == "pdf")
        {
            var result = AiKnowledgePdfModelChunker.Chunk(knowledge, model, new(hash, 97, [new(1, text)]), new(), default);
            return (result.IsSuccess, result.Failure);
        }
        if (format == "docx")
        {
            var result = AiKnowledgeDocxModelChunker.Chunk(knowledge, model, new(hash, 97, text + "\n", [new(0, "paragraph", 0, 5, 1, null, null, null, 0, false)]), new(), default);
            return (result.IsSuccess, result.Failure);
        }
        var chunked = AiKnowledgeModelChunker.Chunk(knowledge, model, new(text, hash, 5, AiKnowledgeTextSource.Utf8), AiKnowledgeTextFormat.PlainText, new(), default);
        return (chunked.IsSuccess, chunked.Failure);
    }

    /// <summary>使用真实任务/批次服务与受控权威查询；变化只发生在预留等待窗口，避免由旧租约伪造拒绝。</summary>
    private sealed class Fixture
    {
        internal readonly Guid TaskId = Guid.CreateVersion7(), Lease = Guid.CreateVersion7(), OperationId = Guid.CreateVersion7();
        internal readonly AiKnowledgeIndexTaskBinding Binding = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        internal readonly AiModelConfigRecord Model = new() { Id = Guid.CreateVersion7(), Version = 1, IsEnabled = true,
            ProviderKey = "ollama", ModelId = "model", EndpointBaseUrl = "https://provider.test", OrganizationId = "org-a" };
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly IAiOperationBudgetStore Budget = Substitute.For<IAiOperationBudgetStore>();
        internal readonly IIdGenerator Ids = Substitute.For<IIdGenerator>();
        internal AiKnowledgeIndexDispatchRecord Current;
        internal string? AfterReservation; internal bool Existing; internal bool Persisted;
        private readonly AiKnowledgeIndexBudgetService service;
        private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
        private IReadOnlyDictionary<string, object?>? batch;
        internal Fixture(bool scoped)
        {
            Current = Source(); var query = Substitute.For<IQueryExecutor>(); var tenant = new CurrentTenantAccessor();
            if (scoped) tenant.SetTenant(new(Guid.CreateVersion7(), "test", "测试")); else tenant.SetHost();
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now); Ids.NewId().Returns(OperationId);
            var tasks = new AiKnowledgeIndexTaskStore(query, Commands, tenant, clock, Ids);
            service = new(query, tasks, new(query, Commands, tasks, Ids), Budget);
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexDispatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Current);
            query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Model);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            { batch = call.ArgAt<IReadOnlyDictionary<string, object?>>(1); Persisted = true; return 1; });
            query.QuerySingleOrDefaultAsync<AiKnowledgeIndexBatchRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
                new AiKnowledgeIndexBatchRecord { Id = OperationId, IndexTaskId = TaskId, BatchOrdinal = 0, InputCount = 1, RequestHash = (string)batch!["RequestHash"]! });
            Budget.ReserveAsync(Arg.Any<AiOperationRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
            { if (AfterReservation is not null) Current = Source(AfterReservation); return new AiOperationReservation(OperationId, !Existing, Existing ? "unknown" : "reserved", 100, null, null); });
        }
        internal Task<AiKnowledgeIndexBudgetAdmission?> Reserve() => service.ReserveAsync(Binding, TaskId, Lease, 0, ["payload"], default);
        internal AiKnowledgeIndexDispatchRecord Source(string? fault = null, bool invalidClassification = false, string classification = "internal") => new()
        {
            TaskId = TaskId, DocumentId = Binding.DocumentId, VersionId = Binding.VersionId, DocumentVersion = 1, KnowledgeBaseVersion = 1,
            ModelConfigId = Model.Id, ModelConfigVersion = 1, LeaseToken = Lease, LeaseExpiresAtUtc = now.AddMinutes(fault == "renewal" ? 2 : 1),
            FileId = Binding.VersionId, ContentHash = new('a', 64), ContentType = "text/plain", SizeBytes = 5,
            DataClassification = fault == "classification" ? invalidClassification ? "INTERNAL" : "restricted" : classification,
            ModelProviderKey = fault == "provider" ? "openai_compatible" : Model.ProviderKey,
            ModelId = fault == "model" ? "changed-model" : Model.ModelId,
            ModelEndpointBaseUrl = fault == "endpoint" ? "https://other.test" : fault == "equivalent_endpoint" ? "HTTPS://PROVIDER.TEST" : Model.EndpointBaseUrl,
            ModelOrganizationId = fault == "organization" ? "org-b" : Model.OrganizationId,
        };
    }
}
