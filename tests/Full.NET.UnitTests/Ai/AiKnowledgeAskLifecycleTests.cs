using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>公开问答入口只在可信范围启动，失败必须回滚并按原范围收尾。</summary>
[TestClass]
public sealed class AiKnowledgeAskLifecycleTests
{
    [TestMethod]
    [DataRow("empty")]
    [DataRow("duplicate")]
    [DataRow("oversize")]
    [DataRow("secret")]
    [DataRow("model")]
    public async Task Invalid_request_never_reads_or_acquires_slot(string fault)
    {
        await using var f = new Fixture();
        var request = f.Request with
        {
            KnowledgeBaseIds = fault == "empty" ? [] : fault == "duplicate" ? [f.BaseId, f.BaseId] : [f.BaseId],
            Question = fault == "oversize" ? new string('问', 4097) : fault == "secret" ? "api_key=secret" : "问题",
            EmbeddingModelConfigId = fault == "model" ? Guid.Empty : f.EmbeddingId,
        };
        var result = await f.Service.AskAsync(f.SessionId, f.Actor, request);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(AiKnowledgeErrorCodes.InputInvalid, result.Error!.Code);
        Assert.AreEqual(0, f.Query.ReceivedCalls().Count()); Assert.HasCount(0, f.Writes);
    }

    [TestMethod]
    [DataRow("permission")]
    [DataRow("owner")]
    [DataRow("model")]
    [DataRow("scope")]
    public async Task Preflight_failure_never_acquires_generation(string fault)
    {
        await using var f = new Fixture();
        if (fault == "permission") f.Denied = AiKnowledgeDocumentPermissions.IndexRead;
        if (fault == "owner") f.Query.QuerySingleOrDefaultAsync<AiChatSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((AiChatSessionRecord?)null);
        if (fault == "model") f.Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((AiModelConfigRecord?)null);
        if (fault == "scope") f.BeforePermission = () => f.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "另一个租户"));
        Assert.IsFalse((await f.Service.AskAsync(f.SessionId, f.Actor, f.Request)).IsSuccess);
        Assert.HasCount(0, f.Writes);
    }

    [TestMethod]
    public async Task Busy_slot_does_not_insert_messages_or_invoke_model()
    {
        await using var f = new Fixture(); f.Busy = true;
        var result = await f.Service.AskAsync(f.SessionId, f.Actor, f.Request);
        Assert.AreEqual(AiErrorCodes.ChatGenerationInProgress, result.Error!.Code);
        Assert.IsFalse(f.Writes.Any(write => write.Name == AiChatSql.InsertMessage.Name));
        Assert.AreEqual(1, f.Coordinator.CommitCount); Assert.AreEqual(0, f.Coordinator.RollbackCount);
    }

    [TestMethod]
    [DataRow("user")]
    [DataRow("placeholder")]
    [DataRow("count")]
    public async Task Startup_failure_rolls_back_and_cleans_original_scope(string stage)
    {
        await using var f = new Fixture(); f.Fault = stage;
        var result = await f.Service.AskAsync(f.SessionId, f.Actor, f.Request);
        Assert.AreEqual(AiErrorCodes.ChatGenerationFailed, result.Error!.Code);
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
        Assert.IsTrue(f.Writes.Any(write => write.Name == AiKnowledgeAnswerSql.FailFor(false).Name));
        Assert.IsTrue(f.Writes.Any(write => write.Name == AiChatGenerationSql.Release.Name));
        Assert.IsTrue(f.CleanupScopes.All(scope => scope is null));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Failure_sql_cannot_overwrite_completed_answer_or_successor(bool tenant)
    {
        var statement = AiKnowledgeAnswerSql.FailFor(tenant);
        Assert.AreEqual(tenant ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, statement.Scope);
        foreach (var clause in new[] { "StatusKey='streaming'", "KnowledgeLineage IS NULL", "IsKnowledgeAnswer=1", "session.OwnerUserId=@OwnerUserId", "session.GenerationId=@MessageId" })
            StringAssert.Contains(statement.Text, clause);
    }

    [TestMethod]
    public async Task Noncooperative_initial_renewal_cannot_hold_cancellation_or_cleanup()
    {
        await using var f = new Fixture();
        var blocked = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.RenewBlock = blocked.Task;
        using var stop = new CancellationTokenSource();
        var pending = f.Service.AskAsync(f.SessionId, f.Actor, f.Request, stop.Token);
        await f.RenewEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        stop.Cancel();
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsTrue(f.Writes.Any(write => write == AiChatGenerationSql.Release));
        }
        finally { blocked.TrySetResult(0); }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Guid SessionId = Guid.CreateVersion7(), Actor = Guid.CreateVersion7(), BaseId = Guid.CreateVersion7(), EmbeddingId = Guid.CreateVersion7();
        internal CurrentTenantAccessor Tenant = new();
        internal IQueryExecutor Query = Substitute.For<IQueryExecutor>();
        internal List<SqlStatement> Writes = []; internal List<Guid?> CleanupScopes = [];
        internal RecordingDbTransactionCoordinator Coordinator = new();
        internal string? Denied, Fault; internal bool Busy; internal Action? BeforePermission;
        internal Task<int>? RenewBlock; internal TaskCompletionSource RenewEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal AiKnowledgeAskService Service; private readonly ServiceProvider services;
        internal AskAiKnowledgeRequest Request => new([BaseId], EmbeddingId, "问题");
        internal Fixture()
        {
            Tenant.SetHost(); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            var auth = Substitute.For<ICurrentSessionAuthorization>();
            auth.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            { BeforePermission?.Invoke(); return call.ArgAt<string>(0) == Denied ? null : new AuthorizedSessionActor(Actor, null, Guid.CreateVersion7()); });
            var generationModel = Guid.CreateVersion7();
            Query.QuerySingleOrDefaultAsync<AiChatSessionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new AiChatSessionRecord { Id = SessionId, OwnerUserId = Actor, ModelConfigId = generationModel, Title = "新对话" });
            Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call => new AiModelConfigRecord { Id = (Guid)((IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1))["ModelConfigId"]!, IsEnabled = true });
            var commands = Substitute.For<ICommandExecutor>();
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var sql = call.Arg<SqlStatement>()!; Writes.Add(sql);
                if (sql == AiChatGenerationSql.Acquire) return Task.FromResult(Busy ? 0 : 1);
                if (sql == AiChatGenerationSql.Renew && RenewBlock is not null) { RenewEntered.TrySetResult(); return RenewBlock; }
                if (sql == AiChatSql.InsertMessage || sql == AiKnowledgeAnswerSql.InsertFor(false) || sql == AiChatSql.TouchSessionAfterMessage)
                {
                    Assert.IsTrue(Coordinator.HasTransaction);
                    if (Fault == "user" && sql == AiChatSql.InsertMessage || Fault == "placeholder" && sql == AiKnowledgeAnswerSql.InsertFor(false)
                        || Fault == "count" && sql == AiChatSql.TouchSessionAfterMessage) throw new IOException("启动失败");
                }
                return Task.FromResult(1);
            });
            services = new ServiceCollection().AddScoped<CurrentTenantAccessor>()
                .AddScoped<ICurrentTenantContextWriter>(s => s.GetRequiredService<CurrentTenantAccessor>())
                .AddScoped<ICommandExecutor>(s =>
                { CleanupScopes.Add(s.GetRequiredService<CurrentTenantAccessor>().Id); Assert.IsFalse(Coordinator.HasTransaction); return commands; }).BuildServiceProvider();
            var scopes = services.GetRequiredService<IServiceScopeFactory>();
            Service = new(Query, commands, new DapperCommandTransaction(Coordinator), Substitute.For<IDataTransactionState>(), new(Query, Tenant, Options.Create(new DatabaseOptions()), clock),
                null!, null!, null!, new(auth, new HttpContextAccessor(), Substitute.For<ICurrentApiKeyAuthorization>()), new(),
                new(scopes, clock, NullLogger<AiChatGenerationLeaseMonitor>.Instance), new(scopes, NullLogger<AiChatCleanupScope>.Instance), Tenant, clock, ids);
        }
        public ValueTask DisposeAsync() => services.DisposeAsync();
    }
}
