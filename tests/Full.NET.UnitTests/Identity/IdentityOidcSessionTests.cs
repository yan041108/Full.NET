using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityOidcSessionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("01995f10-1200-7000-8000-000000000001");
    private static readonly Guid CenterSessionId =
        Guid.Parse("01995f10-1200-7000-8000-000000000002");
    private static readonly Guid ApplicationSessionId =
        Guid.Parse("01995f10-1200-7000-8000-000000000003");
    private static readonly Guid ApplicationId =
        Guid.Parse("01995f10-1200-7000-8000-000000000004");

    [TestMethod]
    public async Task Create_center_and_application_sessions_persist_rows()
    {
        var fixture = new Fixture();
        fixture.CommandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.InsertCenterSession,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        fixture.CommandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.InsertApplicationSession,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var center = await fixture.Service.CreateCenterSessionAsync(
            UserId,
            "stamp",
            Now.AddHours(8),
            default);
        var application = await fixture.Service.CreateApplicationSessionAsync(
            center.Id,
            ApplicationId,
            "integration-client",
            UserId,
            "host",
            "host",
            null,
            Now.AddHours(1),
            default);

        Assert.AreNotEqual(Guid.Empty, center.Id);
        Assert.AreNotEqual(Guid.Empty, application.Id);
        Assert.AreEqual(center.Id, application.CenterSessionId);
    }

    [TestMethod]
    public async Task Revoke_active_application_sessions_by_user_and_client_revokes_each_match()
    {
        var fixture = new Fixture();
        fixture.QueryExecutor
            .QueryAsync<Guid>(
                IdentityOidcSessionSql.ListActiveHostOidcApplicationSessionIdsByUserAndClient,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns([ApplicationSessionId]);
        fixture.QueryExecutor
            .QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionRow>(
                IdentityOidcSessionSql.FindApplicationSessionById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new IdentityOidcApplicationSessionRow(
                ApplicationSessionId,
                CenterSessionId,
                ApplicationId,
                "integration-client",
                UserId,
                "host",
                "host",
                null,
                Now,
                Now.AddHours(1),
                null,
                1,
                Now));
        fixture.CommandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.RevokeApplicationSession,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var revokedSessionIds = await fixture.Service.RevokeActiveApplicationSessionsByUserAndClientAsync(
            UserId,
            "integration-client",
            default);

        CollectionAssert.AreEqual(new[] { ApplicationSessionId }, revokedSessionIds.ToArray());
    }

    [TestMethod]
    public async Task Concurrent_center_revoke_is_idempotent()
    {
        var fixture = new Fixture();
        fixture.QueryExecutor
            .QuerySingleOrDefaultAsync<IdentityOidcCenterSessionRow>(
                IdentityOidcSessionSql.FindCenterSessionById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new IdentityOidcCenterSessionRow(
                CenterSessionId,
                UserId,
                "stamp",
                Now,
                Now.AddHours(1),
                null,
                1,
                Now));
        fixture.CommandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.RevokeApplicationSessionsByCenterSession,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        fixture.CommandExecutor.ExecuteAsync(
                IdentityOidcSessionSql.RevokeCenterSession,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1, 0);

        var first = await fixture.Service.RevokeCenterSessionAsync(CenterSessionId, default);
        var second = await fixture.Service.RevokeCenterSessionAsync(CenterSessionId, default);

        Assert.IsTrue(first);
        Assert.IsFalse(second);
    }

    [TestMethod]
    public async Task Security_stamp_change_invalidates_validator()
    {
        var fixture = new ValidatorFixture(CreateValidationRecord(userSecurityStamp: "rotated"));
        var principal = ValidatorFixture.CreateOidcPrincipal(
            ApplicationSessionId,
            UserId,
            "host",
            "host",
            issuer: "https://localhost/identity",
            audience: "Full.NET.Api");

        var accepted = await fixture.Validator.IsValidAsync(principal, default);

        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public async Task Missing_application_session_returns_false_fail_closed()
    {
        var fixture = new ValidatorFixture(null);
        var principal = ValidatorFixture.CreateOidcPrincipal(
            ApplicationSessionId,
            UserId,
            "host",
            "host",
            issuer: "https://localhost/identity",
            audience: "Full.NET.Api");

        var accepted = await fixture.Validator.IsValidAsync(principal, default);

        Assert.IsFalse(accepted);
    }

    private static IdentityOidcApplicationSessionValidationRecord CreateValidationRecord(
        string userSecurityStamp = "stamp") => new()
    {
        ApplicationSessionId = ApplicationSessionId,
        CenterSessionId = CenterSessionId,
        UserId = UserId,
        ClientId = "integration-client",
        ActorScope = "host",
        EffectiveScope = "host",
        ApplicationExpiresAtUtc = Now.AddHours(1),
        CenterSecurityStamp = "stamp",
        CenterExpiresAtUtc = Now.AddHours(8),
        UserSecurityStamp = userSecurityStamp,
        IsActive = true,
    };

    private sealed class Fixture
    {
        public Fixture()
        {
            QueryExecutor = Substitute.For<IQueryExecutor>();
            CommandExecutor = Substitute.For<ICommandExecutor>();
            Service = new IdentityOidcSessionService(
                QueryExecutor,
                CommandExecutor,
                new FixedClock());
        }

        public IQueryExecutor QueryExecutor { get; }
        public ICommandExecutor CommandExecutor { get; }
        public IdentityOidcSessionService Service { get; }
    }

    private sealed class ValidatorFixture
    {
        public ValidatorFixture(IdentityOidcApplicationSessionValidationRecord? record)
        {
            QueryExecutor = Substitute.For<IQueryExecutor>();
            QueryExecutor
                .QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionValidationRecord>(
                    IdentityOidcSessionSql.FindApplicationSessionValidationById,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(record);
            Validator = new IdentityOidcAccessSessionValidator(
                QueryExecutor,
                new FixedClock(),
                new Full.NET.Abstractions.Tenancy.CurrentTenantAccessor(),
                Microsoft.Extensions.Options.Options.Create(new Modules.Identity.Configuration.IdentityOidcOptions
                {
                    Enable = true,
                    Issuer = "https://localhost/identity",
                }),
                Microsoft.Extensions.Options.Options.Create(new Modules.Identity.Configuration.IdentityOptions
                {
                    Audience = "Full.NET.Api",
                }));
        }

        public IQueryExecutor QueryExecutor { get; }
        public IdentityOidcAccessSessionValidator Validator { get; }

        public static System.Security.Claims.ClaimsPrincipal CreateOidcPrincipal(
            Guid applicationSessionId,
            Guid userId,
            string actorScope,
            string effectiveScope,
            string issuer,
            string audience,
            Guid? tenantId = null)
        {
            var claims = new List<System.Security.Claims.Claim>
            {
                new("sub", userId.ToString("D")),
                new("iss", issuer),
                new("aud", audience),
                new(Full.NET.Modules.Identity.Contracts.FullNetIdentityClaimTypes.TokenUse, IdentityOidcPrincipalFactory.TokenUseAccess),
                new(Full.NET.Modules.Identity.Contracts.FullNetIdentityClaimTypes.ApplicationSessionId, applicationSessionId.ToString("D")),
                new(Full.NET.Modules.Identity.Security.IdentityClaimTypes.ActorScope, actorScope),
                new(Full.NET.Modules.Identity.Security.IdentityClaimTypes.Scope, effectiveScope),
            };
            if (tenantId.HasValue)
            {
                claims.Add(new System.Security.Claims.Claim(
                    Full.NET.Modules.Identity.Security.IdentityClaimTypes.TenantId,
                    tenantId.Value.ToString("D")));
            }

            return new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(claims, "unit-test"));
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
