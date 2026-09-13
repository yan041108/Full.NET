using Full.NET.Agents.Approvals;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>审批一次性消费与并发竞争；相同审批最多成功消费一次。</summary>
internal static class AiApprovalRecoveryAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        await Concurrent_consume_allows_only_one_success_async(factory).ConfigureAwait(false);
        await Consumed_approval_cannot_be_reused_async(factory).ConfigureAwait(false);
    }

    private static async Task Concurrent_consume_allows_only_one_success_async(FullNetApiFactory factory)
    {
        var approvalId = Guid.CreateVersion7();
        await SeedApprovedApprovalAsync(factory, approvalId).ConfigureAwait(false);
        var results = await Task.WhenAll(
            ConsumeAsync(factory, approvalId, 1),
            ConsumeAsync(factory, approvalId, 1)).ConfigureAwait(false);
        Assert.AreEqual(1, results.Count(success => success));
        Assert.AreEqual(1, results.Count(success => !success));
    }

    private static async Task Consumed_approval_cannot_be_reused_async(FullNetApiFactory factory)
    {
        var approvalId = Guid.CreateVersion7();
        await SeedApprovedApprovalAsync(factory, approvalId).ConfigureAwait(false);
        Assert.IsTrue(await ConsumeAsync(factory, approvalId, 1).ConfigureAwait(false));
        Assert.IsFalse(await ConsumeAsync(factory, approvalId, 2).ConfigureAwait(false));
    }

    private static async Task<bool> ConsumeAsync(FullNetApiFactory factory, Guid approvalId, long expectedVersion)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var affected = await commands.ExecuteAsync(
            AiAgentApprovalSql.Consume,
            AiSqlParameters.Create(("Id", approvalId), ("ExpectedVersion", expectedVersion), ("Now", clock.UtcNow)),
            CancellationToken.None).ConfigureAwait(false);
        return affected == 1;
    }

    private static async Task SeedApprovedApprovalAsync(FullNetApiFactory factory, Guid approvalId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ids = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;
        await commands.ExecuteAsync(
            AiAgentApprovalSql.Insert,
            AiSqlParameters.Create(
                ("Id", approvalId),
                ("ScopeKey", "host"),
                ("TenantId", null),
                ("RunId", ids.NewId()),
                ("OperationId", ids.NewId()),
                ("SessionId", ids.NewId()),
                ("ToolName", "ai.chat.sessions.rename"),
                ("ToolVersion", 1),
                ("ArgumentsHash", "ABC123"),
                ("ArgumentsProtected", """{"sessionId":"00000000-0000-0000-0000-000000000001","title":"approved"}"""),
                ("PolicyVersion", AgentApprovalGate.CurrentPolicyVersion),
                ("PresentationJson", """{"action":"Rename","target":"old","change":"approved","scope":"session","costCeiling":null,"currency":null}"""),
                ("RequestedBy", ids.NewId()),
                ("DecisionKey", AgentApprovalDecisionKeys.Approved),
                ("ExpiresAtUtc", now.AddHours(1)),
                ("Now", now)),
            CancellationToken.None).ConfigureAwait(false);
    }
}
