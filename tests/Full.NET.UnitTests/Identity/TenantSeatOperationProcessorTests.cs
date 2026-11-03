using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.TenantSeatOperations;
using Full.NET.Modules.Identity.Features.AcceptTenantInvitation;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>恢复测试保留持久阶段，丢弃执行器实例，模拟请求退出后由下一执行器接续。</summary>
[TestClass]
public sealed class TenantSeatOperationProcessorTests
{
    [TestMethod]
    public async Task Lost_confirmation_response_replays_original_operation_without_second_reserve()
    {
        var f = new Fixture();
        var calls = 0;
        f.Seats.ConfirmAsync(f.Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1 ? throw new IOException("response lost") : Result<bool>.Success(true));
        await Assert.ThrowsExactlyAsync<IOException>(() => f.Processor().ProcessAsync(f.Op.Id, default));
        Assert.AreEqual(TenantSeatPhases.Reserved, f.Op.Phase);
        Assert.IsNull(f.Op.LeaseId);
        var result = await f.Processor().ProcessAsync(f.Op.Id, default);
        Assert.IsTrue(result.IsSuccess && result.Value);
        Assert.AreEqual(TenantSeatPhases.Activated, f.Op.Phase);
        await f.Seats.Received(1).TryReserveAsync(f.Op.TenantId, f.Key, Arg.Any<CancellationToken>());
        await f.Seats.Received(2).ConfirmAsync(f.Op.TenantId, f.Key, Arg.Any<CancellationToken>());
        Assert.IsTrue((await f.Processor().ProcessAsync(f.Op.Id, default)).Value);
        Assert.AreEqual(1, f.Activations);
    }

    [TestMethod]
    public async Task Changed_member_refunds_confirmed_consumption_and_does_not_reopen_invitation()
    {
        var f = new Fixture { AllowActivation = false };
        f.Op = f.Op with { InvitationId = Guid.CreateVersion7() };
        f.Seats.ReleaseAsync(f.Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Failure("quota.already_confirmed"));
        var result = await f.Processor().ProcessAsync(f.Op.Id, default);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenantSeatPhases.Cancelled, f.Op.Phase);
        Assert.AreEqual(0, f.InvitationRestores);
        await f.Seats.Received(1).CancelConsumptionAsync(f.Op.TenantId, f.Key, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Failed_cancellation_is_durable_and_next_executor_retries()
    {
        var f = new Fixture();
        f.Op = f.Op with { Phase = TenantSeatPhases.Cancelling, ErrorCode = "identity.seat_operation.member_removed" };
        f.Seats.ReleaseAsync(f.Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Failure("quota.confirmed"));
        f.Seats.CancelConsumptionAsync(f.Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Failure("quota.unavailable"), Result<bool>.Success(true));
        Assert.IsFalse((await f.Processor().ProcessAsync(f.Op.Id, default)).IsSuccess);
        Assert.AreEqual(TenantSeatPhases.Cancelling, f.Op.Phase);
        Assert.AreEqual(0, f.MemberRestores);
        await f.Processor().ProcessAsync(f.Op.Id, default);
        Assert.AreEqual(TenantSeatPhases.Cancelled, f.Op.Phase);
        Assert.AreEqual(1, f.MemberRestores);
    }

    [TestMethod]
    public async Task Concurrent_removal_cannot_be_overwritten_by_confirmed_phase()
    {
        var f = new Fixture();
        f.Seats.ConfirmAsync(f.Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            f.Op = f.Op with { Phase = TenantSeatPhases.Cancelling, ErrorCode = "identity.seat_operation.member_removed" };
            return Result<bool>.Success(true);
        });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Processor().ProcessAsync(f.Op.Id, default));
        Assert.AreEqual(TenantSeatPhases.Cancelling, f.Op.Phase);
        Assert.AreEqual(0, f.Activations);
        await f.Processor().ProcessAsync(f.Op.Id, default);
        Assert.AreEqual(TenantSeatPhases.Cancelled, f.Op.Phase);
    }

    [TestMethod]
    public async Task Missing_original_metric_does_not_mark_compensation_complete()
    {
        var f = new Fixture();
        f.Op = f.Op with { Phase = TenantSeatPhases.Cancelling };
        f.Seats.ReleaseAsync(f.Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Failure("quota.metric_missing", ErrorType.NotFound));
        f.Seats.CancelConsumptionAsync(f.Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Failure("quota.metric_missing", ErrorType.NotFound));
        Assert.IsFalse((await f.Processor().ProcessAsync(f.Op.Id, default)).IsSuccess);
        Assert.AreEqual(TenantSeatPhases.Cancelling, f.Op.Phase);
        Assert.AreEqual(0, f.MemberRestores);
    }

    [TestMethod]
    public async Task Inactive_tenant_never_reserves_or_activates()
    {
        var f = new Fixture();
        f.Tenants.ResolveActiveByIdAsync(f.Op.TenantId, Arg.Any<CancellationToken>()).Returns((TenantContext?)null);
        await f.Processor().ProcessAsync(f.Op.Id, default);
        Assert.AreEqual(TenantSeatPhases.Cancelled, f.Op.Phase);
        Assert.AreEqual(0, f.Activations);
        await f.Seats.DidNotReceiveWithAnyArgs().TryReserveAsync(default, default!, default);
    }

    [TestMethod]
    public async Task Existing_lease_returns_pending_without_side_effects()
    {
        var f = new Fixture();
        f.Op = f.Op with { LeaseId = Guid.CreateVersion7() };
        var result = await f.Processor().ProcessAsync(f.Op.Id, default);
        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value);
        await f.Seats.DidNotReceiveWithAnyArgs().TryReserveAsync(default, default!, default);
    }

    [TestMethod]
    public async Task Revoked_grantor_cancels_confirmed_activation_before_member_write()
    {
        var f = new Fixture();
        f.Op = f.Op with { InvitationId = Guid.CreateVersion7(), Phase = TenantSeatPhases.Confirmed };
        f.Grantors.IsAuthorizedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var result = await f.Processor().ProcessAsync(f.Op.Id, default);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenantSeatPhases.Cancelled, f.Op.Phase);
        Assert.AreEqual("identity.seat_operation.inviter_unauthorized", f.Op.ErrorCode);
        Assert.AreEqual(0, f.Activations);
        await f.Seats.DidNotReceiveWithAnyArgs().TryReserveAsync(default, default!, default);
    }

    private static Result<bool> Failure(string code, ErrorType type = ErrorType.Conflict) =>
        Result<bool>.Failure(new Error(code, "Test failure", type));

    private sealed class Fixture
    {
        internal TenantSeatOperation Op = new(Guid.CreateVersion7(), "request", Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), null, null, null, 1, null, TenantSeatPhases.Requested, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, 1, 1);
        internal readonly ITenantMemberSeatQuotaPort Seats = Substitute.For<ITenantMemberSeatQuotaPort>();
        internal readonly IActiveTenantContextResolver Tenants = Substitute.For<IActiveTenantContextResolver>();
        internal readonly ITenantInvitationGrantorAuthority Grantors = Substitute.For<ITenantInvitationGrantorAuthority>();
        private readonly IQueryExecutor queries = Substitute.For<IQueryExecutor>();
        private readonly ICommandExecutor commands = Substitute.For<ICommandExecutor>();
        private readonly IClock clock = Substitute.For<IClock>();
        internal bool AllowActivation = true;
        internal int Activations, InvitationRestores, MemberRestores;
        internal string Key => Op.Id.ToString("D") + ":1";

        internal Fixture()
        {
            Grantors.IsAuthorizedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
            queries.QuerySingleOrDefaultAsync<TenantInvitationRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => new TenantInvitationRecord(Op.InvitationId ?? Guid.CreateVersion7(), Op.TenantId,
                    "invitee@example.test", Op.UserId, Guid.CreateVersion7(), "Member", "hash", "Accepted",
                    DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Op.InvitationVersion ?? 1));
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            Tenants.ResolveActiveByIdAsync(Op.TenantId, Arg.Any<CancellationToken>()).Returns(new TenantContext(Op.TenantId, "tenant", "Tenant"));
            queries.QuerySingleOrDefaultAsync<TenantSeatOperation>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Op);
            Seats.TryReserveAsync(Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            Seats.ConfirmAsync(Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            Seats.ReleaseAsync(Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            Seats.CancelConsumptionAsync(Op.TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var sql = call.Arg<SqlStatement>();
                var p = (IReadOnlyDictionary<string, object?>)call[1]!;
                if (sql == TenantSeatOperationSql.Acquire)
                {
                    if (Op.LeaseId is not null) return 0;
                    Op = Op with { LeaseId = (Guid)p["LeaseId"]! };
                }
                else if (sql == TenantSeatOperationSql.Save)
                {
                    if (Op.LeaseId != (Guid)p["LeaseId"]! || Op.Phase != (string)p["ExpectedPhase"]!) return 0;
                    Op = Op with { Phase = (string)p["Phase"]!, ErrorCode = (string?)p["ErrorCode"] };
                }
                else if (sql == TenantSeatOperationSql.ReleaseLease) Op = Op with { LeaseId = null };
                else if (sql == TenantSeatOperationSql.ActivateMember)
                {
                    if (!AllowActivation) return 0;
                    Activations++;
                }
                else if (sql == TenantSeatOperationSql.RestoreInvitation) InvitationRestores++;
                else if (sql == TenantSeatOperationSql.RestoreMember) MemberRestores++;
                return 1;
            });
        }

        internal TenantSeatOperationProcessor Processor() => new(queries, commands,
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()), Substitute.For<ICurrentTenantContextWriter>(), Tenants, Seats, clock, Grantors);
    }
}
