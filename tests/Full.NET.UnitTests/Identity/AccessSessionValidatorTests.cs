using System.Security.Claims;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AccessSessionValidatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 18, 1, 2, 3, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("01981f2a-1200-7000-8000-000000000001");
    private static readonly Guid SessionId =
        Guid.Parse("01981f2a-1200-7000-8000-000000000002");
    private static readonly Guid TenantId =
        Guid.Parse("01981f2a-1200-7000-8000-000000000003");

    [TestMethod]
    public async Task Active_host_session_is_accepted()
    {
        var fixture = new Fixture(CreateRecord());

        var accepted = await fixture.Validator.IsValidAsync(CreatePrincipal());

        Assert.IsTrue(accepted);
    }

    [TestMethod]
    [DataRow("revoked")]
    [DataRow("consumed")]
    [DataRow("expired")]
    [DataRow("disabled")]
    [DataRow("locked")]
    public async Task Inactive_session_or_account_is_rejected(string state)
    {
        var record = CreateRecord();
        switch (state)
        {
            case "revoked":
                record.RevokedAtUtc = Now;
                break;
            case "consumed":
                record.ConsumedAtUtc = Now;
                break;
            case "expired":
                record.ExpiresAtUtc = Now;
                break;
            case "disabled":
                record.IsActive = false;
                break;
            case "locked":
                record.LockoutEndUtc = Now.AddMinutes(5);
                break;
        }

        var fixture = new Fixture(record);

        var accepted = await fixture.Validator.IsValidAsync(CreatePrincipal());

        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public async Task Rotated_security_stamp_rejects_the_old_access_token()
    {
        var fixture = new Fixture(CreateRecord(securityStamp: "new-stamp"));

        var accepted = await fixture.Validator.IsValidAsync(CreatePrincipal());

        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public async Task Tenant_context_switch_rejects_old_token_and_accepts_new_token()
    {
        var fixture = new Fixture(CreateRecord(activeTenantId: TenantId));

        var oldTokenAccepted = await fixture.Validator.IsValidAsync(CreatePrincipal());
        var newTokenAccepted = await fixture.Validator.IsValidAsync(
            CreatePrincipal(TenantId));

        Assert.IsFalse(oldTokenAccepted);
        Assert.IsTrue(newTokenAccepted);
    }

    [TestMethod]
    public async Task Actor_scope_mismatch_is_rejected()
    {
        var fixture = new Fixture(CreateRecord(scopeKey: "host"));

        var accepted = await fixture.Validator.IsValidAsync(
            CreatePrincipal(actorScope: $"tenant:{TenantId:N}"));

        Assert.IsFalse(accepted);
    }

    [TestMethod]
    public async Task Missing_or_malformed_identity_claims_are_rejected_without_querying_database()
    {
        var fixture = new Fixture(CreateRecord());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid")],
            "unit-test"));

        var accepted = await fixture.Validator.IsValidAsync(principal);

        Assert.IsFalse(accepted);
        await fixture.QueryExecutor.DidNotReceiveWithAnyArgs()
            .QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                default!,
                default,
                default);
    }

    private static ClaimsPrincipal CreatePrincipal(
        Guid? activeTenantId = null,
        string actorScope = "host")
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, UserId.ToString("D")),
            new(IdentityClaimTypes.SessionId, SessionId.ToString("D")),
            new(IdentityClaimTypes.ActorScope, actorScope),
            new(
                IdentityClaimTypes.Scope,
                activeTenantId.HasValue
                    ? $"tenant:{activeTenantId.Value:N}"
                    : "host"),
            new(IdentityClaimTypes.SecurityStamp, "stamp"),
        };
        if (activeTenantId.HasValue)
        {
            claims.Add(new Claim(
                IdentityClaimTypes.TenantId,
                activeTenantId.Value.ToString("D")));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "unit-test"));
    }

    private static RefreshSessionRecord CreateRecord(
        string securityStamp = "stamp",
        string scopeKey = "host",
        Guid? activeTenantId = null) => new()
    {
        SessionId = SessionId,
        UserId = UserId,
        FamilyId = Guid.NewGuid(),
        ClientId = "fullnet-web",
        TokenHash = "hash",
        ExpiresAtUtc = Now.AddHours(1),
        ActiveTenantId = activeTenantId,
        CreatedAtUtc = Now.AddMinutes(-10),
        SessionVersion = 1,
        ScopeKey = scopeKey,
        Username = "admin",
        NormalizedUsername = "ADMIN",
        DisplayName = "系统管理员",
        PasswordHash = "hash",
        IsActive = true,
        SecurityStamp = securityStamp,
        UserCreatedAtUtc = Now.AddDays(-1),
        UserVersion = 1,
    };

    private sealed class Fixture
    {
        public Fixture(RefreshSessionRecord record)
        {
            QueryExecutor = Substitute.For<IQueryExecutor>();
            QueryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                    IdentitySql.FindRefreshSessionById,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(record);
            Validator = new AccessSessionValidator(
                QueryExecutor,
                new FixedClock());
        }

        public IQueryExecutor QueryExecutor { get; }

        public AccessSessionValidator Validator { get; }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
