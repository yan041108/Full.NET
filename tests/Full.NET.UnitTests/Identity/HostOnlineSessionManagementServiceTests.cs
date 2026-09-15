using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Realtime;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostOnlineSessionManagementServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 3, 4, 5, TimeSpan.Zero);
    private static readonly Guid ActorUserId =
        Guid.Parse("01981a75-f500-7000-8000-000000000010");
    private static readonly Guid TargetUserId =
        Guid.Parse("01981a75-f500-7000-8000-000000000011");
    private static readonly Guid SessionId =
        Guid.Parse("01981a75-f500-7000-8000-000000000012");
    private static readonly Guid FamilyId =
        Guid.Parse("01981a75-f500-7000-8000-000000000013");

    [TestMethod]
    public async Task Revoke_all_returns_not_found_for_unknown_user()
    {
        var fixture = new Fixture();
        fixture.QueryExecutor
            .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((IdentityUserRecord?)null);

        var result = await fixture.Service.RevokeAllByUserAsync(
            ActorUserId,
            TargetUserId,
            "127.0.0.1",
            "unit-test",
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.UserNotFound, result.Error?.Code);
    }

    [TestMethod]
    public async Task Revoke_all_revokes_only_target_user_sessions()
    {
        var fixture = new Fixture();
        fixture.ReturnTargetUser();
        fixture.QueryExecutor
            .QueryAsync<Guid>(
                OnlineSessionSql.ListActiveHostSessionIdsByUser,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns([SessionId]);

        var result = await fixture.Service.RevokeAllByUserAsync(
            ActorUserId,
            TargetUserId,
            "127.0.0.1",
            "unit-test",
            default);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, result.Value!.RevokedSessionCount);
        await fixture.CommandExecutor.Received(1).ExecuteAsync(
            IdentitySql.RevokeAllUserSessions,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await fixture.RealtimePublisher.Received(1).PublishToUserAsync(
            TargetUserId,
            Arg.Is<RealtimeMessage>(message =>
                message != null
                && message.Code == RealtimeMessageCodes.SessionRevoked),
            Arg.Any<CancellationToken>());
        await fixture.CommandExecutor.Received(1).ExecuteAsync(
            IdentitySql.InsertAuthAudit,
            Arg.Is<AuthAuditEvent>(audit =>
                audit != null
                && audit.EventType == "host_online_session.revoked_all"
                && audit.UserId == ActorUserId),
            Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            QueryExecutor = Substitute.For<IQueryExecutor>();
            CommandExecutor = Substitute.For<ICommandExecutor>();
            CommandExecutor.ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            Transaction = Substitute.For<ICommandTransaction>();
            Transaction
                .ExecuteAsync(
                    Arg.Any<Func<CancellationToken, Task<Result<RevokeAllHostUserSessionsResponse>>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var callback = callInfo.ArgAt<Func<CancellationToken, Task<Result<RevokeAllHostUserSessionsResponse>>>>(0);
                    return callback(CancellationToken.None);
                });
            RealtimePublisher = Substitute.For<IRealtimePublisher>();
            var clock = new FixedClock();
            var oidcSessionService = new IdentityOidcSessionService(
                QueryExecutor,
                CommandExecutor,
                clock);
            var oidcGrantRevocationService = new IdentityOidcGrantRevocationService(
                QueryExecutor,
                CommandExecutor,
                clock);
            Service = new HostOnlineSessionManagementService(
                QueryExecutor,
                CommandExecutor,
                Transaction,
                clock,
                new QueueIdGenerator(Guid.Parse("01981a75-f500-7000-8000-000000000099")),
                new IdentitySessionRealtimeDelivery(RealtimePublisher),
                oidcSessionService,
                oidcGrantRevocationService);
        }

        public IQueryExecutor QueryExecutor { get; }

        public ICommandExecutor CommandExecutor { get; }

        public ICommandTransaction Transaction { get; }

        public IRealtimePublisher RealtimePublisher { get; }

        public HostOnlineSessionManagementService Service { get; }

        public void ReturnTargetUser()
        {
            QueryExecutor
                .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindHostUserById,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(new IdentityUserRecord(
                    TargetUserId,
                    null,
                    "host",
                    "victim",
                    "VICTIM",
                    "Victim User",
                    "hash",
                    true,
                    0,
                    null,
                    "stamp",
                    Now.AddDays(-1),
                    Now.AddDays(-1),
                    1));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class QueueIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}
