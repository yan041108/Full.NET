using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.RefreshSession;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using NSubstitute;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class RefreshSessionHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 5, 6, 7, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("01981ae3-f3c0-7000-8000-000000000001");
    private static readonly Guid SessionId =
        Guid.Parse("01981ae3-f3c0-7000-8000-000000000002");
    private static readonly Guid ReplacementId =
        Guid.Parse("01981ae3-f3c0-7000-8000-000000000003");
    private static readonly Guid FamilyId =
        Guid.Parse("01981ae3-f3c0-7000-8000-000000000004");
    private static readonly Guid TenantId =
        Guid.Parse("01981ae3-f3c0-7000-8000-000000000005");
    private static readonly Guid AuditId =
        Guid.Parse("01981ae3-f3c0-7000-8000-000000000006");

    [TestMethod]
    public async Task Refresh_copies_active_tenant_and_reloads_permissions()
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
        var tokenIssuer = new StubAccessTokenIssuer();
        var handler = new Handler(
            query,
            command,
            new FixedClock(),
            new QueueIdGenerator(ReplacementId, AuditId),
            new StubPermissionSnapshotReader(["platform.dashboard.read"]),
            tokenIssuer,
            new QueueTokenGenerator("replacement-token", "csrf-token"),
            Options.Create(new IdentityOptions
            {
                AllowDevelopmentEphemeralSigningKey = true,
            }));

        var result = await handler.HandleAsync(
            new Command(
                "presented-token",
                new ClientRequestContext("127.0.0.1", "unit-test")),
            default);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(TenantId, tokenIssuer.ActiveTenantId);
        CollectionAssert.AreEqual(
            new[] { "platform.dashboard.read" },
            tokenIssuer.Permissions.ToArray());
        await command.Received(1).ExecuteAsync(
            IdentitySql.InsertRefreshSession,
            Arg.Is<RefreshSession>(session =>
                session != null && session.ActiveTenantId == TenantId),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Refresh_retries_once_when_context_update_wins_the_version_race()
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionByHash,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateRecord(version: 1), CreateRecord(version: 2));
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                IdentitySql.ConsumeRefreshSession,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0, 1);
        command.ExecuteAsync(
                Arg.Is<SqlStatement>(statement =>
                    statement != IdentitySql.ConsumeRefreshSession),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var handler = new Handler(
            query,
            command,
            new FixedClock(),
            new QueueIdGenerator(ReplacementId, AuditId),
            new StubPermissionSnapshotReader(["platform.dashboard.read"]),
            new StubAccessTokenIssuer(),
            new QueueTokenGenerator("replacement-token", "csrf-token"),
            Options.Create(new IdentityOptions
            {
                AllowDevelopmentEphemeralSigningKey = true,
            }));

        var result = await handler.HandleAsync(
            new Command(
                "presented-token",
                new ClientRequestContext("127.0.0.1", "unit-test")),
            default);

        Assert.IsTrue(result.IsSuccess);
        await command.Received(2).ExecuteAsync(
            IdentitySql.ConsumeRefreshSession,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private static RefreshSessionRecord CreateRecord(int version = 1) => new()
    {
        SessionId = SessionId,
        UserId = UserId,
        FamilyId = FamilyId,
        ClientId = "fullnet-web",
        TokenHash = TokenHash.Compute("presented-token"),
        ExpiresAtUtc = Now.AddDays(1),
        ActiveTenantId = TenantId,
        CreatedAtUtc = Now.AddHours(-1),
        SessionVersion = version,
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

    private sealed class QueueIdGenerator(params Guid[] ids) : IIdGenerator
    {
        private readonly Queue<Guid> _ids = new(ids);

        public Guid NewId() => _ids.Dequeue();
    }

    private sealed class QueueTokenGenerator(params string[] tokens) : IRandomTokenGenerator
    {
        private readonly Queue<string> _tokens = new(tokens);

        public string Generate(int byteCount) => _tokens.Dequeue();
    }

    private sealed class StubPermissionSnapshotReader(
        IReadOnlyList<string> permissions) : IPermissionSnapshotReader
    {
        public Task<IReadOnlyList<string>> ReadAsync(
            Guid userId,
            string scopeKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(permissions);
    }

    private sealed class StubAccessTokenIssuer : IAccessTokenIssuer
    {
        public Guid? ActiveTenantId { get; private set; }

        public IReadOnlyCollection<string> Permissions { get; private set; } = [];

        public IssuedAccessToken Issue(
            IdentityUser user,
            Guid sessionId,
            Guid? activeTenantId,
            IReadOnlyCollection<string> permissions)
        {
            ActiveTenantId = activeTenantId;
            Permissions = permissions;
            return new IssuedAccessToken("access-token", Now.AddMinutes(10));
        }
    }
}
