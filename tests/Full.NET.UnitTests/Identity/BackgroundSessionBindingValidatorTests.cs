using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>单元测试（Identity）：Background Session Binding Validator。</summary>
[TestClass]
public sealed class BackgroundSessionBindingValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("01981f2a-1200-7000-8000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("01981f2a-1200-7000-8000-000000000002");
    private static readonly Guid TenantId = Guid.Parse("01981f2a-1200-7000-8000-000000000003");

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_binding_rechecks_activity_after_disable_and_reenable(bool oidc)
    {
        var (validator, binding, tenants) = CreateAuthorityFixture(oidc, TenantId);
        Assert.IsTrue(await validator.IsValidAsync(binding));
        tenants.IsActiveTenantAsync(TenantId, Arg.Any<CancellationToken>()).Returns(false);
        Assert.IsFalse(await validator.IsValidAsync(binding));
        tenants.IsActiveTenantAsync(TenantId, Arg.Any<CancellationToken>()).Returns(true);
        Assert.IsTrue(await validator.IsValidAsync(binding));
        await tenants.Received(3).IsActiveTenantAsync(TenantId, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Tenant_authority_failure_rejects_previous_valid_binding(bool oidc)
    {
        var (validator, binding, tenants) = CreateAuthorityFixture(oidc, TenantId);
        Assert.IsTrue(await validator.IsValidAsync(binding));
        tenants.IsActiveTenantAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<bool>(new InvalidOperationException("authority unavailable")));
        Assert.IsFalse(await validator.IsValidAsync(binding));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Tenant_authority_cancellation_remains_cancellation(bool oidc, bool cancelBeforeRead)
    {
        var (validator, binding, tenants) = CreateAuthorityFixture(oidc, TenantId);
        using var cancellation = new CancellationTokenSource();
        if (cancelBeforeRead) cancellation.Cancel();
        else tenants.IsActiveTenantAsync(TenantId, cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromException<bool>(new OperationCanceledException(cancellation.Token));
        });
        var error = await Assert.ThrowsAsync<OperationCanceledException>(
            () => validator.IsValidAsync(binding, cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        if (cancelBeforeRead) await tenants.DidNotReceiveWithAnyArgs().IsActiveTenantAsync(default);
        else await tenants.Received(1).IsActiveTenantAsync(TenantId, cancellation.Token);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Host_binding_does_not_query_tenant_activity(bool oidc)
    {
        var (validator, binding, tenants) = CreateAuthorityFixture(oidc, null);
        Assert.IsTrue(await validator.IsValidAsync(binding));
        await tenants.DidNotReceiveWithAnyArgs().IsActiveTenantAsync(default);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Invalid_session_is_rejected_before_tenant_authority(bool oidc)
    {
        var (validator, binding, tenants) = CreateAuthorityFixture(oidc, TenantId);
        Assert.IsFalse(await validator.IsValidAsync(binding with { SecurityStamp = "wrong-stamp" }));
        await tenants.DidNotReceiveWithAnyArgs().IsActiveTenantAsync(default);
    }

    private static (BackgroundSessionBindingValidator Validator, SessionBindingSnapshot Binding,
        IIdentityActiveTenantDirectory Tenants) CreateAuthorityFixture(bool oidc, Guid? tenantId)
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<RefreshSessionRecord>(IdentitySql.FindRefreshSessionById,
            Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(CreateRecord(activeTenantId: tenantId));
        query.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionValidationRecord>(
            IdentityOidcSessionSql.FindApplicationSessionValidationById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(CreateOidcRecord(tenantId));
        var tenants = Substitute.For<IIdentityActiveTenantDirectory>();
        tenants.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return (CreateValidator(query, tenants), oidc ? CreateOidcBinding(tenantId) : CreateBinding(tenantId), tenants);
    }

    private static BackgroundSessionBindingValidator CreateValidator(IQueryExecutor query,
        IIdentityActiveTenantDirectory? tenants = null)
    {
        var directory = tenants ?? Substitute.For<IIdentityActiveTenantDirectory>();
        using var services = new ServiceCollection().AddSingleton(query).AddSingleton<IClock>(new FixedClock(Now))
            .AddSingleton(Options.Create(new IdentityOptions())).AddSingleton(directory).BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<BackgroundSessionBindingValidator>(services);
    }

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

    [TestMethod]
    public async Task Oidc_application_binding_matches_active_session()
    {
        var fixture = new OidcFixture(CreateOidcRecord());

        var valid = await fixture.Validator.IsValidAsync(CreateOidcBinding());

        Assert.IsTrue(valid);
    }

    [TestMethod]
    public async Task Oidc_application_binding_rejects_stale_effective_scope_after_tenant_switch()
    {
        var fixture = new OidcFixture(CreateOidcRecord(activeTenantId: TenantId));

        var valid = await fixture.Validator.IsValidAsync(CreateOidcBinding(tenantId: null, effectiveScope: "host"));

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

    private static SessionBindingSnapshot CreateOidcBinding(
        Guid? tenantId = null,
        string? effectiveScope = null) => new(
        UserId,
        tenantId,
        SessionId,
        "stamp",
        "host",
        effectiveScope ?? (tenantId is { } id ? $"tenant:{id:N}" : "host"),
        SessionBindingKinds.OidcApplication);

    private static IdentityOidcApplicationSessionValidationRecord CreateOidcRecord(
        Guid? activeTenantId = null) => new()
    {
        ApplicationSessionId = SessionId,
        CenterSessionId = Guid.Parse("01981f2a-1200-7000-8000-000000000004"),
        UserId = UserId,
        ClientId = "fixture-oidc-a-public",
        ActorScope = "host",
        EffectiveScope = activeTenantId is { } id ? $"tenant:{id:N}" : "host",
        ActiveTenantId = activeTenantId,
        ApplicationExpiresAtUtc = Now.AddHours(1),
        CenterSecurityStamp = "stamp",
        CenterExpiresAtUtc = Now.AddHours(1),
        IsActive = true,
        UserSecurityStamp = "stamp",
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
            Validator = CreateValidator(queryExecutor);
        }

        public BackgroundSessionBindingValidator Validator { get; }
    }

    private sealed class OidcFixture
    {
        public OidcFixture(IdentityOidcApplicationSessionValidationRecord record)
        {
            var queryExecutor = Substitute.For<IQueryExecutor>();
            queryExecutor.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionValidationRecord>(
                    IdentityOidcSessionSql.FindApplicationSessionValidationById,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(record);
            Validator = CreateValidator(queryExecutor);
        }

        public BackgroundSessionBindingValidator Validator { get; }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
