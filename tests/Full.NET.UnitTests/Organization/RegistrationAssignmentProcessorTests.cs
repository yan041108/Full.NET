using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Features.RegistrationAssignments;
using Full.NET.Modules.Organization.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

/// <summary>注册分配只补缺失关系；目录复核与本地事务的时序以及人工关系保护不能退化。</summary>
[TestClass]
public sealed class RegistrationAssignmentProcessorTests
{
    [TestMethod]
    public async Task Current_activation_is_checked_before_local_transaction_and_missing_relationships_are_added()
    {
        var f = new Fixture();
        await f.Processor.ProcessAsync(f.Intent, true, default);
        Assert.AreEqual(1, f.Coordinator.CommitCount);
        Assert.AreEqual(0, f.Coordinator.RollbackCount);
        await f.Commands.Received(1).ExecuteAsync(OrganizationSql.InsertUserUnit,
            Arg.Is<object?>(value => value != null && (bool)((Dictionary<string, object?>)value)["IsPrimary"]! == false), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(OrganizationSql.InsertUserPosition, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Inactive_tenant_or_changed_member_skips_without_organization_writes(bool tenantActive)
    {
        var f = new Fixture();
        f.Members.IsCurrentActivationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(false);
        await f.Processor.ProcessAsync(f.Intent, tenantActive, default);
        await f.Commands.Received(1).ExecuteAsync(RegistrationAssignmentSql.Finish,
            Arg.Is<object?>(value => value != null && (string)((Dictionary<string, object?>)value)["Status"]! == "Skipped"), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(OrganizationSql.InsertUserUnit, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(OrganizationSql.InsertUserPosition, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Existing_inactive_or_primary_relationships_are_preserved()
    {
        var f = new Fixture();
        f.Queries.QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizationUserUnitRecord { Id = Guid.CreateVersion7(), IsActive = false });
        f.Queries.QuerySingleOrDefaultAsync<OrganizationUserPositionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new OrganizationUserPositionRecord { Id = Guid.CreateVersion7(), IsActive = true, IsPrimary = true });
        await f.Processor.ProcessAsync(f.Intent, true, default);
        await f.Commands.Received(1).ExecuteAsync(RegistrationAssignmentSql.Finish, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_position_rolls_back_completion_and_all_relationships()
    {
        var f = new Fixture();
        f.Queries.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((OrganizationPositionRecord?)null);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Processor.ProcessAsync(f.Intent, true, default));
        Assert.AreEqual("organization.registration_assignment.position_unavailable", error.Message);
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
        await f.Commands.DidNotReceive().ExecuteAsync(OrganizationSql.InsertUserUnit, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Lost_intent_version_does_not_repeat_side_effects()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(RegistrationAssignmentSql.Finish, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await f.Processor.ProcessAsync(f.Intent, true, default);
        Assert.AreEqual(1, f.Commands.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly ITenantMemberSelectionDirectory Members = Substitute.For<ITenantMemberSelectionDirectory>();
        internal readonly RegistrationAssignmentRecord Intent;
        internal readonly RegistrationAssignmentProcessor Processor;
        internal Fixture()
        {
            var now = DateTimeOffset.UtcNow;
            Intent = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 2,
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Pending", null, now, now, 1);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Members.IsCurrentActivationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ =>
            { Assert.IsFalse(Coordinator.HasTransaction); return true; });
            Queries.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new OrganizationUnitRecord(Intent.UnitId, Intent.TenantId, null, "unit", "Unit", 0, true, now, null, 1));
            Queries.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new OrganizationPositionRecord(Intent.PositionId!.Value, Intent.TenantId, "position", "Position", null, null, null,
                    null, null, null, 0, true, now, null, 1));
            Processor = new(Queries, Commands, new DapperCommandTransaction(Coordinator), Members, clock, ids);
        }
    }
}
