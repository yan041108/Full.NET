using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Features.ManageTotp;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;
using IdentityOptions = Full.NET.Modules.Identity.Configuration.IdentityOptions;

namespace Full.NET.UnitTests.Identity;

/// <summary>登记入口不能覆盖已启用或已被并发确认的 TOTP 凭据。</summary>
[TestClass]
public sealed class TotpEnrollmentProtectionTests
{
    [TestMethod]
    public async Task Enabled_credential_cannot_be_replaced_by_begin()
    {
        var fixture = new Fixture(true);
        var result = await fixture.Service.BeginAsync(fixture.Principal);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Conflict, result.Error!.Type);
        Assert.AreEqual("identity.mfa.totp_enrollment_conflict", result.Error.Code);
        Assert.AreEqual(0, fixture.Writes.Count);
    }

    [TestMethod]
    public async Task Concurrent_confirmation_returns_conflict_without_secret_or_success_audit()
    {
        var fixture = new Fixture(false) { AffectedRows = 0 };
        var result = await fixture.Service.BeginAsync(fixture.Principal);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Conflict, result.Error!.Type);
        Assert.AreEqual("identity.mfa.totp_enrollment_conflict", result.Error.Code);
        Assert.AreEqual(1, fixture.Writes.Count);
        Assert.AreEqual(IdentitySql.ResetUserTotpPending, fixture.Writes[0]);
    }

    [TestMethod]
    public async Task Pending_restart_uses_observed_version_and_audits_success()
    {
        var fixture = new Fixture(false);
        var result = await fixture.Service.BeginAsync(fixture.Principal);
        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(fixture.Parameters!.TryGetValue("Version", out var version));
        Assert.AreEqual(7, version);
        Assert.HasCount(2, fixture.Writes);
        Assert.AreEqual(IdentitySql.InsertAuthAudit, fixture.Writes[1]);
    }

    [TestMethod]
    public async Task Concurrent_first_begin_returns_conflict_without_success_audit()
    {
        var fixture = new Fixture(null) { InsertConflict = true };
        var result = await fixture.Service.BeginAsync(fixture.Principal);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Conflict, result.Error!.Type);
        Assert.HasCount(1, fixture.Writes);
    }

    [TestMethod]
    public async Task Audit_failure_is_propagated_to_transaction_boundary()
    {
        var fixture = new Fixture(null) { AuditFailure = true };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Service.BeginAsync(fixture.Principal));
    }

    [TestMethod]
    public async Task First_begin_succeeds_and_returns_protected_secret_only_in_response()
    {
        var fixture = new Fixture(null);
        var result = await fixture.Service.BeginAsync(fixture.Principal);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(IdentitySql.InsertUserTotpPending, fixture.Writes[0]);
        Assert.AreEqual(result.Value!.SharedSecretBase32,
            fixture.Protector.Unprotect((string)fixture.Parameters!["SecretProtected"]!));
        Assert.AreNotEqual(result.Value.SharedSecretBase32, fixture.Parameters["SecretProtected"]);
    }

    private sealed class Fixture
    {
        public int AffectedRows { get; set; } = 1;
        public bool InsertConflict { get; set; }
        public bool AuditFailure { get; set; }
        public List<SqlStatement> Writes { get; } = [];
        public IReadOnlyDictionary<string, object?>? Parameters { get; private set; }
        public TotpSecretProtector Protector { get; } = new(new EphemeralDataProtectionProvider());
        public ClaimsPrincipal Principal { get; }
        public TotpEnrollmentService Service { get; }

        public Fixture(bool? enabled)
        {
            var id = Guid.CreateVersion7();
            Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "test"));
            var query = Substitute.For<IQueryExecutor>();
            query.QuerySingleOrDefaultAsync<IdentityUserRecord>(Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new IdentityUserRecord { Id = id, IsActive = true, Username = "totp-user" });
            query.QuerySingleOrDefaultAsync<IdentityUserTotpRecord>(Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(enabled is null ? null : new IdentityUserTotpRecord
                    { UserId = id, IsEnabled = enabled.Value, Version = 7, SecretProtected = "protected-fixture" });
            var commands = Substitute.For<ICommandExecutor>();
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var sql = call.ArgAt<SqlStatement>(0);
                    Writes.Add(sql);
                    if (sql.Equals(IdentitySql.InsertAuthAudit))
                    {
                        if (AuditFailure) throw new InvalidOperationException("audit unavailable");
                        return 1;
                    }
                    if (InsertConflict) throw new DataCommandException(DataCommandFailureKind.UniqueConstraint,
                        new InvalidOperationException("concurrent insert"));
                    Parameters = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
                    return AffectedRows;
                });
            var transaction = Substitute.For<ICommandTransaction>();
            transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<Func<CancellationToken, Task<bool>>>(0)(call.ArgAt<CancellationToken>(1)));
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.CreateVersion7());
            Service = new TotpEnrollmentService(query, commands, transaction,
                new AuthenticationSecurityEventWriter(commands, ids, clock), Protector,
                Options.Create(new IdentityOptions { Issuer = "Full.NET" }), clock);
        }
    }
}
