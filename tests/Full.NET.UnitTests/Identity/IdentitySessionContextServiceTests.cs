using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentitySessionContextServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 6, 7, 8, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("01981b1a-e200-7000-8000-000000000001");
    private static readonly Guid SessionId =
        Guid.Parse("01981b1a-e200-7000-8000-000000000002");
    private static readonly Guid TenantId =
        Guid.Parse("01981b1a-e200-7000-8000-000000000003");

    [TestMethod]
    public async Task Change_updates_the_owned_active_session_and_issues_scoped_token()
    {
        var fixture = new Fixture();
        fixture.CommandExecutor.ExecuteAsync(
                IdentitySql.UpdateRefreshSessionContext,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await fixture.Service.ChangeAsync(
            CreatePrincipal(),
            new VerifiedTenantContext(
                TenantId,
                "acme",
                "Acme Corporation",
                "acme.localhost"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(TenantId, result.Value!.Context.TenantId);
        Assert.AreEqual($"tenant:{TenantId:N}", result.Value.Context.Scope);
        Assert.AreEqual(TenantId, fixture.TokenIssuer.ActiveTenantId);
    }

    [TestMethod]
    public async Task Active_version_race_returns_stable_conflict()
    {
        var fixture = new Fixture();
        fixture.QueryExecutor
            .QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                IdentitySql.FindRefreshSessionById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateRecord(1), CreateRecord(2));
        fixture.CommandExecutor.ExecuteAsync(
                IdentitySql.UpdateRefreshSessionContext,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await fixture.Service.ChangeAsync(
            CreatePrincipal(),
            new VerifiedTenantContext(
                TenantId,
                "acme",
                "Acme Corporation",
                "acme.localhost"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("identity.session_context_conflict", result.Error?.Code);
    }

    [TestMethod]
    public async Task Non_host_actor_is_rejected_before_session_access()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ChangeAsync(
            CreatePrincipal(actorScope: $"tenant:{TenantId:N}"),
            null);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("identity.invalid_actor_scope", result.Error?.Code);
        await fixture.QueryExecutor.DidNotReceiveWithAnyArgs()
            .QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                default!,
                default,
                default);
    }

    [TestMethod]
    public async Task Super_administrator_can_switch_context_without_permission_claims()
    {
        var fixture = new Fixture();
        fixture.CommandExecutor.ExecuteAsync(
                IdentitySql.UpdateRefreshSessionContext,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await fixture.Service.ChangeAsync(
            CreatePrincipal(isSuperAdministrator: true, includePermission: false),
            new VerifiedTenantContext(
                TenantId,
                "acme",
                "Acme Corporation",
                "acme.localhost"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(TenantId, fixture.TokenIssuer.ActiveTenantId);
    }

    private static ClaimsPrincipal CreatePrincipal(
        string actorScope = "host",
        bool isSuperAdministrator = false,
        bool includePermission = true)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, UserId.ToString("D")),
            new(IdentityClaimTypes.SessionId, SessionId.ToString("D")),
            new(IdentityClaimTypes.ActorScope, actorScope),
            new(IdentityClaimTypes.Scope, actorScope),
            new(IdentityClaimTypes.SecurityStamp, "stamp"),
            new(
                IdentityClaimTypes.SuperAdministrator,
                isSuperAdministrator.ToString().ToLowerInvariant()),
        };
        if (includePermission)
        {
            claims.Add(new Claim(
                IdentityClaimTypes.Permission,
                "tenancy.tenants.switch"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            "unit-test"));
    }

    private static RefreshSessionRecord CreateRecord(int version = 1) => new()
    {
        SessionId = SessionId,
        UserId = UserId,
        FamilyId = Guid.NewGuid(),
        ClientId = "fullnet-web",
        TokenHash = "hash",
        ExpiresAtUtc = Now.AddDays(1),
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

    private sealed class Fixture
    {
        public Fixture()
        {
            QueryExecutor = Substitute.For<IQueryExecutor>();
            QueryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                    IdentitySql.FindRefreshSessionById,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(CreateRecord());
            CommandExecutor = Substitute.For<ICommandExecutor>();
            CommandExecutor.ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            TokenIssuer = new StubTokenIssuer();
            Service = new IdentitySessionContextService(
                QueryExecutor,
                CommandExecutor,
                new StubPermissionSnapshotReader(),
                new PermissionClaimEvaluator(AuthorizationCatalog.Create(
                    [
                        new IdentityAuthorizationContributor(),
                        new Full.NET.Modules.Tenancy.TenancyAuthorizationContributor(),
                    ])),
                TokenIssuer,
                new FixedClock(),
                new FixedIdGenerator());
        }

        public IQueryExecutor QueryExecutor { get; }

        public ICommandExecutor CommandExecutor { get; }

        public StubTokenIssuer TokenIssuer { get; }

        public IdentitySessionContextService Service { get; }
    }

    private sealed class StubPermissionSnapshotReader : IPermissionSnapshotReader
    {
        public Task<PermissionSnapshot> ReadAsync(
            Guid userId,
            string scopeKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionSnapshot(
                ["identity.navigation.read", "tenancy.tenants.switch"],
                false));
    }

    private sealed class StubTokenIssuer : IAccessTokenIssuer
    {
        public Guid? ActiveTenantId { get; private set; }

        public IssuedAccessToken Issue(
            IdentityUser user,
            Guid sessionId,
            Guid? activeTenantId,
            IReadOnlyCollection<string> permissions,
            bool isSuperAdministrator)
        {
            ActiveTenantId = activeTenantId;
            return new IssuedAccessToken("context-token", Now.AddMinutes(10));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FixedIdGenerator : IIdGenerator
    {
        public Guid NewId() =>
            Guid.Parse("01981b1a-e200-7000-8000-000000000004");
    }
}
