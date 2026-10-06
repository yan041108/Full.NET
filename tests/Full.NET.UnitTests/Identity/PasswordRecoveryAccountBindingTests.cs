using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.RecoverAccount;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class PasswordRecoveryAccountBindingTests
{
    [TestMethod]
    [DataRow("missing-context")]
    [DataRow("empty-user-id")]
    public async Task Recovery_creation_requires_a_nonempty_trusted_account(string scenario)
    {
        var fixture = new Fixture();
        if (scenario == "missing-context")
        {
            var result = await fixture.Service.CreateAndDeliverAsync(
                IdentityAccountChallengePurpose.PasswordRecovery, "owner@example.test");
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, result.Error!.Code);
        }
        else
        {
            fixture.CurrentUser.Id = Guid.Empty;
            var result = await fixture.Request.HandleAsync(new RequestCommand(
                new RequestPasswordRecoveryRequest("owner@example.test")), CancellationToken.None);
            Assert.IsTrue(result.IsSuccess);
        }
        Assert.IsNull(fixture.Challenge, "账号上下文缺失时不得持久化真实恢复凭据。");
        Assert.IsNull(fixture.Intent);
    }

    [TestMethod]
    [DataRow("same-account")]
    [DataRow("reassigned-email")]
    [DataRow("legacy-unbound")]
    public async Task Recovery_credential_authorizes_only_the_original_account(string scenario)
    {
        var fixture = new Fixture();
        await fixture.CreateAsync();
        if (scenario == "reassigned-email")
        {
            fixture.CurrentUser = fixture.NewUser();
        }
        else if (scenario == "legacy-unbound")
        {
            fixture.Challenge = fixture.Challenge! with
            {
                CredentialHash = AccountChallengeCredentialHasher.Hash(
                    fixture.Intent!.ChallengeId, fixture.Intent.Credential),
            };
        }
        var result = await fixture.Confirm.HandleAsync(new ConfirmCommand(
            new ConfirmPasswordRecoveryRequest(fixture.Intent!.ChallengeId,
                fixture.Intent.Credential, "FullNet!2026Recovered")), CancellationToken.None);
        if (scenario == "same-account")
        {
            Assert.IsTrue(result.IsSuccess);
            CollectionAssert.AreEqual(new[] { fixture.CurrentUser.Id }, fixture.PasswordResetUsers);
        }
        else
        {
            Assert.IsFalse(result.IsSuccess, "恢复凭据必须绑定发起时的稳定账号，未绑定旧码也必须拒绝。");
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, result.Error!.Code);
            Assert.AreEqual(0, fixture.PasswordResetUsers.Count);
        }
    }

    [TestMethod]
    public async Task Recovery_rejects_a_concurrent_account_update_using_the_read_version()
    {
        var fixture = new Fixture();
        fixture.CurrentUser.Version = 7;
        await fixture.CreateAsync();
        fixture.RejectPasswordReset = true;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Confirm.HandleAsync(new ConfirmCommand(
            new ConfirmPasswordRecoveryRequest(fixture.Intent!.ChallengeId,
                fixture.Intent.Credential, "FullNet!2026Recovered")), CancellationToken.None));
        Assert.IsNotNull(fixture.ResetParameters);
        Assert.IsTrue(fixture.ResetParameters.TryGetValue("Version", out var version), "改密必须携带权威读取的账号版本。");
        Assert.AreEqual(7, version);
    }

    [TestMethod]
    [DataRow("password-changed")]
    [DataRow("security-stamp-rotated")]
    [DataRow("disabled-and-reenabled")]
    [DataRow("legacy-user-only")]
    public async Task Recovery_credential_does_not_outlive_authoritative_security_state(string scenario)
    {
        var fixture = new Fixture();
        await fixture.CreateAsync();
        if (scenario == "legacy-user-only")
        {
            var payload = $"password-recovery:v1:{fixture.Intent!.ChallengeId:N}:{fixture.CurrentUser.Id:N}:{fixture.Intent.Credential.Trim()}";
            fixture.Challenge = fixture.Challenge! with { CredentialHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant() };
        }
        else
        {
            if (scenario == "disabled-and-reenabled") fixture.CurrentUser.IsActive = false;
            fixture.CurrentUser.SecurityStamp = "changed-security-stamp";
            fixture.CurrentUser.IsActive = true;
            fixture.CurrentUser.Version++;
        }
        var result = await fixture.Confirm.HandleAsync(new ConfirmCommand(new ConfirmPasswordRecoveryRequest(
            fixture.Intent!.ChallengeId, fixture.Intent.Credential, "FullNet!2026Recovered")), CancellationToken.None);
        Assert.IsFalse(result.IsSuccess, "账号安全状态变更或旧摘要不能继续授权恢复。");
        Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, result.Error!.Code);
        Assert.AreEqual(0, fixture.PasswordResetUsers.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow(null)]
    public async Task Missing_authoritative_security_stamp_creates_only_an_accepted_placeholder(string? stamp)
    {
        var fixture = new Fixture();
        fixture.CurrentUser.SecurityStamp = stamp!;
        var result = await fixture.Request.HandleAsync(new RequestCommand(
            new RequestPasswordRecoveryRequest("owner@example.test")), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.IsNull(fixture.Challenge);
        Assert.IsNull(fixture.Intent);
    }

    private sealed class Fixture
    {
        private readonly DateTimeOffset now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
        public RequestHandler Request { get; }
        public AccountChallengeService Service { get; }
        public IdentityUserRecord CurrentUser { get; set; }
        public AccountChallengeRecord? Challenge { get; set; }
        public IdentityChallengeDeliveryIntent? Intent { get; private set; }
        public List<Guid> PasswordResetUsers { get; } = [];
        public ConfirmHandler Confirm { get; }
        public bool RejectPasswordReset { get; set; }
        public IReadOnlyDictionary<string, object?>? ResetParameters { get; private set; }

        public Fixture()
        {
            CurrentUser = NewUser();
            var query = Substitute.For<IQueryExecutor>();
            query.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<IdentityUserRecord?>(CurrentUser));
            query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Challenge));
            var commands = Substitute.For<ICommandExecutor>();
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var sql = call.ArgAt<SqlStatement>(0);
                    if (sql.Equals(AccountChallengeSql.Insert))
                    {
                        var p = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
                        Challenge = new AccountChallengeRecord((Guid)p["ChallengeId"]!,
                            (byte)p["Purpose"]!, (string)p["NormalizedEmail"]!, (string)p["CredentialHash"]!,
                            (DateTimeOffset)p["ExpiresAtUtc"]!, null, 0, (int)p["MaxAttempts"]!, 1,
                            (DateTimeOffset)p["CreatedAtUtc"]!);
                    }
                    else if (sql.Equals(IdentitySql.ResetUserPasswordByIdentity))
                    {
                        var p = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
                        ResetParameters = p;
                        if (RejectPasswordReset) return Task.FromResult(0);
                        PasswordResetUsers.Add((Guid)p["UserId"]!);
                    }
                    return Task.FromResult(1);
                });
            var transaction = Substitute.For<ICommandTransaction>();
            transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<Func<CancellationToken, Task<bool>>>(0)(call.ArgAt<CancellationToken>(1)));
            transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<Result<bool>>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<Func<CancellationToken, Task<Result<bool>>>>(0)(call.ArgAt<CancellationToken>(1)));
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.CreateVersion7());
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(now);
            var delivery = Substitute.For<IIdentityChallengeDeliveryPort>();
            delivery.SendAsync(Arg.Any<IdentityChallengeDeliveryIntent>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Intent = call.ArgAt<IdentityChallengeDeliveryIntent>(0);
                    return Task.FromResult(Result<bool>.Success(true));
                });
            Service = new AccountChallengeService(query, commands, transaction, delivery, clock, ids);
            Request = new RequestHandler(Service, query);
            Confirm = new ConfirmHandler(Service, query, commands, transaction,
                new AuthenticationSecurityEventWriter(commands, ids, clock), new PasswordHasher<IdentityUser>(),
                Substitute.For<IIdentityOidcUserAuthorityRevoker>(), clock, ids);
        }

        public IdentityUserRecord NewUser() => new()
        {
            Id = Guid.CreateVersion7(), IsActive = true, ScopeKey = "host", Username = "target",
            NormalizedUsername = "TARGET", DisplayName = "Target", SecurityStamp = "stamp",
            CreatedAtUtc = now, Version = 1,
        };

        public async Task CreateAsync()
        {
            var result = await Request.HandleAsync(new RequestCommand(
                new RequestPasswordRecoveryRequest("owner@example.test")), CancellationToken.None);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(Challenge);
            Assert.IsNotNull(Intent);
        }
    }
}
