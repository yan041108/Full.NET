using Full.NET.Agents.Workflows;
using NSubstitute;
using Microsoft.Extensions.AI;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod, DoNotParallelize]
    public async Task Telemetry_red_coordinator_wraps_single_model_async()
    {
        using var capture = new AgentTelemetryCapture();
        var fixture = await CreateFixtureAsync();
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        capture.Verify(fixture.RunId, "run", "processed", 3);
    }

    [TestMethod, DoNotParallelize]
    public async Task Telemetry_red_coordinator_observes_authorization_refusal_async()
    {
        using var capture = new AgentTelemetryCapture();
        var fixture = await CreateFixtureAsync(bindingValid: false);
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        capture.Verify(fixture.RunId, "run", "authorization_required", 1);
    }

    [TestMethod, DoNotParallelize]
    public async Task Budget_admission_classification_does_not_create_model_attempt_async()
    {
        using var capture = new AgentTelemetryCapture();
        var fixture = await CreateFixtureAsync(definitionKey: AgentWorkflowRegistry.ChatRenameWorkflowKey,
            checkpointFactory: run =>
            {
                var state = ReceiptState(run, 2); state.OutputTokens = 0;
                state.NodeReceipts[1] = state.NodeReceipts[1] with { OutputTokens = null };
                return Checkpoint(run, state);
            });
        Assert.AreEqual(1, await fixture.Coordinator.ProcessPendingAsync());
        capture.Verify(fixture.RunId, "run", "budget_rejected", 1);
        await fixture.Runner.DidNotReceive().RunAsync(Arg.Any<IChatClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod, DoNotParallelize]
    public async Task Commit_fencing_classification_does_not_claim_processed_async()
    {
        using var capture = new AgentTelemetryCapture(); var fixture = await CreateFixtureAsync(commitSucceeds: false);
        Assert.AreEqual(0, await fixture.Coordinator.ProcessPendingAsync());
        capture.Verify(fixture.RunId, "run", "not_processed", 1);
    }
}
