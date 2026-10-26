using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Full.NET.Modules.SerialNumbers.Features.WorkflowCommands;
using Full.NET.Modules.SerialNumbers.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.SerialNumbers;

/// <summary>真实命令执行器验证取消墓碑、输入绑定及损坏凭据，业务回滚由双库事务用例覆盖。</summary>
[TestClass]
public sealed class SerialRuleApprovedCommandExecutorTests
{
    [TestMethod]
    public async Task Compensation_before_apply_creates_cancelled_receipt_without_reading_or_writing_rule()
    {
        var f = Fixture(); var result = await f.Executor.CompensateAsync(f.Command, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("cancelled", result.Value!.OutcomeKey); Assert.IsNull(result.Value.AppliedVersion);
        await f.Commands.Received(1).ExecuteAsync(SerialRuleCommandSql.Insert, Arg.Any<object?>(), CancellationToken.None);
        Assert.AreEqual(1, f.Query.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Apply_after_cancel_is_a_no_effect_replay()
    {
        var f = Fixture(); f.Query.QuerySingleOrDefaultAsync<SerialRuleCommandRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Cancelled(f.Command));
        var result = await f.Executor.ApplyAsync(f.Command, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("cancelled", result.Value!.OutcomeKey); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Reused_identity_cannot_change_original_business_input()
    {
        var f = Fixture(); f.Query.QuerySingleOrDefaultAsync<SerialRuleCommandRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Cancelled(f.Command));
        var result = await f.Executor.ApplyAsync(f.Command with { SnapshotJson = "{\"version\":8}" }, CancellationToken.None);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(SerialNumberErrorCodes.ApprovalCommandInputConflict, result.Error!.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Corrupt_original_snapshot_cannot_be_claimed_as_an_applied_effect()
    {
        var f = Fixture(); var corrupt = Cancelled(f.Command) with { StatusKey = "applied", AppliedVersion = 8, BeforeSnapshotJson = "{" };
        f.Query.QuerySingleOrDefaultAsync<SerialRuleCommandRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(corrupt);
        var result = await f.Executor.ApplyAsync(f.Command, CancellationToken.None);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(SerialNumberErrorCodes.ApprovalCommandReceiptInvalid, result.Error!.Code);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Failed_receipt_insert_returns_a_failure_to_the_result_transaction_boundary()
    {
        var f = Fixture(); f.Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var result = await f.Executor.CompensateAsync(f.Command, CancellationToken.None);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(SerialNumberErrorCodes.ApprovalCommandReceiptInvalid, result.Error!.Code);
    }
    [TestMethod]
    public async Task Tenant_context_cannot_apply_or_compensate_host_rule_commands()
    {
        var f = Fixture(); ((ICurrentTenantContextWriter)f.Tenant).SetTenant(new(Guid.CreateVersion7(), "tenant", "tenant"));
        Assert.IsFalse((await f.Executor.ApplyAsync(f.Command, CancellationToken.None)).IsSuccess);
        Assert.IsFalse((await f.Executor.CompensateAsync(f.Command, CancellationToken.None)).IsSuccess);
        Assert.AreEqual(0, f.Query.ReceivedCalls().Count()); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }
    private static SerialRuleCommandRecord Cancelled(DataApprovalScenarioCommand command)
    {
        Assert.IsTrue(SerialRuleCommandSnapshot.TryCreate(command, out var snapshot));
        return new(command.CommandId, command.ApprovalRequestId, command.TargetEntityId, command.RequestedByUserId, command.ScenarioKey,
            snapshot!.InputHash, null, "cancelled", null, null, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
    private static (SerialRuleApprovedCommandExecutor Executor, DataApprovalScenarioCommand Command, IQueryExecutor Query, ICommandExecutor Commands, CurrentTenantAccessor Tenant) Fixture()
    {
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>(); var transaction = Substitute.For<ICommandTransaction>();
        transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<Result<DataApprovalCommandEffect>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result<DataApprovalCommandEffect>>>>()!(call.Arg<CancellationToken>()));
        transaction.ExecuteResultAsync(Arg.Any<Func<CancellationToken, Task<Result<DataApprovalCommandEffect>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<Result<DataApprovalCommandEffect>>>>()!(call.Arg<CancellationToken>()));
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        var rules = new HostSerialRuleService(query, Substitute.For<IMultiResultQueryExecutor>(), commands, transaction, clock,
            Substitute.For<IIdGenerator>(), options, new(query));
        var tenant = new CurrentTenantAccessor(); ((ICurrentTenantContextWriter)tenant).SetHost();
        var executor = new SerialRuleApprovedCommandExecutor(query, commands, transaction, rules, options, clock, tenant);
        var command = new DataApprovalScenarioCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), DataApprovalScenarioKeys.SerialRuleHostDisable,
            Guid.CreateVersion7(), "{\"version\":7}", Guid.CreateVersion7());
        return (executor, command, query, commands, tenant);
    }
}
