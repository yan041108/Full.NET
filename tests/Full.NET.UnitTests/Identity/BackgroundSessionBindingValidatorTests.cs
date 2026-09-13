using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class BackgroundSessionBindingValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("01981f2a-1200-7000-8000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("01981f2a-1200-7000-8000-000000000002");
    private static readonly Guid TenantId = Guid.Parse("01981f2a-1200-7000-8000-000000000003");

    [TestMethod]
    public async Task Frozen_binding_matches_active_session()
    {
        var fixture = new Fixture(CreateRecord());

        var valid = await fixture.Validator.IsValidAsync(CreateBinding());

        Assert.IsTrue(valid);
    }

    [TestMethod]
    public async Task Revoked_session_rejects_frozen_binding()
    {
        var record = CreateRecord();
        record.RevokedAtUtc = Now;
        var fixture = new Fixture(record);

        var valid = await fixture.Validator.IsValidAsync(CreateBinding());

        Assert.IsFalse(valid);
    }

    [TestMethod]
    public async Task Rotated_security_stamp_rejects_frozen_binding()
    {
        var fixture = new Fixture(CreateRecord(securityStamp: "new-stamp"));

        var valid = await fixture.Validator.IsValidAsync(CreateBinding());

        Assert.IsFalse(valid);
    }

    [TestMethod]
    public async Task Tenant_switch_rejects_old_effective_scope()
    {
        var fixture = new Fixture(CreateRecord(activeTenantId: TenantId));

        var valid = await fixture.Validator.IsValidAsync(CreateBinding(tenantId: null));

        Assert.IsFalse(valid);
    }

    private static SessionBindingSnapshot CreateBinding(Guid? tenantId = null) => new(
        UserId,
        tenantId,
        SessionId,
        "stamp",
        "host",
        tenantId is { } id ? $"tenant:{id:N}" : "host");

    private static RefreshSessionRecord CreateRecord(
        string securityStamp = "stamp",
        Guid? activeTenantId = null,
        string scopeKey = "host") => new()
    {
        SessionId = SessionId,
        UserId = UserId,
        ScopeKey = scopeKey,
        ActiveTenantId = activeTenantId,
        SecurityStamp = securityStamp,
        IsActive = true,
        ExpiresAtUtc = Now.AddHours(1),
    };

    private sealed class Fixture
    {
        public Fixture(RefreshSessionRecord record)
        {
            var queryExecutor = Substitute.For<IQueryExecutor>();
            queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
                    IdentitySql.FindRefreshSessionById,
                    Arg.Any<IReadOnlyDictionary<string, object?>>(),
                    Arg.Any<CancellationToken>())
                .Returns(record);
            Validator = new BackgroundSessionBindingValidator(queryExecutor, new FixedClock(Now));
        }

        public BackgroundSessionBindingValidator Validator { get; }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
