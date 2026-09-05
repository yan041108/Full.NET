using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证超时 Worker 的收件人、升级优先和重复扫描 CAS 语义。</summary>
[TestClass]
public sealed class WorkflowTodoTimeoutProcessorTests
{
    /// <summary>催办应发送给扫描时的当前办理人，改派不会重置策略时钟。</summary>
    [TestMethod]
    public async Task Due_reminder_is_committed_and_published_to_current_assignee()
    {
        var fixture = CreateFixture(CreateCandidate(escalateNow: false));

        var count = await fixture.Processor.ProcessDueAsync(TestContext.CancellationToken);

        Assert.AreEqual(1, count);
        await fixture.Command.Received(1).ExecuteAsync(
            WorkflowTodoTimeoutSql.CommitReminder,
            Arg.Is<object?>(value => Has(value, "ReminderCount", 2)),
            TestContext.CancellationToken);
        await fixture.Outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoReminderRequested, 1,
            Arg.Is<WorkflowTodoReminderRequestedIntegrationEvent>(value =>
                value != null && value.RecipientUserId == fixture.Candidate.AssigneeUserId &&
                value.ReminderSequence == 2),
            Arg.Any<Full.NET.Messaging.Abstractions.IntegrationEventMetadata>(),
            TestContext.CancellationToken);
    }

    /// <summary>同一时刻满足催办和升级时应只提交升级，并发送给固定升级接收人。</summary>
    [TestMethod]
    public async Task Escalation_has_priority_over_reminder()
    {
        var fixture = CreateFixture(CreateCandidate(escalateNow: true));

        await fixture.Processor.ProcessDueAsync(TestContext.CancellationToken);

        await fixture.Command.Received(1).ExecuteAsync(
            WorkflowTodoTimeoutSql.CommitEscalation,
            Arg.Any<object?>(), TestContext.CancellationToken);
        await fixture.Command.DidNotReceive().ExecuteAsync(
            WorkflowTodoTimeoutSql.CommitReminder,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await fixture.Outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoEscalationRequested, 1,
            Arg.Is<WorkflowTodoEscalationRequestedIntegrationEvent>(value =>
                value != null && value.RecipientUserId == fixture.Candidate.EscalationRecipientUserId),
            Arg.Any<Full.NET.Messaging.Abstractions.IntegrationEventMetadata>(),
            TestContext.CancellationToken);
    }

    /// <summary>CAS 未命中代表重复扫描或并发办理，禁止追加重复事件。</summary>
    [TestMethod]
    public async Task Lost_compare_and_swap_does_not_publish_duplicate_event()
    {
        var fixture = CreateFixture(CreateCandidate(escalateNow: false), commitResult: 0);

        await fixture.Processor.ProcessDueAsync(TestContext.CancellationToken);

        await fixture.Outbox.DidNotReceive().AddAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<object>(),
            Arg.Any<Full.NET.Messaging.Abstractions.IntegrationEventMetadata>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>满页停用租户不得让游标永远停在首批候选，下一批 Host 信号仍需被处理。</summary>
    [TestMethod]
    public async Task Inactive_tenant_page_advances_cursor_and_does_not_starve_host_candidate()
    {
        var inactiveCandidates = Enumerable.Range(0, 50)
            .Select(_ => CreateCandidate(escalateNow: false))
            .ToArray();
        var hostCandidate = CreateCandidate(escalateNow: false) with
        {
            TenantId = null,
            ScopeKey = "host",
            TenantScopeKey = "host",
        };
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowTodoTimeoutCandidateRecord>(
                WorkflowTodoTimeoutSql.ScanDueSqlServer,
                Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(inactiveCandidates, [hostCandidate]);
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        resolver.ResolveActiveByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((TenantContext?)null);
        var currentTenant = Substitute.For<ICurrentTenantContextWriter>();
        var outbox = Substitute.For<IOutboxWriter>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-05T03:00:00Z"));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.CreateVersion7());
        var processor = new WorkflowTodoTimeoutProcessor(
            query, command, new RecordingTransaction(), clock, ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            resolver, currentTenant, new WorkflowNotificationOutboxPublisher(outbox),
            new WorkflowTodoTimeoutScanCursor());

        Assert.AreEqual(50, await processor.ProcessDueAsync(TestContext.CancellationToken));
        Assert.AreEqual(1, await processor.ProcessDueAsync(TestContext.CancellationToken));

        await query.Received(1).QueryAsync<WorkflowTodoTimeoutCandidateRecord>(
            WorkflowTodoTimeoutSql.ScanDueSqlServer,
            Arg.Is<object?>(value => Has(value, "HasAfter", 1) &&
                HasNonNull(value, "AfterSignalAtUtc") &&
                HasNonNull(value, "AfterTodoId")),
            TestContext.CancellationToken);
        await outbox.Received(1).AddAsync(
            WorkflowNotificationIntegrationEventTypes.TodoReminderRequested, 1,
            Arg.Is<WorkflowTodoReminderRequestedIntegrationEvent>(value =>
                value != null && value.TodoId == hostCandidate.TodoId),
            Arg.Any<Full.NET.Messaging.Abstractions.IntegrationEventMetadata>(),
            TestContext.CancellationToken);
    }

    /// <summary>扫描和 CAS SQL 必须同时锁定活动状态、租户锚点与预期调度版本。</summary>
    [TestMethod]
    public void Timeout_sql_closes_pause_terminal_tenant_and_concurrency_boundaries()
    {
        StringAssert.Contains(WorkflowTodoTimeoutSql.ScanDueSqlServer.Text,
            "instance.StatusKey = 'active'");
        StringAssert.Contains(WorkflowTodoTimeoutSql.ScanDueMySql.Text,
            "instance.StatusKey = 'active'");
        foreach (var statement in new[]
                 {
                     WorkflowTodoTimeoutSql.CommitReminder,
                     WorkflowTodoTimeoutSql.CommitEscalation,
                 })
        {
            StringAssert.Contains(statement.Text, "instance.TenantScopeKey = @TenantScopeKey");
            StringAssert.Contains(statement.Text, "Revision = @Revision");
            StringAssert.Contains(statement.Text,
                "NextTimeoutSignalAtUtc = @ExpectedSignalAtUtc");
            StringAssert.Contains(statement.Text, "instance.StatusKey = 'active'");
        }
    }

    private static TimeoutFixture CreateFixture(
        WorkflowTodoTimeoutCandidateRecord candidate,
        int commitResult = 1)
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QueryAsync<WorkflowTodoTimeoutCandidateRecord>(
                WorkflowTodoTimeoutSql.ScanDueSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([candidate]);
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(commitResult);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-05T03:00:00Z"));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.CreateVersion7());
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        resolver.ResolveActiveByIdAsync(candidate.TenantId!.Value, Arg.Any<CancellationToken>())
            .Returns(new TenantContext(candidate.TenantId.Value, "tenant-a", "Tenant A"));
        var currentTenant = Substitute.For<ICurrentTenantContextWriter>();
        var outbox = Substitute.For<IOutboxWriter>();
        var processor = new WorkflowTodoTimeoutProcessor(
            query, command, new RecordingTransaction(), clock, ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            resolver, currentTenant, new WorkflowNotificationOutboxPublisher(outbox),
            new WorkflowTodoTimeoutScanCursor());
        return new(processor, command, outbox, candidate);
    }

    private static WorkflowTodoTimeoutCandidateRecord CreateCandidate(bool escalateNow)
    {
        var now = DateTimeOffset.Parse("2026-09-05T03:00:00Z");
        return new(Guid.CreateVersion7(), "tenant", $"tenant:{Guid.CreateVersion7():N}",
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "contract", "C-004", 3, now.AddMinutes(-1),
            escalateNow ? now : now.AddHours(1), 15, 3, 1, Guid.CreateVersion7(), null,
            now.AddMinutes(-1));
    }

    private static bool Has(object? value, string name, object expected) =>
        value is IReadOnlyDictionary<string, object?> values &&
        values.TryGetValue(name, out var actual) && Equals(actual, expected);

    /// <summary>判断参数字典包含指定的非空值。</summary>
    /// <param name="value">待检查的 SQL 参数对象。</param>
    /// <param name="name">参数名。</param>
    /// <returns>参数存在且非空时返回真。</returns>
    private static bool HasNonNull(object? value, string name) =>
        value is IReadOnlyDictionary<string, object?> values &&
        values.TryGetValue(name, out var actual) && actual is not null;

    private sealed record TimeoutFixture(
        WorkflowTodoTimeoutProcessor Processor,
        ICommandExecutor Command,
        IOutboxWriter Outbox,
        WorkflowTodoTimeoutCandidateRecord Candidate);

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => await action(cancellationToken);
    }

    /// <summary>获取 MSTest 当前测试上下文。</summary>
    public TestContext TestContext { get; set; } = null!;
}
