using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Definitions;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features.ManagePromptVersions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Identity.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Prompt Run Resolver。</summary>
[TestClass]
public sealed class AiPromptRunResolverTests
{
    [TestMethod]
    public async Task Published_version_prepares_exact_binding_and_composes_literal_input()
    {
        var f = new Fixture();
        var prepared = await f.Resolver.PrepareAsync(f.Request, f.Binding, default);
        Assert.IsTrue(prepared.IsSuccess);
        Assert.IsNotNull(prepared.Value);
        Assert.AreEqual(f.Version.Id, prepared.Value.Binding.VersionId);
        Assert.AreEqual(f.Version.BodyHash, prepared.Value.Binding.BodyHash);
        Assert.AreEqual(0, prepared.Value.Binding.Epoch);
        var executed = await f.Resolver.ExecutionAsync(f.Snapshot, AgentDefinitionRegistry.SingleTextKey, f.Binding, f.Model, default);
        Assert.IsTrue(executed.IsSuccess);
        Assert.AreEqual("Before {{input}} After", executed.Value);
    }

    [TestMethod]
    public async Task Publication_switch_preserves_old_run_but_rejects_new_binding_to_old_version()
    {
        var f = new Fixture(); f.ActiveId = Guid.CreateVersion7();
        Assert.IsTrue((await f.ExecuteAsync()).IsSuccess);
        Assert.IsFalse((await f.Resolver.PrepareAsync(f.Request, f.Binding, default)).IsSuccess);
    }

    [TestMethod]
    public async Task Deactivation_and_republication_never_revive_old_run()
    {
        var f = new Fixture(); f.ActiveId = null; f.Epoch = 1;
        Assert.IsFalse((await f.ExecuteAsync()).IsSuccess);
        f.ActiveId = f.Version.Id;
        Assert.IsFalse((await f.ExecuteAsync()).IsSuccess);
        var current = await f.Resolver.PrepareAsync(f.Request, f.Binding, default);
        Assert.IsNotNull(current.Value);
        Assert.AreEqual(1, current.Value.Binding.Epoch);
    }

    [TestMethod]
    public async Task Revoked_precise_use_permission_rejects_create_and_execution()
    {
        var f = new Fixture();
        f.Authorization.AuthorizeAsync(f.Binding, AiPromptPermissions.Use, Arg.Any<CancellationToken>()).Returns((AuthorizedSessionActor?)null);
        Assert.IsFalse((await f.ExecuteAsync()).IsSuccess);
        Assert.IsFalse((await f.Resolver.PrepareAsync(f.Request, f.Binding, default)).IsSuccess);
    }

    [TestMethod]
    public async Task Tenant_or_session_mismatch_rejects_without_reading_body()
    {
        var f = new Fixture();
        f.Authorization.AuthorizeAsync(f.Binding, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(f.Binding.UserId, Guid.CreateVersion7(), f.Binding.SessionId));
        Assert.IsFalse((await f.ExecuteAsync()).IsSuccess);
        await f.Queries.DidNotReceive().QuerySingleOrDefaultAsync<AiPromptVersionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Model_revision_or_disabled_policy_rejects_bound_execution()
    {
        var f = new Fixture(); f.CurrentModelVersion = 2;
        Assert.IsFalse((await f.ExecuteAsync()).IsSuccess);
        f.CurrentModelVersion = 1; f.Enabled = false;
        Assert.IsFalse((await f.ExecuteAsync()).IsSuccess);
    }

    [TestMethod]
    public async Task Wrong_model_row_identity_rejects_preparation()
    {
        var f = new Fixture(); f.CurrentModelId = Guid.CreateVersion7();
        Assert.IsFalse((await f.Resolver.PrepareAsync(f.Request, f.Binding, default)).IsSuccess);
    }

    [TestMethod]
    public async Task Changed_body_or_hash_rejects_execution()
    {
        var f = new Fixture(); f.BodyJson = f.BodyJson.Replace("Before", "Changed", StringComparison.Ordinal);
        Assert.IsFalse((await f.ExecuteAsync()).IsSuccess);
    }

    [TestMethod]
    public async Task Scope_mismatch_and_never_published_version_reject_execution()
    {
        var f = new Fixture(); f.RowScope = "other";
        Assert.IsFalse((await f.ExecuteAsync()).IsSuccess);
        f.RowScope = "host"; f.WasPublished = false;
        Assert.IsFalse((await f.ExecuteAsync()).IsSuccess);
    }

    [TestMethod]
    public async Task Bound_budget_and_definition_reject_escalation()
    {
        var f = new Fixture();
        Assert.IsFalse((await f.Resolver.ExecutionAsync(f.Snapshot with { OutputTokenLimit = 101 }, AgentDefinitionRegistry.SingleTextKey, f.Binding, f.Model, default)).IsSuccess);
        Assert.IsFalse((await f.Resolver.ExecutionAsync(f.Snapshot, AgentDefinitionRegistry.ReadOnlyToolLoopKey, f.Binding, f.Model, default)).IsSuccess);
    }

    [TestMethod]
    public async Task Authorization_inside_existing_transaction_is_rejected()
    {
        var f = new Fixture(); f.Transactions.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.ExecuteAsync());
    }

    [TestMethod]
    [DataRow("fullnet-single-text-v1")][DataRow("fullnet-readonly-tool-loop-v1")][DataRow("fullnet-chat-rename-workflow-v1")]
    public async Task Explicit_static_strategy_preserves_exact_definition_and_execution_binding(string definition)
    {
        var f = new Fixture(definition);
        Assert.IsTrue((await f.Resolver.PrepareAsync(f.Request, f.Binding, default)).IsSuccess);
        var result = await f.Resolver.ExecutionAsync(f.Snapshot, definition, f.Binding, f.Model, default);
        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual(definition == "fullnet-chat-rename-workflow-v1" ? "" : "Before {{input}} After", result.Value);
    }

    private sealed class Fixture
    {
        internal IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        internal IBackgroundSessionAuthorization Authorization { get; } = Substitute.For<IBackgroundSessionAuthorization>();
        internal IDataTransactionState Transactions { get; } = Substitute.For<IDataTransactionState>();
        internal SessionBindingSnapshot Binding { get; } = new(Guid.CreateVersion7(), null, Guid.CreateVersion7(), "stamp", "host", "host");
        private Guid PromptId { get; } = Guid.CreateVersion7();
        private Guid VersionId { get; } = Guid.CreateVersion7();
        private Guid ModelId { get; } = Guid.CreateVersion7();
        internal Guid? ActiveId { get; set; }
        internal int Epoch { get; set; }
        internal int CurrentModelVersion { get; set; } = 1;
        internal Guid CurrentModelId { get; set; }
        internal bool Enabled { get; set; } = true;
        internal bool WasPublished { get; set; } = true;
        internal string RowScope { get; set; } = "host";
        internal string BodyJson { get; set; }
        private string Hash { get; }
        internal AiPromptRunResolver Resolver { get; }
        internal AiModelConfigRecord Model => new() { Id = ModelId, Version = 1, IsEnabled = true, ProviderKey = "ollama", ModelId = "model" };
        internal AiPromptVersionRecord Version => new() { Id = VersionId, PromptId = PromptId, ScopeKey = RowScope, Version = 1, FormatVersion = 1, BodyJson = BodyJson, ModelConfigVersion = 1, ProviderKey = "ollama", ModelId = "model", BodyHash = Hash, WasPublished = WasPublished };
        private readonly string definition = AgentDefinitionRegistry.SingleTextKey;
        private string Input => definition == "fullnet-chat-rename-workflow-v1" ? "" : "{{input}}";
        internal CreateAiAgentRunRequest Request => new(Guid.CreateVersion7(), definition, ModelId, Input, 100, 100, VersionId);
        internal AgentRunBudgetSnapshot Snapshot => new(ModelId, Input, 100, 100, new(PromptId, VersionId, 1, Hash, 0));
        internal Task<Full.NET.Abstractions.Results.Result<string>> ExecuteAsync() => Resolver.ExecutionAsync(Snapshot, AgentDefinitionRegistry.SingleTextKey, Binding, Model, default);
        internal Fixture(string? staticDefinition = null)
        {
            definition = staticDefinition ?? AgentDefinitionRegistry.SingleTextKey;
            ActiveId = VersionId; CurrentModelId = ModelId;
            var body = new AiPromptBody(ModelId, "zh-CN", definition == "fullnet-chat-rename-workflow-v1" ? "{{input}}" : "Before {{input}} After", 100, 100)
            { Execution = staticDefinition is null ? null : AiPromptExecutionPolicy.Resolve(staticDefinition) };
            BodyJson = AiPromptVersionIntegrity.Serialize(body);
            Hash = AiPromptVersionIntegrity.Hash(body, BodyJson, 1, "ollama", "model");
            var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
            Authorization.AuthorizeAsync(Binding, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new AuthorizedSessionActor(Binding.UserId, null, Binding.SessionId));
            Queries.QuerySingleOrDefaultAsync<AiPromptVersionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Version);
            Queries.QuerySingleOrDefaultAsync<AiPromptRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => new AiPromptRecord { Id = PromptId, ScopeKey = "host", Revision = 1, LastVersion = 1, ActiveVersionId = ActiveId, Epoch = Epoch });
            Queries.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => new AiModelConfigRecord { Id = CurrentModelId, Version = CurrentModelVersion, IsEnabled = Enabled, ProviderKey = "ollama", ModelId = "model" });
            Resolver = new(new(Queries), tenant, Authorization, Transactions);
        }
    }
}
