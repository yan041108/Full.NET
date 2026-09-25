using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using NSubstitute;

using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;
using IdentityOptions = Full.NET.Modules.Identity.Configuration.IdentityOptions;

namespace Full.NET.UnitTests.Identity;

/// <summary>OIDC 中心登录必须沿用账号失败次数和锁定策略。</summary>
[TestClass]
public sealed class IdentityOidcCenterLoginServiceTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Login_records_outcome_and_rechecks_optimistic_conflicts(bool correctPassword, bool conflict)
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = new FixedClock();
        var userId = Guid.Parse("0199a101-0000-7000-8000-000000000001");
        var hasher = new PasswordHasher<IdentityUser>();
        var passwordHash = "placeholder";
        var user = new IdentityUser(userId, null, "host", "admin", "ADMIN", "Admin", passwordHash, true,
            4, null, "stamp", clock.UtcNow, clock.UtcNow, 7, "zh-CN", 1, "standard", false, null);
        passwordHash = hasher.HashPassword(user, "correct");
        user = user with { PasswordHash = passwordHash };
        var record = new IdentityUserRecord(
            user.Id, user.TenantId, user.ScopeKey, user.Username, user.NormalizedUsername,
            user.DisplayName, user.PasswordHash, user.IsActive, user.FailedLoginCount,
            user.LockoutEndUtc, user.SecurityStamp, user.CreatedAtUtc, user.UpdatedAtUtc,
            user.Version, user.PreferredLocale, user.ProfileVersion, user.AccountType,
            user.MustChangePassword, user.PasswordChangedAtUtc);
        query.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(record);
        command.ExecuteAsync(IdentitySql.UpdateLoginFailure, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(conflict ? 0 : 1, 1);
        command.ExecuteAsync(IdentitySql.UpdateLoginSuccess, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(conflict ? 0 : 1, 1);
        var service = new IdentityOidcCenterLoginService(
            query, command, hasher, clock,
            Options.Create(new IdentityOptions { LockoutThreshold = 5, LockoutMinutes = 15 }));

        var attempt = await service.AuthenticateWithOutcomeAsync(
            "admin", correctPassword ? "correct" : "wrong");
        var result = attempt.Login;
        if (correctPassword)
        {
            Assert.IsNotNull(result);
            Assert.AreEqual("identity.oidc_center_login_succeeded", attempt.ResultCode);
        }
        else
        {
            Assert.IsNull(result);
            Assert.AreEqual("identity.invalid-password", attempt.ResultCode);
            Assert.AreEqual(userId, attempt.UserId);
        }

        await query.Received(conflict ? 2 : 1).QuerySingleOrDefaultAsync<IdentityUserRecord>(
            IdentitySql.FindUserByScopeAndUsername, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        if (correctPassword)
        {
            await command.Received(2).ExecuteAsync(IdentitySql.UpdateLoginSuccess,
                Arg.Any<object?>(), Arg.Any<CancellationToken>());
            return;
        }

        await command.Received(conflict ? 2 : 1).ExecuteAsync(
            IdentitySql.UpdateLoginFailure,
            Arg.Is<LoginFailureUpdate>(update =>
                update!.Id == userId
                && update.FailedLoginCount == 5
                && update.LockoutEndUtc == clock.UtcNow.AddMinutes(15)
                && update.Version == 7),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("disabled")]
    [DataRow("locked")]
    [DataRow("password")]
    [DataRow("contention")]
    public async Task Concurrent_account_change_or_persistent_conflict_never_creates_session(string change)
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var hasher = Substitute.For<IPasswordHasher<IdentityUser>>();
        var clock = new FixedClock();
        var userId = Guid.NewGuid();
        var initial = new IdentityUserRecord { Id = userId, IsActive = true, PasswordHash = "old", Version = 1 };
        var current = new IdentityUserRecord
        {
            Id = userId, IsActive = change != "disabled", Version = 2,
            PasswordHash = change == "password" ? "changed" : "old",
            LockoutEndUtc = change == "locked" ? clock.UtcNow.AddMinutes(5) : null
        };
        query.QuerySingleOrDefaultAsync<IdentityUserRecord>(IdentitySql.FindUserByScopeAndUsername,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(initial, current);
        hasher.VerifyHashedPassword(Arg.Any<IdentityUser>(), "old", "password")
            .Returns(PasswordVerificationResult.Success);
        hasher.VerifyHashedPassword(Arg.Any<IdentityUser>(), "changed", "password")
            .Returns(PasswordVerificationResult.Failed);
        command.ExecuteAsync(IdentitySql.UpdateLoginFailure, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var service = new IdentityOidcCenterLoginService(query, command, hasher, clock,
            Options.Create(new IdentityOptions()));

        var attempt = await service.AuthenticateWithOutcomeAsync("admin", "password");
        Assert.IsNull(attempt.Login);
        Assert.AreEqual(change switch
        {
            "disabled" => "identity.user-disabled",
            "locked" => "identity.user-locked",
            "password" => "identity.invalid-password",
            _ => "identity.login-contention"
        }, attempt.ResultCode);
        await command.Received(change == "contention" ? 32 : 1).ExecuteAsync(
            IdentitySql.UpdateLoginSuccess, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 18, 8, 0, 0, TimeSpan.Zero);
    }
}
