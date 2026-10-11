using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Features.ManageAgentRuns;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>审批等待后的当前会话、参数、业务归属与消费边界必须失败关闭。</summary>
[TestClass]
public sealed partial class AiApprovalCurrentAdmissionTests
{
    [TestMethod]
    [DataRow("scope")]
    [DataRow("tenant")]
    [DataRow("session")]
    [DataRow("actor")]
    [DataRow("run")]
    [DataRow("tool")]
    [DataRow("tool_version")]
    [DataRow("policy")]
    [DataRow("hash")]
    [DataRow("pending")]
    [DataRow("consumed")]
    [DataRow("expired")]
    [DataRow("version")]
    [DataRow("empty_id")]
    [DataRow("empty_operation")]
    [DataRow("malformed_json")]
    [DataRow("null_title")]
    [DataRow("duplicate_argument")]
    [DataRow("identity_argument")]
    [DataRow("missing_target")]
    [DataRow("target_owner")]
    [DataRow("target_tenant")]
    [DataRow("target_changed")]
    [DataRow("presentation_missing")]
    [DataRow("presentation_changed")]
    public async Task Resume_gate_rejects_stale_or_incompatible_candidate(string change)
    {
        using var f = new Fixture();
        switch (change)
        {
            case "scope": f.Record.ScopeKey = "other"; break;
            case "tenant": f.Record.TenantId = Guid.CreateVersion7(); break;
            case "session": f.Record.SessionId = Guid.CreateVersion7(); break;
            case "actor": f.Record.RequestedBy = Guid.CreateVersion7(); break;
            case "run": f.Record.RunId = Guid.CreateVersion7(); break;
            case "tool": f.Record.ToolName = "unregistered.write"; break;
            case "tool_version": f.Record.ToolVersion = 2; break;
            case "policy": f.Record.PolicyVersion = 2; break;
            case "hash": f.Record.ArgumentsHash = "changed"; break;
            case "pending": f.Record.DecisionKey = "pending"; break;
            case "consumed": f.Record.ConsumedAtUtc = f.Now; break;
            case "expired": f.Record.ExpiresAtUtc = f.Now; break;
            case "version": f.Record.Version = 0; break;
            case "empty_id": f.Record.Id = Guid.Empty; break;
            case "empty_operation": f.Record.OperationId = Guid.Empty; break;
            case "malformed_json": f.Record.ArgumentsProtected = "{"; break;
            case "null_title": f.SetArguments($"{{\"sessionId\":\"{f.TargetId}\",\"title\":null}}"); break;
            case "duplicate_argument": f.SetArguments($"{{\"sessionId\":\"{f.TargetId}\",\"title\":\"one\",\"title\":\"two\"}}"); break;
            case "identity_argument": f.SetArguments($"{{\"sessionId\":\"{f.TargetId}\",\"title\":\"new\",\"tenantId\":null}}"); break;
            case "missing_target": f.Target = null; break;
            case "target_owner": f.Target = new() { Id = f.TargetId, OwnerUserId = Guid.CreateVersion7(), Version = 1 }; break;
            case "target_tenant": f.Target = new() { Id = f.TargetId, OwnerUserId = f.Actor.UserId, TenantId = Guid.CreateVersion7(), Version = 1 }; break;
            case "target_changed": f.Target = new() { Id = f.TargetId, OwnerUserId = f.Actor.UserId, Version = 2, Title = "another" }; break;
            case "presentation_missing": f.Record.PresentationJson = "{}"; break;
            case "presentation_changed": f.Record.PresentationJson = f.Record.PresentationJson.Replace("new", "different"); break;
        }
        Assert.IsFalse(await f.Gate.HasApprovedUnconsumedForRunAsync(f.RunId, f.Actor.UserId));
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("revoked")]
    [DataRow("user")]
    [DataRow("tenant")]
    [DataRow("session")]
    [DataRow("expired_after_authorization")]
    [DataRow("scope_after_authorization")]
    public async Task Resume_gate_checks_current_authorization_after_target_query(string change)
    {
        using var f = new Fixture();
        f.Authorization.AuthorizeAsync(AiChatPermissions.Update, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (change == "expired_after_authorization") f.CurrentTime = f.Record.ExpiresAtUtc;
            if (change == "scope_after_authorization") f.Tenant.SetTenant(new(Guid.CreateVersion7(), "tenant", "租户"));
            return change switch
            {
                "revoked" => null,
                "user" => f.Actor with { UserId = Guid.CreateVersion7() },
                "tenant" => f.Actor with { TenantId = Guid.CreateVersion7() },
                "session" => f.Actor with { SessionId = Guid.CreateVersion7() },
                _ => f.Actor,
            };
        });
        Assert.IsFalse(await f.Gate.HasApprovedUnconsumedForRunAsync(f.RunId, f.Actor.UserId));
    }

    [TestMethod]
    public async Task Resume_gate_accepts_current_candidate_without_consuming_it()
    {
        using var f = new Fixture();
        Assert.IsTrue(await f.Gate.HasApprovedUnconsumedForRunAsync(f.RunId, f.Actor.UserId));
        await f.Authorization.Received(1).AuthorizeAsync(AiChatPermissions.Update, Arg.Any<CancellationToken>());
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("scope")]
    [DataRow("session")]
    [DataRow("version")]
    [DataRow("empty_id")]
    public async Task Consumption_rejects_wrong_scope_session_or_invalid_record(string change)
    {
        using var f = new Fixture();
        if (change == "scope") f.Record.ScopeKey = "other";
        if (change == "session") f.Record.SessionId = Guid.CreateVersion7();
        if (change == "version") f.Record.Version = 0;
        if (change == "empty_id") f.Record.Id = Guid.Empty;
        Assert.IsFalse(await f.Consumption.TryConsumeForToolExecutionAsync(f.Invocation, f.Actor, default));
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Consumption_cancellation_after_binding_query_never_writes()
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.OnApprovalQuery = () => cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await f.Consumption.TryConsumeForToolExecutionAsync(f.Invocation, f.Actor, cancellation.Token));
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("approval")]
    [DataRow("target")]
    [DataRow("authorization")]
    public async Task Resume_gate_cancellation_after_ignored_await_stops_admission(string boundary)
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (boundary == "approval") f.OnApprovalQuery = () => cancellation.Cancel();
        if (boundary == "target") f.OnTargetQuery = () => cancellation.Cancel();
        if (boundary == "authorization") f.Authorization.AuthorizeAsync(AiChatPermissions.Update, Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return f.Actor; });
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await f.Gate.HasApprovedUnconsumedForRunAsync(f.RunId, f.Actor.UserId, cancellation.Token));
    }

    [TestMethod]
    [DataRow("revoked")]
    [DataRow("session")]
    [DataRow("cancel_target")]
    public async Task Rename_handler_reauthorizes_before_entering_local_transaction(string change)
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (change == "cancel_target") f.OnTargetQuery = () => cancellation.Cancel();
        f.Authorization.AuthorizeAsync(AiChatPermissions.Update, Arg.Any<CancellationToken>()).Returns(
            change == "revoked" ? null : f.Actor with { SessionId = Guid.CreateVersion7() });
        // 事务夹具执行真实委托；授权不能在此区域跨入 Identity 模块。
        var transaction = new RecordingTransaction();
        var queryService = new AiChatSessionQueryService(f.Queries, f.Tenant, Options.Create(new DatabaseOptions()), f.Clock);
        var sessions = new AiChatSessionManagementService(f.Queries, f.Commands, transaction, queryService, null!, f.Tenant, f.Clock, Substitute.For<IIdGenerator>());
        var handler = new RenameChatSessionToolHandler(sessions, queryService, f.Consumption, transaction, f.Tenant);
        if (change == "cancel_target") await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await handler.ExecuteAsync(f.Invocation, f.Actor, cancellation.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.ExecuteAsync(f.Invocation, f.Actor, cancellation.Token));
        Assert.AreEqual(0, transaction.Calls);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Rename_handler_cancellation_after_consumption_never_dispatches_target_write()
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return 1; });
        var transaction = new RecordingTransaction();
        var queryService = new AiChatSessionQueryService(f.Queries, f.Tenant, Options.Create(new DatabaseOptions()), f.Clock);
        var sessions = new AiChatSessionManagementService(f.Queries, f.Commands, transaction, queryService, null!, f.Tenant, f.Clock, Substitute.For<IIdGenerator>());
        var handler = new RenameChatSessionToolHandler(sessions, queryService, f.Consumption, transaction, f.Tenant);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await handler.ExecuteAsync(f.Invocation, f.Actor, cancellation.Token));
        Assert.AreEqual(1, transaction.Calls);
        Assert.AreEqual(1, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("tenant")]
    [DataRow("actor_scope")]
    [DataRow("effective_scope")]
    [DataRow("session_kind")]
    [DataRow("cancel_run")]
    [DataRow("cancel_binding")]
    [DataRow("expired_after_gate")]
    public async Task Resume_service_preserves_original_binding_and_stops_late_cancel(string change)
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        var binding = new SessionBindingSnapshot(f.Actor.UserId, null, f.Actor.SessionId, "stamp", "host", "host");
        var run = new AiAgentRunRecord { Id = f.RunId, ScopeKey = "host", ActorUserId = f.Actor.UserId, SessionId = f.Actor.SessionId,
            SecurityStamp = "stamp", ActorScope = "host", EffectiveScope = "host", StatusKey = "awaiting_approval", DeadlineAtUtc = f.Now.AddMinutes(1) };
        if (change == "tenant") run.TenantId = Guid.CreateVersion7();
        if (change == "actor_scope") run.ActorScope = "other";
        if (change == "effective_scope") run.EffectiveScope = "other";
        if (change == "session_kind") run.SessionKind = "api_key";
        f.Queries.RunQuery = () => { if (change == "cancel_run") cancellation.Cancel(); return run; };
        var validator = Substitute.For<IBackgroundSessionBindingValidator>();
        validator.IsValidAsync(binding, Arg.Any<CancellationToken>()).Returns(_ => { if (change == "cancel_binding") cancellation.Cancel(); return true; });
        if (change == "expired_after_gate") f.OnApprovalQuery = () => f.CurrentTime = f.Now.AddMinutes(2);
        var ids = Substitute.For<IIdGenerator>();
        var store = new AiAgentRunStore(f.Queries, f.Commands, new RecordingTransaction(), ids, f.Clock, Options.Create(new DatabaseOptions()));
        var service = new AiAgentRunManagementService(store, null!, f.Gate, null!, validator, null!, null!, f.Tenant, f.Clock, ids, Options.Create(new AiAgentRuntimeOptions()));
        if (change.StartsWith("cancel_", StringComparison.Ordinal))
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.ResumeAsync(f.RunId, binding, cancellation.Token));
        else
        {
            var result = await service.ResumeAsync(f.RunId, binding, cancellation.Token);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(AiErrorCodes.AgentRunNotResumable, result.Error!.Code);
        }
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Approved_rename_handler_does_not_read_or_release_chat_history()
    {
        using var f = new Fixture();
        f.Queries.RejectHistory = true;
        var transaction = new RecordingTransaction();
        var queryService = new AiChatSessionQueryService(f.Queries, f.Tenant, Options.Create(new DatabaseOptions()), f.Clock);
        var sessions = new AiChatSessionManagementService(f.Queries, f.Commands, transaction, queryService, null!, f.Tenant, f.Clock, Substitute.For<IIdGenerator>());
        var handler = new RenameChatSessionToolHandler(sessions, queryService, f.Consumption, transaction, f.Tenant);
        var result = await handler.ExecuteAsync(f.Invocation, f.Actor, default);
        Assert.AreEqual("new", result.GetProperty("title").GetString());
        Assert.AreEqual(2, result.EnumerateObject().Count());
        Assert.AreEqual(1, transaction.Calls);
        Assert.AreEqual(2, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Approved_rename_handler_cancellation_after_commit_never_returns_success()
    {
        using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        var transaction = new RecordingTransaction { AfterResult = cancellation.Cancel };
        var queryService = new AiChatSessionQueryService(f.Queries, f.Tenant, Options.Create(new DatabaseOptions()), f.Clock);
        var sessions = new AiChatSessionManagementService(f.Queries, f.Commands, transaction, queryService, null!, f.Tenant, f.Clock, Substitute.For<IIdGenerator>());
        var handler = new RenameChatSessionToolHandler(sessions, queryService, f.Consumption, transaction, f.Tenant);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await handler.ExecuteAsync(f.Invocation, f.Actor, cancellation.Token));
        Assert.AreEqual(2, f.Commands.ReceivedCalls().Count());
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        internal int Calls;
        internal Action? AfterResult;
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        { Calls++; return action(token); }
        public async Task<Result<T>> ExecuteResultAsync<T>(Func<CancellationToken, Task<Result<T>>> action, CancellationToken token)
        { Calls++; var result = await action(token); AfterResult?.Invoke(); return result; }
    }

    /// <summary>显式查询夹具故意忽略取消，避免内部行类型被动态代理并验证调用方边界。</summary>
    private sealed class FixtureQueries(Fixture fixture) : IQueryExecutor
    {
        internal bool RejectHistory;
        internal Func<AiAgentRunRecord?>? RunQuery;
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken token = default)
        {
            object? row;
            if (typeof(T) == typeof(AiAgentRunRecord)) row = RunQuery?.Invoke();
            else if (typeof(T) == typeof(AiAgentApprovalRecord) || typeof(T) == typeof(int))
            { fixture.OnApprovalQuery?.Invoke(); row = typeof(T) == typeof(int) ? (object)1 : fixture.ApprovalExists ? fixture.Record : null; }
            else if (typeof(T) == typeof(AiChatSessionRecord))
            { fixture.OnTargetQuery?.Invoke(); row = fixture.Target; }
            else throw new InvalidOperationException("Unexpected query projection.");
            return Task.FromResult(row is null ? default : (T)row);
        }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken token = default)
        {
            if (typeof(T) == typeof(AiAgentApprovalRecord))
            { fixture.OnApprovalQuery?.Invoke(); return Task.FromResult<IReadOnlyList<T>>([(T)(object)fixture.Record]); }
            if (typeof(T) == typeof(AiChatMessageRecord))
            {
                if (RejectHistory) throw new InvalidOperationException("工具写入不能查询聊天历史。");
                return Task.FromResult<IReadOnlyList<T>>([]);
            }
            throw new InvalidOperationException("Unexpected query projection.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal DateTimeOffset CurrentTime;
        internal readonly Guid RunId = Guid.CreateVersion7(), TargetId = Guid.CreateVersion7();
        internal readonly ToolActor Actor = new(Guid.CreateVersion7(), null, Guid.CreateVersion7());
        internal readonly FixtureQueries Queries;
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly IToolAuthorizationPort Authorization = Substitute.For<IToolAuthorizationPort>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly CurrentTenantAccessor Tenant = new();
        internal readonly AiAgentApprovalRecord Record;
        internal AiChatSessionRecord? Target;
        internal bool ApprovalExists = true;
        internal Action? OnApprovalQuery, OnTargetQuery;
        internal readonly ServiceProvider Services;
        internal AiAgentApprovalConsumption Consumption => ActivatorUtilities.CreateInstance<AiAgentApprovalConsumption>(Services);
        internal AiAgentRunApprovalGate Gate => ActivatorUtilities.CreateInstance<AiAgentRunApprovalGate>(Services);
        internal ToolInvocation Invocation => new(Record.OperationId, RunId, "ai.chat.sessions.rename", 1, JsonSerializer.SerializeToElement(
            new Dictionary<string, string> { ["sessionId"] = TargetId.ToString(), ["title"] = "new" }));

        internal Fixture()
        {
            CurrentTime = Now; Clock.UtcNow.Returns(_ => CurrentTime); Tenant.SetHost();
            Queries = new FixtureQueries(this);
            Record = new() { Id = Guid.CreateVersion7(), ScopeKey = "host", RunId = RunId, OperationId = Guid.CreateVersion7(),
                SessionId = Actor.SessionId, ToolName = "ai.chat.sessions.rename", ToolVersion = 1, PolicyVersion = 1,
                RequestedBy = Actor.UserId, DecisionKey = "approved", ExpiresAtUtc = Now.AddMinutes(5), Version = 1,
                PresentationJson = JsonSerializer.Serialize(new AiAgentApprovalPresentation("Rename", "old", "new", "owned", null, null, 1), AiJsonSerializerContext.Default.AiAgentApprovalPresentation) };
            SetArguments(Invocation.Arguments.GetRawText());
            Target = new() { Id = TargetId, OwnerUserId = Actor.UserId, Version = 1, Title = "old" };
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Authorization.AuthorizeAsync(AiChatPermissions.Update, Arg.Any<CancellationToken>()).Returns(Actor);
            Services = new ServiceCollection().AddSingleton<IQueryExecutor>(Queries).AddSingleton(Commands).AddSingleton(Clock)
                .AddSingleton(Authorization).AddSingleton<ICurrentTenant>(Tenant).AddSingleton(Options.Create(new DatabaseOptions())).BuildServiceProvider();
        }
        internal void SetArguments(string json)
        {
            Record.ArgumentsProtected = json;
            using var args = JsonDocument.Parse(json);
            Record.ArgumentsHash = Full.NET.Agents.Approvals.AgentApprovalGate.ComputeArgumentsHash(args.RootElement);
        }
        public void Dispose() => Services.Dispose();
    }
}
