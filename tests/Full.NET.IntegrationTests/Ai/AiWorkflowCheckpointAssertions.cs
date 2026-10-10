using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Workflows;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>双库验证真实检查点与运行元数据的写入、读取及恢复准入。</summary>
internal static class AiWorkflowCheckpointAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        var runId = await SeedWorkflowRunAsync(factory).ConfigureAwait(false);
        await Roundtrip_checkpoint_payload_async(factory, runId).ConfigureAwait(false);
        await Node_receipt_intent_atomicity_and_reclaim_async(factory).ConfigureAwait(false);
        await Event_sequence_without_checkpoint_async(factory).ConfigureAwait(false);
    }

    private static async Task Node_receipt_intent_atomicity_and_reclaim_async(FullNetApiFactory factory)
    {
        var runId = await SeedWorkflowRunAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<AiAgentRunStore>();
        var run = (await store.FindByIdAsync(runId))!;
        var definition = AgentWorkflowRegistry.Resolve(run.DefinitionKey, run.DefinitionVersion)!;
        var state = AgentWorkflowState.Create(run.SessionId);
        state.Binding = new(run.Id, run.DefinitionKey, run.DefinitionVersion, AgentWorkflowNodeReceipt.DigestDefinition(definition),
            Guid.Parse("00000000-0000-0000-0000-000000000001"), 1);
        state.PendingNode = new(0, "read_sessions", AgentWorkflowNodeReceipt.ResolveOperationId(runId, "read_sessions"),
            AgentWorkflowNodeReceipt.Digest("{}"), null, 1, null, null);
        var lease = (await store.TryAcquireAsync(runId, "node-owner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
        Assert.IsFalse((await store.ScanQueuedAsync(100, DateTimeOffset.UtcNow)).Contains(runId));
        var firstPayload = state.ToJson();
        Assert.IsTrue(await store.CommitProgressAsync(new(lease, "running", null,
            new(Guid.CreateVersion7(), 1, 1, AgentFrameworkRuntime.FrameworkVersion, run.DefinitionVersion, firstPayload, AgentWorkflowNodeReceipt.Digest(firstPayload)),
            new(Guid.CreateVersion7(), 1, "run.node_started", 1, "{}"))));
        lease = lease with { Version = lease.Version + 1 };
        var pending = AiWorkflowCheckpointAdmission.Restore(run, await store.FindLatestCheckpointAsync(runId));
        Assert.AreEqual("read_sessions", pending!.PendingNode!.NodeKey);
        var receipt = state.PendingNode with { OutputDigest = AgentWorkflowNodeReceipt.Digest("[]") };
        state.PendingNode = null;
        state.NodeReceipts.Add(receipt);
        state.Outputs.Add("read_sessions", "[]");
        state.NextNodeIndex = 1;
        var payload = state.ToJson();
        var step = new AgentStepUpsert(Guid.CreateVersion7(), "read_sessions", 1, receipt.OperationId, "committed", null, 1,
            null, null, null, null, null, null, receipt.InputDigest, null);
        Assert.IsTrue(await store.CommitProgressAsync(new(lease, "running", step,
            new(Guid.CreateVersion7(), 2, 1, AgentFrameworkRuntime.FrameworkVersion, run.DefinitionVersion, payload, AgentWorkflowNodeReceipt.Digest(payload)),
            new(Guid.CreateVersion7(), 2, "run.node_completed", 1, "{}"))));
        lease = lease with { Version = lease.Version + 1 };
        var restored = AiWorkflowCheckpointAdmission.Restore(run, await store.FindLatestCheckpointAsync(runId));
        Assert.AreEqual(1, restored!.NextNodeIndex);
        Assert.AreEqual(receipt, restored.NodeReceipts.Single());
        // 重复步骤故障必须回滚运行版本、检查点与事件，不能留下只有游标成功的半笔提交。
        var stepFailure = await Assert.ThrowsAsync<DataCommandException>(async () => await store.CommitProgressAsync(new(lease, "running", step,
            new(Guid.CreateVersion7(), 3, 1, AgentFrameworkRuntime.FrameworkVersion, run.DefinitionVersion, payload, AgentWorkflowNodeReceipt.Digest(payload)),
            new(Guid.CreateVersion7(), 3, "run.node_completed", 1, "{}"))));
        Assert.AreEqual(DataCommandFailureKind.UniqueConstraint, stepFailure.Kind);
        Assert.IsInstanceOfType<System.Data.Common.DbException>(stepFailure.InnerException);
        Assert.AreEqual(lease.Version, (await store.FindByIdAsync(runId))!.Version);
        Assert.AreEqual(2L, (await store.FindLatestCheckpointAsync(runId))!.Sequence);
        Assert.AreEqual(2L, await store.FindLatestEventSequenceAsync(runId, CancellationToken.None));
        var rolledBackStepId = Guid.CreateVersion7();
        // 事件唯一键在事务末段拒绝，前面已经插入的步骤与检查点也必须一起回滚。
        var eventFailure = await Assert.ThrowsAsync<DataCommandException>(async () => await store.CommitProgressAsync(new(lease, "running",
            step with { StepId = rolledBackStepId, StepKey = "summarize", OperationId = AgentWorkflowNodeReceipt.ResolveOperationId(runId, "summarize") },
            new(Guid.CreateVersion7(), 3, 1, AgentFrameworkRuntime.FrameworkVersion, run.DefinitionVersion, payload, AgentWorkflowNodeReceipt.Digest(payload)),
            new(Guid.CreateVersion7(), 2, "run.node_completed", 1, "{}"))));
        Assert.AreEqual(DataCommandFailureKind.UniqueConstraint, eventFailure.Kind);
        Assert.IsInstanceOfType<System.Data.Common.DbException>(eventFailure.InnerException);
        Assert.AreEqual(lease.Version, (await store.FindByIdAsync(runId))!.Version);
        Assert.AreEqual(2L, (await store.FindLatestCheckpointAsync(runId))!.Sequence);
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<int>(
            new("test.ai.node.step_count", "SELECT COUNT(*) FROM fn_ai_agent_step WHERE Id = @StepId", SqlDataScope.Global),
            AiSqlParameters.Create(("StepId", rolledBackStepId))));
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        Assert.AreEqual(1, await commands.ExecuteAsync(new("test.ai.node.expire", "UPDATE fn_ai_agent_run SET LeaseExpiresAtUtc = @Expired WHERE Id = @RunId", SqlDataScope.Global),
            AiSqlParameters.Create(("RunId", runId), ("Expired", DateTimeOffset.UtcNow.AddMinutes(-1)))));
        Assert.IsTrue((await store.ScanQueuedAsync(100, DateTimeOffset.UtcNow)).Contains(runId));
        // 同样过期但没有新节点凭证的历史运行不能靠检查点缺失猜测为可重放。
        var legacyId = await SeedWorkflowRunAsync(factory);
        Assert.IsNotNull(await store.TryAcquireAsync(legacyId, "legacy-owner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        await commands.ExecuteAsync(new("test.ai.node.expire", "UPDATE fn_ai_agent_run SET LeaseExpiresAtUtc = @Expired WHERE Id = @RunId", SqlDataScope.Global),
            AiSqlParameters.Create(("RunId", legacyId), ("Expired", DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.IsFalse((await store.ScanQueuedAsync(100, DateTimeOffset.UtcNow)).Contains(legacyId));
        var reclaimed = (await store.TryAcquireAsync(runId, "recovery-owner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
        Assert.IsGreaterThan(lease.Epoch, reclaimed.Epoch);
        Assert.IsFalse(await store.CommitProgressAsync(new(lease, "failed", null, null, null)));
        Assert.IsTrue(await store.CommitProgressAsync(new(reclaimed, "failed", null, null,
            new(Guid.CreateVersion7(), await store.FindLatestEventSequenceAsync(runId, CancellationToken.None) + 1, "run.failed", 1, "{}"))));
        Assert.AreEqual("failed", (await store.FindByIdAsync(runId))!.StatusKey);
        Assert.AreEqual(3L, await store.FindLatestEventSequenceAsync(runId, CancellationToken.None));
    }

    private static async Task Event_sequence_without_checkpoint_async(FullNetApiFactory factory)
    {
        var runId = await SeedWorkflowRunAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<AiAgentRunStore>();
        var lease = (await store.TryAcquireAsync(runId, "event-owner", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
        Assert.IsTrue(await store.CommitProgressAsync(new(lease, "awaiting_approval", null, null,
            new(Guid.CreateVersion7(), 1, "run.awaiting_approval", 1, "{}"))));
        Assert.IsNull(await store.FindLatestCheckpointAsync(runId));
        Assert.AreEqual(1L, await store.FindLatestEventSequenceAsync(runId, CancellationToken.None));
        // 模拟合法恢复后再次等待；读取事件而非不存在的检查点来分配序号。
        lease = lease with { Version = lease.Version + 1 };
        Assert.IsTrue(await store.CommitProgressAsync(new(lease, "awaiting_approval", null, null,
            new(Guid.CreateVersion7(), await store.FindLatestEventSequenceAsync(runId, CancellationToken.None) + 1, "run.awaiting_approval", 1, "{}"))));
        lease = lease with { Version = lease.Version + 1 };
        Assert.IsTrue(await store.CommitProgressAsync(new(lease, "authorization_required", null, null,
            new(Guid.CreateVersion7(), await store.FindLatestEventSequenceAsync(runId, CancellationToken.None) + 1, "run.authorization_required", 1, "{}"))));
        Assert.AreEqual(3L, await store.FindLatestEventSequenceAsync(runId, CancellationToken.None));
        Assert.AreEqual("authorization_required", (await store.FindByIdAsync(runId))!.StatusKey);
    }

    private static async Task Roundtrip_checkpoint_payload_async(FullNetApiFactory factory, Guid runId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IAgentRunStore>();
        var run = await scope.ServiceProvider.GetRequiredService<AiAgentRunStore>().FindByIdAsync(runId);
        Assert.IsNotNull(run);
        var state = new AgentWorkflowState
        {
            SessionId = run.SessionId,
            NextNodeIndex = 2,
            Outputs = { ["read_sessions"] = "[]", ["summarize"] = "Title" },
            InputTokens = 4,
            OutputTokens = 2,
        };
        var payload = state.ToJson();
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var lease = await store.TryAcquireAsync(runId, "workflow-checkpoint", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        Assert.IsNotNull(lease);
        var committed = await store.CommitProgressAsync(new(
            lease!,
            "awaiting_approval",
            null,
            new(Guid.CreateVersion7(), 1, AgentCheckpointCompatibility.CurrentCheckpointFormatVersion,
                AgentFrameworkRuntime.FrameworkVersion, AgentWorkflowRegistry.ChatRenameWorkflowVersion, payload, checksum),
            new(Guid.CreateVersion7(), 1, "run.awaiting_approval", 1, """{"reason":"tool_approval_required"}"""))).ConfigureAwait(false);
        Assert.IsTrue(committed);
        var latest = await store.FindLatestCheckpointAsync(runId).ConfigureAwait(false);
        Assert.IsNotNull(latest);
        Assert.AreEqual(AgentWorkflowRegistry.ChatRenameWorkflowVersion, latest.DefinitionVersion);
        Assert.AreEqual(payload, latest!.PayloadProtected);
        Assert.AreEqual(checksum, latest.Checksum);
        var restored = AiWorkflowCheckpointAdmission.Restore(run, latest);
        Assert.IsNotNull(restored);
        Assert.AreEqual(2, restored!.NextNodeIndex);
        Assert.AreEqual("Title", restored.Outputs["summarize"]);
        Assert.AreEqual(4L, restored.InputTokens);
        Assert.AreEqual(2L, restored.OutputTokens);
        Assert.IsNull(AiWorkflowCheckpointAdmission.Restore(run, latest with { DefinitionVersion = 1 }));
        Assert.IsNull(AiWorkflowCheckpointAdmission.Restore(run, latest with { RunId = Guid.CreateVersion7() }));
        Assert.IsNull(AiWorkflowCheckpointAdmission.Restore(run, latest with { FormatVersion = 99 }));
        Assert.IsNull(AiWorkflowCheckpointAdmission.Restore(run, latest with { Checksum = new('0', 64) }));
    }

    private static async Task<Guid> SeedWorkflowRunAsync(FullNetApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
            var runId = ids.NewId();
            var draft = new AgentRunDraft(
                Guid.CreateVersion7(),
                new string('C', 64),
                "host",
                null,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.NewGuid().ToString("N"),
                "host-admin",
                "host",
                AgentWorkflowRegistry.ChatRenameWorkflowKey,
                AgentWorkflowRegistry.ChatRenameWorkflowVersion,
                """{"modelConfigId":"00000000-0000-0000-0000-000000000001","prompt":"","inputTokenLimit":100,"outputTokenLimit":100}""",
                DateTimeOffset.UtcNow.AddHours(1),
                runId);
            await scope.ServiceProvider.GetRequiredService<IAgentRunStore>().CreateOrGetAsync(draft).ConfigureAwait(false);
            return runId;
        }
        finally
        {
            tenant.Clear();
        }
    }
}
