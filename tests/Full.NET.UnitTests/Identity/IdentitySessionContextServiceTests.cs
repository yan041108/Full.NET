using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Oidc;
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

    /// <summary>验证上下文更新携带令牌中的原租户，以拒绝已被另一请求替换的旧令牌。</summary>
    [TestMethod]
    public async Task Change_compares_the_active_tenant_from_the_access_token()
    {
        var fixture = new Fixture();
        RefreshSessionContextUpdate? captured = null;
        fixture.CommandExecutor.ExecuteAsync(
                IdentitySql.UpdateRefreshSessionContext,
                Arg.Do<object?>(value => captured = (RefreshSessionContextUpdate)value!),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var principal = CreatePrincipal();
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(
            IdentityClaimTypes.TenantId,
            TenantId.ToString("D")));

        var result = await fixture.Service.ChangeAsync(principal, null);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(captured);
        Assert.IsTrue(captured.ExpectedActiveTenantId == TenantId);
        StringAssert.Contains(
            IdentitySql.UpdateRefreshSessionContext.Text,
            "ActiveTenantId = @ExpectedActiveTenantId");
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
    public async Task ChangeOidc_updates_application_session_and_issues_oidc_access_token()
    {
        var fixture = new OidcFixture();
        fixture.CommandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.UpdateApplicationSessionContext,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await fixture.Service.ChangeAsync(
            CreateOidcPrincipal(),
            new VerifiedTenantContext(
                TenantId,
                "acme",
                "Acme Corporation",
                "acme.localhost"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Bearer", result.Value!.TokenType);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Value.AccessToken));
        Assert.AreEqual(TenantId, result.Value.Context.TenantId);
        fixture.TokenIssuer.DidNotReceive().Issue(
            Arg.Any<IdentityUser>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<bool>());
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

    private static ClaimsPrincipal CreateOidcPrincipal()
    {
        var applicationSessionId = Guid.Parse("01981b1a-e200-7000-8000-000000000005");
        var centerSessionId = Guid.Parse("01981b1a-e200-7000-8000-000000000006");
        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, UserId.ToString("D")),
                new Claim(JwtRegisteredClaimNames.Iss, "https://localhost/identity"),
                new Claim(FullNetIdentityClaimTypes.ApplicationSessionId, applicationSessionId.ToString("D")),
                new Claim(FullNetIdentityClaimTypes.CenterSessionId, centerSessionId.ToString("D")),
                new Claim(FullNetIdentityClaimTypes.OidcClientId, "fixture-oidc-a-public"),
                new Claim(FullNetIdentityClaimTypes.TokenUse, "access"),
                new Claim(IdentityClaimTypes.ActorScope, "host"),
                new Claim(IdentityClaimTypes.Scope, "host"),
                new Claim(IdentityClaimTypes.SecurityStamp, "stamp"),
                new Claim(IdentityClaimTypes.Permission, "tenancy.tenants.switch"),
                new Claim("scope", "openid profile"),
            ],
            "unit-test"));
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

    private sealed class OidcFixture
    {
        public OidcFixture()
        {
            QueryExecutor = Substitute.For<IQueryExecutor>();
            QueryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionValidationRecord>(
                    IdentityOidcSessionSql.FindApplicationSessionValidationById,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(CreateOidcValidationRecord());
            CommandExecutor = Substitute.For<ICommandExecutor>();
            CommandExecutor.ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            TokenIssuer = Substitute.For<IAccessTokenIssuer>();
            var (oidcIssuer, clientResolver) = Fixture.CreateOidcDependencies();
            var (refreshIssuer, grantRevocation) = Fixture.CreateOidcContextSwitchDependencies(
                QueryExecutor,
                CommandExecutor);
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
                grantRevocation,
                new CurrentTenantAccessor(),
                new FixedClock(),
                new FixedIdGenerator(),
                Options.Create(new IdentityOidcOptions
                {
                    Enable = true,
                    Issuer = "https://localhost/identity",
                    AllowDevelopmentEphemeralSigningKey = true,
                }),
                Options.Create(new IdentityOptions()),
                oidcIssuer,
                refreshIssuer,
                clientResolver);
        }

        public IQueryExecutor QueryExecutor { get; }
        public ICommandExecutor CommandExecutor { get; }
        public IAccessTokenIssuer TokenIssuer { get; }
        public IdentitySessionContextService Service { get; }

        private static IdentityOidcApplicationSessionValidationRecord CreateOidcValidationRecord() => new()
        {
            ApplicationSessionId = Guid.Parse("01981b1a-e200-7000-8000-000000000005"),
            UserId = UserId,
            ActorScope = "host",
            EffectiveScope = "host",
            IsActive = true,
            ApplicationExpiresAtUtc = Now.AddMinutes(10),
            CenterExpiresAtUtc = Now.AddDays(1),
            UserSecurityStamp = "stamp",
            CenterSecurityStamp = "stamp",
            Version = 1,
        };
    }

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
            var (oidcIssuer, clientResolver) = CreateOidcDependencies();
            var (refreshIssuer, grantRevocation) = CreateOidcContextSwitchDependencies(
                QueryExecutor,
                CommandExecutor);
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
                grantRevocation,
                new CurrentTenantAccessor(),
                new FixedClock(),
                new FixedIdGenerator(),
                Options.Create(new IdentityOidcOptions
                {
                    Enable = true,
                    Issuer = "https://localhost/identity",
                    AllowDevelopmentEphemeralSigningKey = true,
                }),
                Options.Create(new IdentityOptions()),
                oidcIssuer,
                refreshIssuer,
                clientResolver);
        }

        internal static (
            IdentityOidcContextRefreshTokenIssuer RefreshIssuer,
            IdentityOidcGrantRevocationService GrantRevocation) CreateOidcContextSwitchDependencies(
            IQueryExecutor queryExecutor,
            ICommandExecutor commandExecutor)
        {
            var clock = new FixedClock();
            return (
                new IdentityOidcContextRefreshTokenIssuer(
                    Substitute.For<IOpenIddictServerDispatcher>(),
                    Options.Create(new OpenIddictServerOptions()),
                    Substitute.For<ILogger<IdentityOidcContextRefreshTokenIssuer>>(),
                    clock),
                new IdentityOidcGrantRevocationService(queryExecutor, commandExecutor, clock));
        }

        internal static (IdentityOidcContextAccessTokenIssuer Issuer, IdentityOidcClientConfigResolver ClientResolver)
            CreateOidcDependencies()
        {
            var oidcOptions = Options.Create(new IdentityOidcOptions
            {
                Enable = true,
                Issuer = "https://localhost/identity",
                AllowDevelopmentEphemeralSigningKey = true,
            });
            var identityOptions = Options.Create(new IdentityOptions { Audience = "Full.NET.Api" });
            var keyRing = new IdentityOidcSigningKeyRing(
                oidcOptions,
                Substitute.For<ILogger<IdentityOidcSigningKeyRing>>());
            var issuer = new IdentityOidcContextAccessTokenIssuer(
                new IdentityOidcPrincipalFactory(),
                keyRing,
                oidcOptions,
                identityOptions,
                new FixedClock(),
                new FixedIdGenerator());
            var clientResolver = new IdentityOidcClientConfigResolver(
                oidcOptions,
                Substitute.For<IOpenIddictApplicationManager>());
            return (issuer, clientResolver);
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
