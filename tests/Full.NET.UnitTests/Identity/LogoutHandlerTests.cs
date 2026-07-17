using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Features.Logout;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class LogoutHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 6, 7, 8, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("01981b1b-8800-7000-8000-000000000001");
    private static readonly Guid SessionId =
        Guid.Parse("01981b1b-8800-7000-8000-000000000002");
    private static readonly Guid FamilyId =
        Guid.Parse("01981b1b-8800-7000-8000-000000000003");
    private static readonly Guid AuditId =
        Guid.Parse("01981b1b-8800-7000-8000-000000000004");

    [TestMethod]
    public async Task Logout_revokes_entire_family_when_presented_session_is_not_consumed()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionByHash,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateRecord());
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var handler = new Handler(
            query,
            command,
            new FixedClock(),
            new FixedIdGenerator());

        var result = await handler.HandleAsync(
            new Command(
                "presented-token",
                new ClientRequestContext("127.0.0.1", "unit-test")),
            default);

        Assert.IsTrue(result.IsSuccess);
        await command.Received(1).ExecuteAsync(
            IdentitySql.RevokeRefreshFamily,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await command.DidNotReceive().ExecuteAsync(
            Arg.Is<SqlStatement>(statement =>
                statement != null
                && statement.Name == "identity.revoke-refresh-session"),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private static RefreshSessionRecord CreateRecord() => new()
    {
        SessionId = SessionId,
        UserId = UserId,
        FamilyId = FamilyId,
        TokenHash = TokenHash.Compute("presented-token"),
        ExpiresAtUtc = Now.AddDays(1),
        CreatedAtUtc = Now.AddHours(-1),
        SessionVersion = 1,
        ScopeKey = "host",
        Username = "admin",
        NormalizedUsername = "ADMIN",
        DisplayName = "系统管理员",
        PasswordHash = "hash",
        IsActive = true,
        SecurityStamp = "stamp",
        UserCreatedAtUtc = Now.AddDays(-1),
        UserVersion = 1,
    };

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FixedIdGenerator : IIdGenerator
    {
        public Guid NewId() => AuditId;
    }
}
