using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

[TestClass]
public sealed class EnterpriseRequestWorkflowOutcomeServiceTests
{
    [TestMethod]
    public async Task Ignores_non_enterprise_business_type()
    {
        var f = new Fixture();
        await f.Deliver("other.business");
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Approved_workflow_updates_submitted_request()
    {
        var f = new Fixture();
        await f.Deliver();
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal,
            Arg.Is<object?>(p => ((Dictionary<string, object?>)p!)["LastMessageId"]!.Equals(f.Message)), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Coordinator.CommitCount);
        f.Tenant.Received(1).SetHost();
    }

    [TestMethod]
    public async Task Does_not_update_when_request_is_not_submitted()
    {
        var f = new Fixture(); f.Row = f.Row with { Status = "Draft" };
        await f.Deliver();
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("instance")]
    [DataRow("tenant")]
    [DataRow("version")]
    [DataRow("identity")]
    [DataRow("deleted")]
    [DataRow("missing")]
    public async Task Foreign_or_stale_outcome_has_no_side_effects(string kind)
    {
        var f = new Fixture();
        if (kind == "instance") f.Submission = f.Submission! with { WorkflowInstanceId = Guid.NewGuid() };
        if (kind == "tenant") f.Submission = f.Submission! with { TenantId = Guid.NewGuid() };
        if (kind == "version") f.Row = f.Row with { Version = f.Row.Version + 1 };
        if (kind == "identity") f.Row = f.Row with { Id = Guid.NewGuid() };
        if (kind == "deleted") f.Row = f.Row with { IsDeleted = true };
        if (kind == "missing") f.Submission = null;
        if (kind is "missing" or "version") await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver());
        else await f.Deliver();
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    public async Task Sealed_submission_ignores_duplicate_or_conflicting_outcome(string sealedStatus)
    {
        var f = new Fixture(); f.Submission = f.Submission! with { FinalStatus = sealedStatus, LastMessageId = f.Message };
        await f.Deliver();
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Receipt_conflict_rolls_back_status_write()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver());
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
        f.Tenant.Received(1).SetHost();
    }

    /// <summary>完成、驳回、取消均须以提交版本更新，并写入同一事务的消息回执。</summary>
    [TestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    public async Task Terminal_results_preserve_submission_version_and_message_identity(string status)
    {
        var f = new Fixture();
        await f.Deliver(status: status);
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
            Arg.Is<object?>(p => ((Dictionary<string, object?>)p!)["Status"]!.Equals(status)
                && ((Dictionary<string, object?>)p!)["ExpectedVersion"]!.Equals(f.Submission!.RequestVersion)),
            Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal,
            Arg.Is<object?>(p => ((Dictionary<string, object?>)p!)["FinalStatus"]!.Equals(status)
                && ((Dictionary<string, object?>)p!)["LastMessageId"]!.Equals(f.Message)), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Coordinator.CommitCount);
    }

    /// <summary>乐观锁竞争失败后，已绑定实例的获胜回执允许确认重投；不再重写终态。</summary>
    [TestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    public async Task Concurrent_terminal_winner_is_preserved_after_losing_compare_exchange(string winnerStatus)
    {
        var f = new Fixture();
        var original = f.Submission!;
        f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(original,
                original with { FinalStatus = winnerStatus, LastMessageId = Guid.CreateVersion7() });
        f.Commands.ExecuteAsync(EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await f.Deliver();
        await f.Commands.DidNotReceive().ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
        f.Tenant.Received(1).SetHost();
    }

    /// <summary>读取、状态 CAS 与回执写入分别失败或取消时，不提交，且恢复事件租户作用域。</summary>
    [TestMethod]
    [DataRow("submission", false)]
    [DataRow("submission", true)]
    [DataRow("request", false)]
    [DataRow("request", true)]
    [DataRow("status", false)]
    [DataRow("status", true)]
    [DataRow("receipt", false)]
    [DataRow("receipt", true)]
    public async Task Failure_or_cancellation_keeps_transaction_and_tenant_boundaries(string stage, bool cancelled)
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        Exception fault = cancelled ? new OperationCanceledException("test.outcome_cancelled", token)
            : new InvalidOperationException("test.outcome_fault");
        Task<T> Faulted<T>()
        {
            // 在被测阶段才取消真实令牌，证明回滚不会被已经取消的业务令牌阻断。
            if (cancelled) cancellation.Cancel();
            return Task.FromException<T>(fault);
        }
        if (stage == "submission") f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(
            EnterpriseRequestApprovalSql.FindByRequest, Arg.Any<object?>(), token)
            .Returns(_ => Faulted<EnterpriseRequestApprovalSubmission?>());
        else if (stage == "request") f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
            EnterpriseRequestSql.FindByIdStatement, Arg.Any<object?>(), token)
            .Returns(_ => Faulted<EnterpriseRequestRecord?>());
        else f.Commands.ExecuteAsync(stage == "status" ? EnterpriseRequestWorkflowSql.ApplyTerminalStatus
                : EnterpriseRequestApprovalSql.MarkFinal, Arg.Any<object?>(), token)
            .Returns(_ => Faulted<int>());

        if (cancelled)
        {
            var observed = await Assert.ThrowsAsync<OperationCanceledException>(() => f.Deliver(cancellationToken: token));
            Assert.AreSame(fault, observed); Assert.AreEqual(token, observed.CancellationToken);
        }
        else Assert.AreSame(fault, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver(cancellationToken: token)));
        Assert.AreEqual(cancelled, token.IsCancellationRequested);
        var enteredTransaction = stage is "status" or "receipt";
        Assert.AreEqual(enteredTransaction ? 1 : 0, f.Coordinator.BeginCount);
        Assert.AreEqual(enteredTransaction ? 1 : 0, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
        Assert.IsFalse(f.Coordinator.HasTransaction);
        if (enteredTransaction) Assert.AreEqual(CancellationToken.None, f.Coordinator.RollbackToken);
        foreach (var call in f.Queries.ReceivedCalls().Concat(f.Commands.ReceivedCalls()))
            Assert.AreEqual(token, call.GetArguments().OfType<CancellationToken>().Single());
        f.Tenant.Received(1).SetHost();
    }

    /// <summary>CAS 失败只有同一提交和实例的完整获胜回执可确认，否则保留重试错误。</summary>
    [TestMethod]
    [DataRow("missing")]
    [DataRow("pending")]
    [DataRow("submission")]
    [DataRow("instance")]
    public async Task Losing_compare_exchange_without_matching_winner_remains_retryable(string kind)
    {
        var f = new Fixture(); var original = f.Submission!;
        EnterpriseRequestApprovalSubmission? latest = original with { FinalStatus = "Cancelled", LastMessageId = Guid.CreateVersion7() };
        latest = kind switch {
            "missing" => null,
            "pending" => latest with { FinalStatus = null },
            "submission" => latest with { Id = Guid.CreateVersion7() },
            "instance" => latest with { WorkflowInstanceId = Guid.CreateVersion7() },
            _ => latest
        };
        f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(original, latest);
        f.Commands.ExecuteAsync(EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver());
        Assert.AreEqual("enterprise_request.status_update_conflict", error.Message);
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
        await f.Commands.DidNotReceive().ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        f.Tenant.Received(1).SetHost();
    }

    private sealed class CancellationRecordingCoordinator : RecordingDbTransactionCoordinator
    {
        internal CancellationToken? RollbackToken;
        public override Task RollbackAsync(CancellationToken cancellationToken)
        { RollbackToken = cancellationToken; return base.RollbackAsync(cancellationToken); }
    }

    [TestMethod]
    public async Task Pre_cancelled_outcome_has_no_reads_writes_or_transaction()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => f.Deliver(cancellationToken: stop.Token));
        Assert.AreEqual(stop.Token, error.CancellationToken);
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count()); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Coordinator.BeginCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
        Assert.AreEqual(0, f.Tenant.ReceivedCalls().Count(call => call.GetMethodInfo().Name is "SetTenant" or "SetHost" or "Clear"));
    }

    private sealed class Fixture
    {
        internal readonly Guid Instance = Guid.CreateVersion7(), Message = Guid.CreateVersion7();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly ICurrentTenantContextWriter Tenant = Substitute.For<ICurrentTenantContextWriter>();
        internal readonly CancellationRecordingCoordinator Coordinator = new();
        internal EnterpriseRequestRecord Row;
        internal EnterpriseRequestApprovalSubmission? Submission;
        internal readonly EnterpriseRequestWorkflowOutcomeService Service;
        internal Fixture()
        {
            Row = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "REQ", "Title", "Submitted", 1m,
                Guid.CreateVersion7(), 2, DateTimeOffset.UtcNow, Guid.CreateVersion7(), null, null, false, null, null);
            Submission = new(Guid.CreateVersion7(), Row.TenantId, Row.Id, Row.Version, Guid.CreateVersion7(), Instance,
                Row.ApplicantUserId, Row.OrganizationUnitId, Row.Title, DateTimeOffset.UtcNow, null, null, null, null);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(EnterpriseRequestSql.FindByIdStatement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Row);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Submission);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Tenant.IsHost.Returns(true);
            Service = new(Queries, Commands, Substitute.For<IClock>(), new DapperCommandTransaction(Coordinator), Tenant);
        }
        internal Task Deliver(string type = EnterpriseRequestWorkflowConstants.BusinessType, string status = "Approved", CancellationToken cancellationToken = default) =>
            Service.HandleTerminalWorkflowAsync(type, Row.Id.ToString("D"), status, Instance, Row.TenantId, Message, cancellationToken);
    }
}
