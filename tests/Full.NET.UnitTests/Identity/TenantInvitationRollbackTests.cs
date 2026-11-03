using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AcceptTenantInvitation;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.TenantSeatOperations;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>单元测试（Identity）：Tenant Invitation Rollback。</summary>
[TestClass]
public sealed class TenantInvitationRollbackTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_member_or_invitation_write_rolls_back_without_reserving_quota(bool failConfirmation)
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
            .Returns(failConfirmation ? 1 : 0, 0);
        var coordinator = new RecordingDbTransactionCoordinator();
        var writer = Substitute.For<ICurrentTenantContextWriter>();
        var transaction = new DapperCommandTransaction(coordinator);
        var authority = Substitute.For<ITenantInvitationGrantorAuthority>();
        authority.IsAuthorizedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var processor = new TenantSeatOperationProcessor(query, command, transaction, writer, resolver, quota, clock, authority, Substitute.For<IOutboxWriter>());
        var service = new AcceptTenantInvitationService(query, command,
            transaction, writer, resolver, processor, clock, ids, authority);

        var result = await service.AcceptAsync(userId, new AcceptTenantInvitationRequest("valid-invitation-token"));

        Assert.IsFalse(result.IsSuccess);
        await command.Received(failConfirmation ? 2 : 1).ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await quota.DidNotReceiveWithAnyArgs().TryReserveAsync(default, default!, default);
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
    }
}
