using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>普通写入不能替代审批动作或覆盖已进入流程的单据。</summary>
[TestClass]
public sealed class EnterpriseRequestStateWriteTests
{
    [TestMethod]
    [DataRow("Submitted")]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    [DataRow("draft")]
    [DataRow("")]
    public async Task Create_rejects_non_draft_status_before_insert(string status)
    {
        var f = new Fixture();
        var result = await f.Service.CreateAsync(f.Create with { Status = status }, f.Actor, f.Unit);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestWorkflowErrorCodes.InvalidStatus, result.Error!.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("Submitted")]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    [DataRow("draft")]
    public async Task Update_cannot_change_draft_to_workflow_status(string status)
    {
        var f = new Fixture();
        var result = await f.Service.UpdateAsync(f.Id, f.Update with { Status = status }, f.Actor);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestWorkflowErrorCodes.InvalidStatus, result.Error!.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("Submitted")]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    public async Task Non_draft_cannot_be_edited_or_deleted_even_with_current_version(string status)
    {
        var f = new Fixture(); f.Row = f.Row with { Status = status };
        foreach (var result in new[] {
            await f.Service.UpdateAsync(f.Id, f.Update, f.Actor),
            await f.Service.DeleteAsync(f.Id, new DeleteEnterpriseRequestRequest(f.Row.Version), f.Actor) })
        {
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(EnterpriseRequestWorkflowErrorCodes.InvalidStatus, result.Error!.Code);
        }
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Draft_create_edit_and_delete_keep_existing_write_paths()
    {
        var f = new Fixture();
        Assert.IsTrue((await f.Service.CreateAsync(f.Create, f.Actor, f.Unit)).IsSuccess);
        Assert.IsTrue((await f.Service.UpdateAsync(f.Id, f.Update, f.Actor)).IsSuccess);
        Assert.IsTrue((await f.Service.DeleteAsync(f.Id, new DeleteEnterpriseRequestRequest(f.Row.Version), f.Actor)).IsSuccess);
        // 主从场景删除还会执行明细级联，分别核对三个主表写入口。
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestSql.InsertStatement, Arg.Any<object>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestSql.UpdateStatement, Arg.Any<object>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestSql.DeleteStatement, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Submission_between_guard_read_and_update_is_rejected_by_version_cas()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(EnterpriseRequestSql.UpdateStatement, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(_ => { f.Row = f.Row with { Status = "Submitted", Version = 2 }; return 0; });
        var result = await f.Service.UpdateAsync(f.Id, f.Update, f.Actor);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual("Submitted", f.Row.Status);
    }

    [TestMethod]
    public async Task Parent_delete_version_conflict_rolls_back_prior_detail_delete()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(EnterpriseRequestSql.DeleteStatement, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(0);
        var result = await f.Service.DeleteAsync(f.Id, new DeleteEnterpriseRequestRequest(1), f.Actor);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual(2, f.Commands.ReceivedCalls().Count(), "回归必须经过明细删除及父行 CAS，不能在领域门提前拒绝。");
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    [DataRow("update", 0L)]
    [DataRow("update", 2L)]
    [DataRow("delete", 0L)]
    [DataRow("delete", 2L)]
    public async Task Write_version_must_match_domain_read_before_any_write(string action, long version)
    {
        var f = new Fixture();
        var result = action == "update"
            ? await f.Service.UpdateAsync(f.Id, f.Update with { Version = version }, f.Actor)
            : await f.Service.DeleteAsync(f.Id, new DeleteEnterpriseRequestRequest(version), f.Actor);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("update")]
    [DataRow("delete")]
    public async Task Predicted_future_version_cannot_match_submission_after_domain_snapshot(string action)
    {
        var f = new Fixture(); var reads = 0;
        f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                var snapshot = f.Row;
                if (++reads == (action == "update" ? 2 : 1))
                    f.Row = snapshot with { Status = "Submitted", Version = 2 };
                return snapshot;
            });
        // 模拟实际 SQL CAS：未来版本碰巧等于并发提交后的版本时会成功，而不是强制返回 0。
        f.Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                var statement = call.Arg<SqlStatement>();
                if (statement == EnterpriseRequestSql.UpdateStatement || statement == EnterpriseRequestSql.DeleteStatement)
                {
                    var version = (long)call.Arg<object>()!.GetType().GetProperty("Version")!.GetValue(call.Arg<object>())!;
                    if (version != f.Row.Version) return 0;
                    f.Row = f.Row with { Status = action == "update" ? "Draft" : f.Row.Status, IsDeleted = action == "delete" };
                }
                return 1;
            });
        var result = action == "update"
            ? await f.Service.UpdateAsync(f.Id, f.Update with { Version = 2 }, f.Actor)
            : await f.Service.DeleteAsync(f.Id, new DeleteEnterpriseRequestRequest(2), f.Actor);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
        Assert.AreEqual("Submitted", f.Row.Status); Assert.IsFalse(f.Row.IsDeleted);
    }

    private sealed class Fixture
    {
        internal readonly Guid Id = Guid.NewGuid(), Actor = Guid.NewGuid(), Unit = Guid.NewGuid();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal EnterpriseRequestRecord Row;
        internal readonly EnterpriseRequestManagementService Service;
        internal readonly CreateEnterpriseRequestRequest Create;
        internal readonly UpdateEnterpriseRequestRequest Update;
        internal Fixture()
        {
            var tenant = Substitute.For<ICurrentTenant>();
            tenant.IsAvailable.Returns(true); tenant.Id.Returns(Guid.NewGuid());
            Row = new(Id, tenant.Id!.Value, Unit, "REQ", "Draft request", "Draft", 1m, Actor,
                1, DateTimeOffset.UtcNow, Actor, null, null, false, null, null);
            Create = new("REQ", "Draft request", "Draft", 1m, Actor);
            Update = new("REQ", "Edited draft", "Draft", 1m, Actor, 1);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ => Row);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(1);
            var transaction = new DapperCommandTransaction(Coordinator);
            var authorizer = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
            authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            var queries = new EnterpriseRequestQueryService(Queries, Options.Create(new DatabaseOptions()),
                Substitute.For<IUserDataScopeResolver>(), Substitute.For<IDataScopeSqlFilterBuilder>());
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Id);
            Service = new(Queries, Commands, transaction, queries, tenant, Substitute.For<IClock>(), ids, authorizer);
        }
    }
}
