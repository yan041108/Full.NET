using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using NSubstitute;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class LoginHandlerTests
{
    private const string Password = "FullNet!2026Secure";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 3, 4, 5, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("01981a75-f500-7000-8000-000000000001");
    private static readonly Guid SessionId =
        Guid.Parse("01981a75-f500-7000-8000-000000000002");
    private static readonly Guid FamilyId =
        Guid.Parse("01981a75-f500-7000-8000-000000000003");
    private static readonly Guid AuditId =
        Guid.Parse("01981a75-f500-7000-8000-000000000004");

    [TestMethod]
    public async Task Unknown_user_returns_same_public_error_and_writes_audit()
    {
        var passwordHasher = new CountingPasswordHasher();
        var fixture = new Fixture(passwordHasher);
        fixture.QueryExecutor
            .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((IdentityUserRecord?)null);

        var result = await fixture.Handler.HandleAsync(CreateCommand(), default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("identity.invalid_credentials", result.Error?.Code);
        Assert.AreEqual(1, passwordHasher.VerificationCount);
        await fixture.CommandExecutor.Received(1).ExecuteAsync(
            IdentitySql.InsertAuthAudit,
            Arg.Is<AuthAuditEvent>(audit =>
                audit != null
                && !audit.Succeeded
                && audit.ResultCode == "identity.user-not-found"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Fifth_invalid_password_locks_account_without_exposing_reason()
    {
        var fixture = new Fixture();
        fixture.ReturnUser(failedLoginCount: 4);

        var result = await fixture.Handler.HandleAsync(
            CreateCommand(password: "Wrong!2026Password"),
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("identity.invalid_credentials", result.Error?.Code);
        await fixture.CommandExecutor.Received(1).ExecuteAsync(
            IdentitySql.UpdateLoginFailure,
            Arg.Is<LoginFailureUpdate>(update =>
                update != null
                && update.FailedLoginCount == 5
                && update.LockoutEndUtc == Now.AddMinutes(15)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Valid_password_creates_hashed_refresh_session_and_access_token()
    {
        var fixture = new Fixture();
        fixture.ReturnUser();

        var result = await fixture.Handler.HandleAsync(CreateCommand(), default);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("access-token", result.Value!.Token.AccessToken);
        Assert.AreEqual("refresh-token", result.Value.RefreshToken);
        Assert.AreEqual("csrf-token", result.Value.CsrfToken);
        await fixture.CommandExecutor.Received(1).ExecuteAsync(
            IdentitySql.InsertRefreshSession,
            Arg.Is<RefreshSession>(session =>
                session != null
                && session.Id == SessionId
                && session.FamilyId == FamilyId
                && session.TokenHash == TokenHash.Compute("refresh-token")),
            Arg.Any<CancellationToken>());
    }

    private static Command CreateCommand(string password = Password) => new(
        " admin ",
        password,
        new ClientRequestContext("127.0.0.1", "unit-test"));

    private sealed class Fixture
    {
        private readonly Microsoft.AspNetCore.Identity.PasswordHasher<IdentityUser>
            _passwordHasher = new();

        public Fixture(
            Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser>?
                passwordHasher = null)
        {
            QueryExecutor = Substitute.For<IQueryExecutor>();
            CommandExecutor = Substitute.For<ICommandExecutor>();
            CommandExecutor.ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            Handler = new Handler(
                QueryExecutor,
                CommandExecutor,
                passwordHasher ?? _passwordHasher,
                new FixedClock(),
                new QueueIdGenerator(SessionId, FamilyId, AuditId),
                new StubAccessTokenIssuer(),
                new QueueTokenGenerator("refresh-token", "csrf-token"),
                Options.Create(new IdentityOptions
                {
                    AllowDevelopmentEphemeralSigningKey = true,
                }));
        }

        public IQueryExecutor QueryExecutor { get; }

        public ICommandExecutor CommandExecutor { get; }

        public Handler Handler { get; }

        public void ReturnUser(int failedLoginCount = 0)
        {
            var user = new IdentityUser(
                UserId,
                null,
                "host",
                "admin",
                "ADMIN",
                "系统管理员",
                string.Empty,
                true,
                failedLoginCount,
                null,
                "stamp",
                Now.AddDays(-1),
                null,
                1);
            var passwordHash = _passwordHasher.HashPassword(user, Password);
            QueryExecutor
                .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(new IdentityUserRecord(
                    user.Id,
                    user.TenantId,
                    user.ScopeKey,
                    user.Username,
                    user.NormalizedUsername,
                    user.DisplayName,
                    passwordHash,
                    user.IsActive,
                    user.FailedLoginCount,
                    user.LockoutEndUtc,
                    user.SecurityStamp,
                    user.CreatedAtUtc,
                    user.UpdatedAtUtc,
                    user.Version));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class QueueIdGenerator(params Guid[] ids) : IIdGenerator
    {
        private readonly Queue<Guid> _ids = new(ids);

        public Guid NewId() => _ids.Dequeue();
    }

    private sealed class StubAccessTokenIssuer : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(IdentityUser user, Guid sessionId) =>
            new("access-token", Now.AddMinutes(10));
    }

    private sealed class QueueTokenGenerator(params string[] tokens) : IRandomTokenGenerator
    {
        private readonly Queue<string> _tokens = new(tokens);

        public string Generate(int byteCount) => _tokens.Dequeue();
    }

    private sealed class CountingPasswordHasher
        : Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser>
    {
        private readonly Microsoft.AspNetCore.Identity.PasswordHasher<IdentityUser>
            _inner = new();

        public int VerificationCount { get; private set; }

        public string HashPassword(IdentityUser user, string password) =>
            _inner.HashPassword(user, password);

        public Microsoft.AspNetCore.Identity.PasswordVerificationResult
            VerifyHashedPassword(
                IdentityUser user,
                string hashedPassword,
                string providedPassword)
        {
            VerificationCount++;
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
