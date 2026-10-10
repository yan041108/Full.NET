using System.Security.Cryptography;
using System.Text;
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
