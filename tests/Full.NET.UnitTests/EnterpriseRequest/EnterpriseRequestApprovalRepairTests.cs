using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.RepairApproval;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>恢复须有已授权单据、原启动证据及原子修复记录，不能创建新流程。</summary>
[TestClass]
public sealed class EnterpriseRequestApprovalRepairTests
{
    [TestMethod]
    public async Task Missing_binding_is_restored_from_start_proof_without_changing_business_version()
    {
        var f = new Fixture();
        Assert.IsTrue((await f.Repair()).IsSuccess);
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.Insert,
            Arg.Is<object?>(p => Value(p, "SubmittedById").Equals(f.Snapshot.StartedById)
                && Value(p, "RequestVersion").Equals(2L)), Arg.Any<CancellationToken>());
        Assert.IsFalse(f.Commands.ReceivedCalls().Any(c => c.GetArguments()[0] is SqlStatement s && s == EnterpriseRequestWorkflowSql.ApplyTerminalStatus));
        Assert.AreEqual(1, f.Coordinator.CommitCount);
    }

    [TestMethod]
    [DataRow("completed", "Approved")]
    [DataRow("rejected", "Rejected")]
    [DataRow("cancelled", "Cancelled")]
    public async Task Terminal_proof_atomically_repairs_business_and_records_logical_receipt(string workflow, string status)
    {
        var f = new Fixture(); f.Snapshot = f.Snapshot with { StatusKey = workflow, CompletedAtUtc = f.Now };
        Assert.IsTrue((await f.Repair()).IsSuccess);
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
            Arg.Is<object?>(p => Value(p, "Status").Equals(status) && Value(p, "ExpectedVersion").Equals(2L)), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal,
            Arg.Is<object?>(p => Value(p, "LastMessageId").Equals(f.Operation)), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("host")]
    [DataRow("actor")]
    [DataRow("version")]
    [DataRow("draft")]
    [DataRow("organization")]
    [DataRow("hidden")]
    [DataRow("reason")]
    public async Task Denial_never_reads_workflow_or_writes(string kind)
    {
        var f = new Fixture();
        if (kind == "host") f.Tenant.IsHost.Returns(true);
        if (kind == "version") f.Row = f.Row! with { Version = 3 };
        if (kind == "draft") f.Row = f.Row! with { Status = "Draft" };
        if (kind == "hidden") f.Row = null;
        if (kind == "organization") f.Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(false));
        var result = await f.Service.RepairAsync(f.Id, new(f.Instance, 2, kind == "reason" ? " " : "verified original start"),
            kind == "actor" ? Guid.Empty : f.Actor, false);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, f.Directory.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_or_mismatched_start_proof_does_not_guess_binding()
    {
        var f = new Fixture(); f.Directory.FindAsync(Arg.Any<WorkflowBusinessStartProofRequest>(), Arg.Any<CancellationToken>()).Returns((WorkflowBusinessInstanceSnapshot?)null);
        Assert.IsFalse((await f.Repair()).IsSuccess);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Failed_repair_receipt_rolls_back_binding_and_terminal_state()
    {
        var f = new Fixture(); f.Snapshot = f.Snapshot with { StatusKey = "cancelled", CompletedAtUtc = f.Now };
        f.Commands.ExecuteAsync(Arg.Is<SqlStatement>(s => s != null && s.Name == "enterprise_request.approval.insert_repair"), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse((await f.Repair()).IsSuccess);
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    private static object Value(object? p, string name) => ((Dictionary<string, object?>)p!)[name]!;
    [TestMethod]
    public async Task Start_receipt_repair_leaves_terminal_reconciliation_key_available()
    {
        var f = new Fixture(); f.Submission = f.Binding() with { StartedAtUtc = null };
        Assert.IsTrue((await f.Repair()).IsSuccess);
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalRepairSql.InsertRepair,
            Arg.Is<object?>(p => Value(p, "KindKey").Equals("start_receipt")), Arg.Any<CancellationToken>());
        f.Submission = f.Binding(); f.Snapshot = f.Snapshot with { StatusKey = "cancelled", CompletedAtUtc = f.Now };
        Assert.IsTrue((await f.Repair()).IsSuccess);
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalRepairSql.InsertRepair,
            Arg.Is<object?>(p => Value(p, "KindKey").Equals("reconcile")), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Concurrent_binding_repair_rechecks_completed_start_under_parent_lock()
    {
        var f = new Fixture(); var reads = 0;
        f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => ++reads == 1 ? null : f.Binding());
        Assert.IsTrue((await f.Repair()).IsSuccess);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count(), "并发恢复已完成时不能更改业务元数据或追加恢复记录。");
        Assert.AreEqual(1, f.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Known_binding_terminal_is_reconciled_without_second_submission()
    {
        var f = new Fixture(); f.Submission = f.Binding();
        f.Snapshot = f.Snapshot with { StatusKey = "cancelled", CompletedAtUtc = f.Now };
        Assert.IsTrue((await f.Repair()).IsSuccess);
        await f.Commands.DidNotReceive().ExecuteAsync(EnterpriseRequestApprovalSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Parent_version_changed_before_lock_refuses_all_local_writes()
    {
        var f = new Fixture();
        f.Queries.QuerySingleOrDefaultAsync<Guid?>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);
        Assert.IsFalse((await f.Repair()).IsSuccess);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count()); Assert.AreEqual(1, f.Coordinator.RollbackCount);
    }
    [TestMethod]
    public async Task Final_repair_replay_keeps_original_receipt_and_does_not_read_workflow()
    {
        var f = new Fixture(); f.Submission = f.Binding() with { FinalStatus = "Cancelled", CompletedAtUtc = f.Now, LastMessageId = f.Operation };
        f.Row = f.Row! with { Status = "Cancelled", Version = 3 };
        Assert.IsTrue((await f.Repair()).IsSuccess);
        Assert.AreEqual(0, f.Directory.ReceivedCalls().Count()); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }
    [TestMethod]
    [DataRow("tenant")]
    [DataRow("actor")]
    [DataRow("title")]
    [DataRow("instance")]
    [DataRow("definition")]
    [DataRow("business")]
    [DataRow("status")]
    [DataRow("terminal_time")]
    public async Task Forged_or_incomplete_proof_does_not_create_binding(string mismatch)
    {
        var f = new Fixture(); f.Snapshot = mismatch switch {
            "tenant" => f.Snapshot with { TenantId = Guid.NewGuid() }, "actor" => f.Snapshot with { StartedById = Guid.NewGuid() },
            "title" => f.Snapshot with { BusinessTitle = "Other" }, "instance" => f.Snapshot with { InstanceId = Guid.NewGuid() },
            "definition" => f.Snapshot with { DefinitionVersionId = Guid.Empty }, "business" => f.Snapshot with { BusinessId = Guid.NewGuid().ToString("D") },
            "status" => f.Snapshot with { StatusKey = "unknown" }, _ => f.Snapshot with { StatusKey = "completed", CompletedAtUtc = null }
        };
        Assert.IsFalse((await f.Repair()).IsSuccess); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }
    [TestMethod]
    [DataRow("enterprise_request.approval.insert")]
    [DataRow("enterprise_request.approval.mark_started")]
    [DataRow("enterprise_request.approval.mark_final")]
    public async Task Any_failed_local_write_rolls_back_recovery(string name)
    {
        var f = new Fixture(); f.Snapshot = f.Snapshot with { StatusKey = "cancelled", CompletedAtUtc = f.Now };
        f.Commands.ExecuteAsync(Arg.Is<SqlStatement>(s => s != null && s.Name == name), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse((await f.Repair()).IsSuccess); Assert.AreEqual(1, f.Coordinator.RollbackCount);
    }
    private sealed class Fixture
    {
        internal readonly Guid Id = Guid.NewGuid(), Instance = Guid.NewGuid(), Actor = Guid.NewGuid(), TenantId = Guid.NewGuid(), Unit = Guid.NewGuid(), Operation = Guid.NewGuid();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly ICurrentTenant Tenant = Substitute.For<ICurrentTenant>();
        internal readonly IOrganizationOwnedEntityWriteAuthorizer Authorizer = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        internal readonly IWorkflowBusinessStartProofDirectory Directory = Substitute.For<IWorkflowBusinessStartProofDirectory>();
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal EnterpriseRequestRecord? Row;
        internal EnterpriseRequestApprovalSubmission? Submission;
        internal WorkflowBusinessInstanceSnapshot Snapshot;
        internal readonly EnterpriseRequestApprovalRepairService Service;
        internal Fixture()
        {
            Tenant.IsAvailable.Returns(true); Tenant.Id.Returns(TenantId);
            Row = new(Id, TenantId, Unit, "REQ", "Title", "Submitted", 1m, Actor, 2, Now, Actor, null, null, false, null, null);
            Snapshot = new(Instance, TenantId, Guid.NewGuid(), EnterpriseRequestWorkflowConstants.BusinessType, Id.ToString("D"), "Title", "active", Guid.NewGuid(), Now.AddMinutes(-1), null);
            Row = Row with { UpdatedById = Snapshot.StartedById };
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Row);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Submission);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Queries.QuerySingleOrDefaultAsync<Guid?>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Id);
            Directory.FindAsync(Arg.Any<WorkflowBusinessStartProofRequest>(), Arg.Any<CancellationToken>()).Returns(_ => {
                Assert.AreEqual(Coordinator.CommitCount + Coordinator.RollbackCount, Coordinator.BeginCount,
                    "跨模块证据读取必须在业务事务之外。"); return Snapshot;
            });
            Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            var scopes = Substitute.For<IUserDataScopeResolver>(); scopes.ResolveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(false, []));
            var filters = Substitute.For<IDataScopeSqlFilterBuilder>(); filters.BuildOrganizationUnitFilter(Arg.Any<EffectiveUserDataScope>(), "OrganizationUnitId", Actor).Returns(new DataScopeSqlFilter("OrganizationUnitId = @RepairUnit", new Dictionary<string, object?> { ["RepairUnit"] = Unit }));
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Operation);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(Now);
            Service = new(new EnterpriseRequestQueryService(Queries, Options.Create(new DatabaseOptions()), scopes, filters), Queries, Commands, Tenant,
                Authorizer, Directory, new DapperCommandTransaction(Coordinator), clock, ids, Options.Create(new DatabaseOptions()));
        }
        internal Task<Result<bool>> Repair() => Service.RepairAsync(Id, new(Instance, 2, "verified original start"), Actor, false);
        internal EnterpriseRequestApprovalSubmission Binding() => new(Guid.NewGuid(), TenantId, Id, 2, Snapshot.DefinitionVersionId, Instance,
            Snapshot.StartedById, Unit, "Title", Now, Snapshot.StartedAtUtc, null, null, null);
    }
}
