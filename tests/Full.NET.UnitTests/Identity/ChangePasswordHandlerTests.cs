using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ChangePassword;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using IdentityOptions = Full.NET.Modules.Identity.Configuration.IdentityOptions;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class ChangePasswordHandlerTests
{
    private const string Password = "FullNet!2026Secure";
    private const string NewPassword = "FullNet!2026Rotate";
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid SessionId = Guid.CreateVersion7();
    private static readonly Guid FamilyId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 1, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Wrong_current_password_returns_current_password_invalid()
    {
        var hasher = new PasswordHasher<IdentityUser>();
        var user = CreateUser(hasher.HashPassword(CreateUser(string.Empty), Password));
        var fixture = CreateFixture(user, hasher);

        var result = await fixture.Handler.HandleAsync(
            CreateCommand("wrong-password", NewPassword, user.SecurityStamp),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.CurrentPasswordInvalid, result.Error!.Code);
        await fixture.CommandExecutor.DidNotReceive().ExecuteAsync(
            IdentitySql.ResetUserPasswordByIdentity,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Same_new_password_returns_validation_error()
    {
        var hasher = new PasswordHasher<IdentityUser>();
        var user = CreateUser(hasher.HashPassword(CreateUser(string.Empty), Password));
        var fixture = CreateFixture(user, hasher);

        var result = await fixture.Handler.HandleAsync(
            CreateCommand(Password, Password, user.SecurityStamp),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.NewPasswordSameAsCurrent, result.Error!.Code);
    }

    [TestMethod]
    public async Task Successful_change_rotates_sessions_and_returns_tokens()
    {
        var hasher = new PasswordHasher<IdentityUser>();
        var user = CreateUser(hasher.HashPassword(CreateUser(string.Empty), Password));
        var newStamp = Guid.CreateVersion7().ToString("N");
        var sessionState = CreateSession(user);
        var fixture = CreateFixture(user, hasher, sessionState, newStamp);
        fixture.CommandExecutor.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<SqlStatement>(0) == IdentitySql.ResetUserPasswordByIdentity)
                {
                    sessionState.SecurityStamp = newStamp;
                    sessionState.UserVersion += 1;
                }

                return 1;
            });

        var result = await fixture.Handler.HandleAsync(
            CreateCommand(Password, NewPassword, user.SecurityStamp),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Value!.Token.AccessToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Value.RefreshToken));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Value.CsrfToken));
        await fixture.CommandExecutor.Received(1).ExecuteAsync(
            IdentitySql.RevokeUserSessionsExcept,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private static Command CreateCommand(
        string currentPassword,
        string newPassword,
        string securityStamp) =>
        new(
            currentPassword,
            newPassword,
            CreatePrincipal(securityStamp),
            new ClientRequestContext("127.0.0.1", "unit-test"));

    private static ClaimsPrincipal CreatePrincipal(string securityStamp)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, UserId.ToString("D")));
        identity.AddClaim(new Claim(IdentityClaimTypes.SessionId, SessionId.ToString("D")));
        identity.AddClaim(new Claim(IdentityClaimTypes.ActorScope, "host"));
        identity.AddClaim(new Claim(IdentityClaimTypes.SecurityStamp, securityStamp));
        return new ClaimsPrincipal(identity);
    }

    private static IdentityUser CreateUser(string passwordHash) => new(
        UserId,
        null,
        "host",
        "change-password-user",
        "CHANGE-PASSWORD-USER",
        "Change Password User",
        passwordHash,
        true,
        0,
        null,
        Guid.NewGuid().ToString("N"),
        Now,
        Now,
        1);

    private static Fixture CreateFixture(
        IdentityUser user,
        IPasswordHasher<IdentityUser> hasher,
        RefreshSessionRecord? sessionState = null,
        string? nextSecurityStamp = null)
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        sessionState ??= CreateSession(user);
        queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(sessionState);

        var commandExecutor = Substitute.For<ICommandExecutor>();
        commandExecutor.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var permissionSnapshots = Substitute.For<IPermissionSnapshotReader>();
        permissionSnapshots.ReadAsync(
                UserId,
                "host",
                null,
                Arg.Any<CancellationToken>())
            .Returns(new PermissionSnapshot([], false));

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var idGenerator = Substitute.For<IIdGenerator>();
        if (nextSecurityStamp is not null
            && Guid.TryParse(nextSecurityStamp, out var parsedStamp))
        {
            idGenerator.NewId().Returns(parsedStamp);
        }
        else
        {
            idGenerator.NewId().Returns(_ => Guid.CreateVersion7());
        }

        var accessTokenIssuer = Substitute.For<IAccessTokenIssuer>();
        accessTokenIssuer.Issue(
                Arg.Any<IdentityUser>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<bool>())
            .Returns(new IssuedAccessToken("access-token", Now.AddMinutes(15)));

        var randomTokenGenerator = Substitute.For<IRandomTokenGenerator>();
        randomTokenGenerator.Generate(Arg.Any<int>()).Returns("random-token-value");

        var handler = new Handler(
            queryExecutor,
            commandExecutor,
            hasher,
            clock,
            idGenerator,
            permissionSnapshots,
            accessTokenIssuer,
            randomTokenGenerator,
            Options.Create(new IdentityOptions
            {
                ClientId = "admin",
                RefreshTokenDays = 14,
            }));

        return new Fixture(handler, commandExecutor);
    }

    private static RefreshSessionRecord CreateSession(IdentityUser user) => new()
    {
        SessionId = SessionId,
        UserId = user.Id,
        FamilyId = FamilyId,
        ClientId = "admin",
        TokenHash = "hash",
        ExpiresAtUtc = Now.AddDays(1),
        SessionVersion = 1,
        ScopeKey = user.ScopeKey,
        Username = user.Username,
        NormalizedUsername = user.NormalizedUsername,
        DisplayName = user.DisplayName,
        PasswordHash = user.PasswordHash,
        IsActive = true,
        SecurityStamp = user.SecurityStamp,
        UserCreatedAtUtc = user.CreatedAtUtc,
        UserUpdatedAtUtc = user.UpdatedAtUtc,
        UserVersion = user.Version,
        PreferredLocale = "zh-CN",
        ProfileVersion = 1,
    };

    private sealed record Fixture(Handler Handler, ICommandExecutor CommandExecutor);
}
