using Full.NET.Agents.Approvals;
using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Microsoft.Extensions.Options;
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
        await Current_admission_and_failed_write_preserve_approval_async(factory).ConfigureAwait(false);
    }

    private static async Task Current_admission_and_failed_write_preserve_approval_async(FullNetApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        tenant.SetHost();
        try
        {
            var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var actor = new ToolActor(Guid.CreateVersion7(), null, Guid.CreateVersion7());
            var auth = new CurrentAuthorization { Actor = actor };
            var sessionId = Guid.CreateVersion7(); var runId = Guid.CreateVersion7(); var operationId = Guid.CreateVersion7();
            await commands.ExecuteAsync(AiChatSql.InsertSession, AiSqlParameters.Create(
                ("Id", sessionId), ("TenantId", null), ("OwnerUserId", actor.UserId), ("ModelConfigId", Guid.CreateVersion7()),
                ("ModelName", "admission-fixture"), ("Title", "old"), ("CreatedAtUtc", clock.UtcNow)), default);
            var json = JsonSerializer.Serialize(new { sessionId, title = "approved" });
            using var args = JsonDocument.Parse(json);
            var invocation = new ToolInvocation(operationId, runId, "ai.chat.sessions.rename", 1, args.RootElement);
            await commands.ExecuteAsync(AiAgentApprovalSql.Insert, AiSqlParameters.Create(
                ("Id", Guid.CreateVersion7()), ("ScopeKey", "host"), ("TenantId", null), ("RunId", runId),
                ("OperationId", operationId), ("SessionId", actor.SessionId), ("ToolName", invocation.ToolName),
                ("ToolVersion", 1), ("ArgumentsHash", AgentApprovalGate.ComputeArgumentsHash(args.RootElement)),
                ("ArgumentsProtected", json), ("PolicyVersion", AgentApprovalGate.CurrentPolicyVersion),
                ("PresentationJson", """{"action":"Rename","target":"old","change":"approved","scope":"session","costCeiling":null,"currency":null,"targetVersion":1}"""),
                ("RequestedBy", actor.UserId), ("DecisionKey", AgentApprovalDecisionKeys.Approved),
                ("ExpiresAtUtc", clock.UtcNow.AddHours(1)), ("Now", clock.UtcNow)), default);
            var consumption = new AiAgentApprovalConsumption(queries, commands, clock, tenant, auth);
            var gate = new AiAgentRunApprovalGate(queries, tenant, clock, auth, Options.Create(new DatabaseOptions { Provider = factory.Provider }));
            Assert.IsTrue(await gate.HasApprovedUnconsumedForRunAsync(runId, actor.UserId));
            Assert.AreEqual(AgentApprovalExecutionStatus.Approved, await consumption.ValidateForToolExecutionAsync(invocation, actor, default));
            auth.Actor = null;
            Assert.IsFalse(await gate.HasApprovedUnconsumedForRunAsync(runId, actor.UserId));
            Assert.AreEqual(AgentApprovalExecutionStatus.Denied, await consumption.ValidateForToolExecutionAsync(invocation, actor, default));
            auth.Actor = actor with { SessionId = Guid.CreateVersion7() };
            Assert.IsFalse(await gate.HasApprovedUnconsumedForRunAsync(runId, actor.UserId));
            auth.Actor = actor;
            // 历史缺版本和重复字段的持久快照均拒绝，不写回或消费。
            foreach (var snapshot in new[]
            {
                """{"action":"Rename","target":"old","change":"approved","scope":"session","costCeiling":null,"currency":null}""",
                """{"action":"Rename","target":"old","change":"approved","scope":"session","costCeiling":null,"currency":null,"targetVersion":1,"targetVersion":1}""",
            })
            {
                var invalidRun = Guid.CreateVersion7(); var invalidOperation = Guid.CreateVersion7();
                var invalidInvocation = invocation with { RunId = invalidRun, OperationId = invalidOperation };
                await commands.ExecuteAsync(AiAgentApprovalSql.Insert, AiSqlParameters.Create(
                    ("Id", Guid.CreateVersion7()), ("ScopeKey", "host"), ("TenantId", null), ("RunId", invalidRun),
                    ("OperationId", invalidOperation), ("SessionId", actor.SessionId), ("ToolName", invocation.ToolName),
                    ("ToolVersion", 1), ("ArgumentsHash", AgentApprovalGate.ComputeArgumentsHash(args.RootElement)),
                    ("ArgumentsProtected", json), ("PolicyVersion", AgentApprovalGate.CurrentPolicyVersion),
                    ("PresentationJson", snapshot), ("RequestedBy", actor.UserId),
                    ("DecisionKey", AgentApprovalDecisionKeys.Approved), ("ExpiresAtUtc", clock.UtcNow.AddHours(1)),
                    ("Now", clock.UtcNow)), default);
                Assert.IsFalse(await gate.HasApprovedUnconsumedForRunAsync(invalidRun, actor.UserId));
                Assert.AreEqual(AgentApprovalExecutionStatus.Denied, await consumption.ValidateForToolExecutionAsync(invalidInvocation, actor, default));
                Assert.IsFalse(await consumption.TryConsumeForToolExecutionAsync(invalidInvocation, actor, default));
                var invalid = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(AiAgentApprovalSql.FindByOperation,
                    AiSqlParameters.Create(("OperationId", invalidOperation)), default);
                Assert.IsNull(invalid!.ConsumedAtUtc); Assert.AreEqual(1L, invalid.Version);
                Assert.AreEqual(snapshot, invalid.PresentationJson);
            }
            // 使用真实数据库事务：审批已消费但目标版本冲突时，整个本地结果事务必须回滚。
            var failed = await transaction.ExecuteResultAsync<bool>(async token =>
            {
                Assert.IsTrue(await consumption.TryConsumeForToolExecutionAsync(invocation, actor, token));
                var changed = await commands.ExecuteAsync(AiChatSql.UpdateSessionTitle, AiSqlParameters.Create(
                    ("SessionId", sessionId), ("OwnerUserId", actor.UserId), ("ScopeTenantId", null),
                    ("Title", "approved"), ("UpdatedAtUtc", clock.UtcNow), ("Version", 99)), token);
                Assert.AreEqual(0, changed);
                return Result<bool>.Failure(new Error("fixture.version_conflict", "Expected version conflict.", ErrorType.Conflict));
            }, default);
            Assert.IsFalse(failed.IsSuccess);
            var approval = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(AiAgentApprovalSql.FindByOperation,
                AiSqlParameters.Create(("OperationId", operationId)), default);
            Assert.IsNotNull(approval); Assert.IsNull(approval.ConsumedAtUtc); Assert.AreEqual(1L, approval.Version);
            Assert.AreEqual(1, await commands.ExecuteAsync(AiChatSql.UpdateSessionTitle, AiSqlParameters.Create(
                ("SessionId", sessionId), ("OwnerUserId", actor.UserId), ("ScopeTenantId", null),
                ("Title", "changed"), ("UpdatedAtUtc", clock.UtcNow), ("Version", 1)), default));
            Assert.IsFalse(await gate.HasApprovedUnconsumedForRunAsync(runId, actor.UserId));
            Assert.IsFalse(await consumption.TryConsumeForToolExecutionAsync(invocation, actor, default));
            approval = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(AiAgentApprovalSql.FindByOperation,
                AiSqlParameters.Create(("OperationId", operationId)), default);
            Assert.IsNull(approval!.ConsumedAtUtc); Assert.AreEqual(1L, approval.Version);

            // 标题恢复原值时版本已前进，旧审批不可复用，也不可从当前目标补造版本。
            Assert.AreEqual(1, await commands.ExecuteAsync(AiChatSql.UpdateSessionTitle, AiSqlParameters.Create(
                ("SessionId", sessionId), ("OwnerUserId", actor.UserId), ("ScopeTenantId", null),
                ("Title", "old"), ("UpdatedAtUtc", clock.UtcNow), ("Version", 2)), default));
            Assert.IsFalse(await gate.HasApprovedUnconsumedForRunAsync(runId, actor.UserId));
            Assert.AreEqual(AgentApprovalExecutionStatus.Denied, await consumption.ValidateForToolExecutionAsync(invocation, actor, default));
            Assert.IsFalse(await consumption.TryConsumeForToolExecutionAsync(invocation, actor, default));
            approval = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(AiAgentApprovalSql.FindByOperation,
                AiSqlParameters.Create(("OperationId", operationId)), default);
            Assert.IsNull(approval!.ConsumedAtUtc); Assert.AreEqual(1L, approval.Version);

            // 新操作绑定当前版本；旧审批保持原快照与未消费状态。
            var previousOperation = operationId;
            operationId = Guid.CreateVersion7();
            invocation = invocation with { OperationId = operationId };
            await commands.ExecuteAsync(AiAgentApprovalSql.Insert, AiSqlParameters.Create(
                ("Id", Guid.CreateVersion7()), ("ScopeKey", "host"), ("TenantId", null), ("RunId", runId),
                ("OperationId", operationId), ("SessionId", actor.SessionId), ("ToolName", invocation.ToolName),
                ("ToolVersion", 1), ("ArgumentsHash", AgentApprovalGate.ComputeArgumentsHash(args.RootElement)),
                ("ArgumentsProtected", json), ("PolicyVersion", AgentApprovalGate.CurrentPolicyVersion),
                ("PresentationJson", """{"action":"Rename","target":"old","change":"approved","scope":"session","costCeiling":null,"currency":null,"targetVersion":3}"""),
                ("RequestedBy", actor.UserId), ("DecisionKey", AgentApprovalDecisionKeys.Approved),
                ("ExpiresAtUtc", clock.UtcNow.AddHours(1)), ("Now", clock.UtcNow)), default);
            Assert.IsTrue(await gate.HasApprovedUnconsumedForRunAsync(runId, actor.UserId));
            // 带派生答案的目标仍可执行新批准；工具事务不得尝试交付历史正文。
            var messageId = Guid.CreateVersion7();
            await commands.ExecuteAsync(new SqlStatement("fixture.insert_knowledge_answer", """
                INSERT INTO fn_ai_chat_message (Id, SessionId, RoleKey, Content, StatusKey, CreatedAtUtc, IsKnowledgeAnswer, KnowledgeLineage)
                VALUES (@Id, @SessionId, 'assistant', @Content, 'completed', @Now, 1, NULL)
                """, SqlDataScope.Global), AiSqlParameters.Create(("Id", messageId), ("SessionId", sessionId),
                ("Content", "不可交付的旧知识答案"), ("Now", clock.UtcNow)), default);
            var sessionQueries = scope.ServiceProvider.GetRequiredService<AiChatSessionQueryService>();
            var handler = new RenameChatSessionToolHandler(scope.ServiceProvider.GetRequiredService<AiChatSessionManagementService>(),
                sessionQueries, consumption, transaction, tenant);
            var result = await handler.ExecuteAsync(invocation, actor, default);
            Assert.AreEqual("approved", result.GetProperty("title").GetString());
            approval = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(AiAgentApprovalSql.FindByOperation,
                AiSqlParameters.Create(("OperationId", operationId)), default);
            Assert.IsNotNull(approval!.ConsumedAtUtc); Assert.AreEqual(2L, approval.Version);
            var previous = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(AiAgentApprovalSql.FindByOperation,
                AiSqlParameters.Create(("OperationId", previousOperation)), default);
            Assert.IsNull(previous!.ConsumedAtUtc); Assert.AreEqual(1L, previous.Version);
            var detail = await sessionQueries.GetByIdAsync(sessionId, actor.UserId);
            Assert.IsTrue(detail.IsSuccess); Assert.AreEqual("approved", detail.Value!.Title); Assert.AreEqual(4, detail.Value.Version);
            Assert.AreEqual("", detail.Value.Messages.Single().Content);
            Assert.AreEqual(AiErrorCodes.KnowledgeAnswerUnavailable, detail.Value.Messages.Single().KnowledgeUnavailableCode);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await handler.ExecuteAsync(invocation, actor, default));
        }
        finally { tenant.Clear(); }
    }

    /// <summary>只控制 Contract 授权结果，审批、目标和回滚均走真实双库实现。</summary>
    private sealed class CurrentAuthorization : IToolAuthorizationPort
    {
        internal ToolActor? Actor;
        public ValueTask<ToolActor?> AuthorizeAsync(string permissionCode, CancellationToken token) =>
            ValueTask.FromResult(permissionCode == AiChatPermissions.Update ? Actor : null);
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
