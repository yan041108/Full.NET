using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AcceptTenantInvitation;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class TenantInvitationRollbackTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_invitation_or_quota_confirmation_rolls_back_member_write(bool failConfirmation)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.NewGuid());
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        resolver.ResolveActiveByIdAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantContext(tenantId, "tenant", "Tenant"));
        var quota = Substitute.For<ITenantMemberSeatQuotaPort>();
        quota.TryReserveAsync(tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        quota.ReleaseAsync(tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        quota.ConfirmAsync(tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Failure(new Error("test.quota.conflict", "Conflict", ErrorType.Conflict)));
        query.QuerySingleOrDefaultAsync<TenantInvitationRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new TenantInvitationRecord(Guid.NewGuid(), tenantId, "user@example.test", userId,
                Guid.NewGuid(), TenantMemberRoles.Member, "hash", TenantInvitationStatuses.Pending,
                now.AddHours(1), now, now, 1));
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1, failConfirmation ? 1 : 0);
        var coordinator = new RecordingDbTransactionCoordinator();
        var service = new AcceptTenantInvitationService(query, command,
            new DapperCommandTransaction(coordinator), Substitute.For<ICurrentTenantContextWriter>(),
            resolver, quota, clock, ids);

        var result = await service.AcceptAsync(userId, new AcceptTenantInvitationRequest("valid-invitation-token"));

        Assert.IsFalse(result.IsSuccess);
        await command.Received(2).ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
    }
}
