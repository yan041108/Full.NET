using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AcceptTenantInvitation;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.MyTenantRegistrations;
using Full.NET.Modules.Identity.Features.TenantSeatOperations;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>成员重置后工作项 CAS 失败仍回滚，并保持配额调用在事务外。</summary>
[TestClass]
public sealed class SelfTenantRegistrationRetryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_member_or_operation_reset_rolls_back_without_reserving_quota(bool operationFails)
    {
        var now = DateTimeOffset.UtcNow;
        var op = new TenantSeatOperation(Guid.CreateVersion7(), "registration:self", Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), null, null, null, 1, null, TenantSeatPhases.Cancelled, null, "quota.rejected", now, now, null, null, 9, 1, Guid.CreateVersion7());
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var coordinator = new RecordingDbTransactionCoordinator(); var transaction = new DapperCommandTransaction(coordinator);
        var writer = Substitute.For<ICurrentTenantContextWriter>();
        var active = Substitute.For<IIdentityActiveTenantDirectory>();
        active.IsActiveTenantAsync(op.TenantId, Arg.Any<CancellationToken>()).Returns(_ => { Assert.IsFalse(coordinator.HasTransaction); return true; });
        var quota = Substitute.For<ITenantMemberSeatQuotaPort>();
        query.QuerySingleOrDefaultAsync<TenantSeatOperation>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(op);
        query.QuerySingleOrDefaultAsync<RegistrationWayRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new RegistrationWayRecord { Id = op.RegistrationWayId!.Value, TenantId = op.TenantId, IsEnabled = true });
        query.QuerySingleOrDefaultAsync<TenantMemberRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TenantMemberRecord(op.MemberId, op.TenantId, op.UserId, TenantMemberRoles.Member, TenantMemberStatuses.Removed, now, now, 2));
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.IsTrue(coordinator.HasTransaction);
            return operationFails && call.ArgAt<SqlStatement>(0) == SelfTenantRegistrationSql.RetryMember ? 1 : 0;
        });
        var seats = new TenantSeatOperationProcessor(query, commands, transaction, writer, Substitute.For<IActiveTenantContextResolver>(),
            quota, clock, Substitute.For<ITenantInvitationGrantorAuthority>(), Substitute.For<IOutboxWriter>());
        var service = new MyTenantRegistrationService(query, commands, transaction, writer, active, seats, clock, Options.Create(new DatabaseOptions()));
        var result = await service.ContinueAsync(op.UserId, op.Id, op.Version, default);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(operationFails ? IdentityErrorCodes.TenantMemberVersionConflict : IdentityErrorCodes.RegistrationMembershipRetryRejected, result.Error!.Code);
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
        await quota.DidNotReceiveWithAnyArgs().TryReserveAsync(default, default!, default);
    }
}
