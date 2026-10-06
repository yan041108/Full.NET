using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Features.RegisterAccount;
using Full.NET.Modules.Identity.Features.RegistrationInvitations;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class RegistrationTransactionBoundaryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Incorrect_challenge_is_counted_before_registration_transaction(bool invitationOnly)
    {
        var fixture = new Fixture(invitationOnly, wrongCredential: true);
        var result = await fixture.HandleAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, result.Error!.Code);
        Assert.IsFalse(fixture.FailedAttemptInsideTransaction,
            "错误次数不能随注册业务失败一起回滚。");
        Assert.AreEqual(1, fixture.Writes);
        Assert.AreEqual(0, fixture.Coordinator.BeginCount);
    }

    [TestMethod]
    [DataRow("consumed")]
    [DataRow("exhausted")]
    [DataRow("expired")]
    public async Task Registration_rechecks_challenge_after_prevalidation(string change)
    {
        var fixture = new Fixture();
        fixture.AfterInitialChallengeRead = () => fixture.Challenge = change switch
        {
            "consumed" => fixture.Challenge with { ConsumedAtUtc = DateTimeOffset.UtcNow },
            "exhausted" => fixture.Challenge with { AttemptCount = fixture.Challenge.MaxAttempts },
            _ => fixture.Challenge with { ExpiresAtUtc = DateTimeOffset.MinValue },
        };
        var result = await fixture.HandleAsync();
        Assert.IsFalse(result.IsSuccess, "事务前的校验快照不能替代事务内消费时的重新验证。");
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(1, fixture.Coordinator.RollbackCount);
    }

    [TestMethod]
    public async Task Active_tenant_is_checked_before_transaction_and_local_writes_remain_atomic()
    {
        var fixture = new Fixture();
        var result = await fixture.HandleAsync();
        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(fixture.AuthorityReadInsideTransaction);
        Assert.AreEqual(1, fixture.Coordinator.CommitCount);
        Assert.AreEqual(3, fixture.Writes);
        await fixture.Directory.Received(1).IsActiveTenantAsync(fixture.TenantId, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Inactive_tenant_does_not_start_transaction_or_consume_challenge()
    {
        var fixture = new Fixture { Active = false };
        var result = await fixture.HandleAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.RegistrationWayTenantInactive, result.Error!.Code);
        Assert.AreEqual(0, fixture.Coordinator.BeginCount);
        Assert.AreEqual(0, fixture.Writes);
    }

    [TestMethod]
    [DataRow("tenant")]
    [DataRow("way-version")]
    [DataRow("policy-version")]
    [DataRow("disabled-way")]
    public async Task Changed_registration_target_is_rejected_before_local_writes(string change)
    {
        var fixture = new Fixture();
        fixture.AfterAuthorityRead = () =>
        {
            if (change == "tenant") fixture.Way.TenantId = Guid.NewGuid();
            if (change == "way-version") fixture.Way.Version++;
            if (change == "policy-version") fixture.Policy = fixture.Policy with { Version = 2 };
            if (change == "disabled-way") fixture.Way.IsEnabled = false;
        };
        var result = await fixture.HandleAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.Coordinator.CommitCount);
        Assert.AreEqual(1, fixture.Coordinator.RollbackCount);
    }

    [TestMethod]
    [DataRow("unchanged")]
    [DataRow("tenant")]
    [DataRow("revoked")]
    public async Task Invitation_target_is_rechecked_inside_transaction(string change)
    {
        var fixture = new Fixture(invitationOnly: true);
        fixture.AfterAuthorityRead = () =>
        {
            if (change == "tenant") fixture.Invitation = fixture.Invitation! with { TenantId = Guid.NewGuid() };
            if (change == "revoked") fixture.Invitation = fixture.Invitation! with { RevokedAtUtc = DateTimeOffset.UtcNow };
        };
        var result = await fixture.HandleAsync();
        Assert.IsFalse(fixture.AuthorityReadInsideTransaction);
        Assert.AreEqual(change == "unchanged", result.IsSuccess);
        Assert.AreEqual(change == "unchanged" ? 1 : 0, fixture.Coordinator.CommitCount);
        Assert.AreEqual(change == "unchanged" ? 5 : 0, fixture.Writes);
    }

    private sealed class Fixture
    {
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly RecordingDbTransactionCoordinator Coordinator = new();
        public readonly IIdentityActiveTenantDirectory Directory = Substitute.For<IIdentityActiveTenantDirectory>();
        public readonly RegistrationWayRecord Way;
        public RegistrationPolicyRecord Policy;
        public RegistrationInvitationRecord? Invitation;
        public bool Active = true;
        public bool AuthorityReadInsideTransaction;
        public Action? AfterAuthorityRead;
        public int Writes;
        public bool FailedAttemptInsideTransaction;
        public AccountChallengeRecord Challenge;
        public Action? AfterInitialChallengeRead;
        private int challengeReads;
        private readonly Handler handler;
        private readonly RegisterAccountRequest request;

        public Fixture(bool invitationOnly = false, bool wrongCredential = false)
        {
            var now = DateTimeOffset.UtcNow;
            var query = Substitute.For<IQueryExecutor>();
            var command = Substitute.For<ICommandExecutor>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(now);
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.NewGuid());
            var transaction = new DapperCommandTransaction(Coordinator);
            Way = new RegistrationWayRecord { Id = Guid.NewGuid(), TenantId = TenantId, IsEnabled = true, Version = 1 };
            Policy = new RegistrationPolicyRecord(Guid.NewGuid(), true, (byte)IdentityRegistrationMode.Open, now, 1);
            request = new RegisterAccountRequest("user@example.test", "User", "StrongPassword!123", Guid.NewGuid(), "123456", Way.Id);
            if (invitationOnly)
            {
                Policy = Policy with { RegistrationMode = (byte)IdentityRegistrationMode.InvitationOnly };
                var invitationId = Guid.NewGuid();
                request = request with { InvitationId = invitationId, InvitationToken = "invitation-token" };
                Invitation = new RegistrationInvitationRecord(invitationId, TenantId, request.Email,
                    RegistrationInvitationCredentialHasher.Hash(invitationId, request.InvitationToken), Way.Id,
                    (byte)IdentityRegistrationInvitationStatus.Pending, null, now.AddHours(1), null, null, now, 1);
            }
            query.QuerySingleOrDefaultAsync<RegistrationInvitationRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Invitation);
            query.QuerySingleOrDefaultAsync<RegistrationPolicyRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Policy);
            query.QuerySingleOrDefaultAsync<RegistrationWayRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Way);
            Challenge = new AccountChallengeRecord(request.ChallengeId, (byte)(invitationOnly ? IdentityAccountChallengePurpose.InvitationEmailVerification : IdentityAccountChallengePurpose.RegistrationEmailVerification),
                request.Email, AccountChallengeCredentialHasher.Hash(request.ChallengeId, wrongCredential ? "654321" : request.ChallengeCode), now.AddMinutes(5), null, 0, 5, 1, now);
            query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var snapshot = Challenge;
                    if (++challengeReads == 1) AfterInitialChallengeRead?.Invoke();
                    return snapshot;
                });
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (call.ArgAt<SqlStatement>(0) == AccountChallengeSql.IncrementAttempt)
                {
                    FailedAttemptInsideTransaction |= Coordinator.HasTransaction;
                }
                else
                {
                    Assert.IsTrue(Coordinator.HasTransaction);
                }
                Writes++;
                return 1;
            });
            Directory.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                AuthorityReadInsideTransaction = Coordinator.HasTransaction;
                AfterAuthorityRead?.Invoke();
                return Active;
            });
            var hasher = Substitute.For<IPasswordHasher<IdentityUser>>();
            hasher.HashPassword(Arg.Any<IdentityUser>(), Arg.Any<string>()).Returns("hash");
            handler = new Handler(new RegistrationPolicyService(query, command, clock),
                new AccountChallengeService(query, command, transaction, Substitute.For<IIdentityChallengeDeliveryPort>(), clock, ids),
                new RegistrationInvitationService(query, command, clock, ids), query, command, transaction, hasher, Directory, clock, ids);
        }

        public Task<Full.NET.Abstractions.Results.Result<RegisterAccountResponse>> HandleAsync() =>
            handler.HandleAsync(new Command(request), CancellationToken.None);
    }
}
