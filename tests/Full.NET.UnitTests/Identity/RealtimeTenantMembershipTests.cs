using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>同一连接主体的后续核证读取当前成员，不保留退出前的准入，也不吞主动取消。</summary>
[TestClass]
public sealed class RealtimeTenantMembershipTests
{
    [TestMethod]
    public async Task Repeated_connection_checks_observe_membership_removal_and_reactivation()
    {
        var f = new Fixture();
        f.Queries.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1, 0, 1);
        Assert.IsTrue(await f.Authorizer.IsAuthorizedAsync(f.Principal, default));
        Assert.IsFalse(await f.Authorizer.IsAuthorizedAsync(f.Principal, default));
        Assert.IsTrue(await f.Authorizer.IsAuthorizedAsync(f.Principal, default));
        Assert.IsFalse(f.Tenant.IsAvailable);
    }

    [TestMethod]
    [DataRow("tenant")]
    [DataRow("unavailable")]
    [DataRow("expired")]
    public async Task Authority_failure_or_expiry_during_read_rejects_connection(string kind)
    {
        var f = new Fixture();
        if (kind == "tenant") f.Tenants.ResolveActiveByIdAsync(f.TenantId, Arg.Any<CancellationToken>()).Returns((TenantContext?)null);
        else f.Queries.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns<int>(_ =>
        {
            if (kind == "unavailable") throw new InvalidOperationException("membership unavailable");
            f.Clock.UtcNow.Returns(f.Now.AddHours(2)); return 1;
        });
        Assert.IsFalse(await f.Authorizer.IsAuthorizedAsync(f.Principal, default));
        Assert.IsFalse(f.Tenant.IsAvailable);
    }

    [TestMethod]
    public async Task Cancellation_during_membership_read_propagates_and_clears_context()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.Queries.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), cancellation.Token).Returns<int>(_ =>
        {
            cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token);
        });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Authorizer.IsAuthorizedAsync(f.Principal, cancellation.Token));
        Assert.IsFalse(f.Tenant.IsAvailable);
    }

    [TestMethod]
    public async Task Local_tenant_session_retains_its_authenticated_ownership_boundary()
    {
        var f = new Fixture(); var scope = $"tenant:{f.TenantId:N}";
        var identity = (ClaimsIdentity)f.Principal.Identity!;
        identity.RemoveClaim(identity.FindFirst(FullNetIdentityClaimTypes.ActorScope)!);
        identity.AddClaim(new(FullNetIdentityClaimTypes.ActorScope, scope));
        f.Session.ScopeKey = scope;
        Assert.IsTrue(await f.Authorizer.IsAuthorizedAsync(f.Principal, default));
        await f.Queries.DidNotReceive().QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly Guid TenantId = Guid.CreateVersion7();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly IClock Clock = Substitute.For<IClock>();
        internal readonly IActiveTenantContextResolver Tenants = Substitute.For<IActiveTenantContextResolver>();
        internal readonly CurrentTenantAccessor Tenant = new();
        internal readonly AccessSessionValidationRecord Session;
        internal readonly ClaimsPrincipal Principal;
        internal readonly RealtimeConnectionAuthorizer Authorizer;
        internal Fixture()
        {
            var user = Guid.CreateVersion7(); var session = Guid.CreateVersion7(); Clock.UtcNow.Returns(Now);
            Session = new() { UserId = user, ScopeKey = "host", ActiveTenantId = TenantId,
                SecurityStamp = "stamp", IsActive = true, ExpiresAtUtc = Now.AddHours(1) };
            Queries.QuerySingleOrDefaultAsync<AccessSessionValidationRecord>(IdentitySql.FindAccessSessionValidationById,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Session);
            Queries.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Tenants.ResolveActiveByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new TenantContext(TenantId, "a", "租户 A"));
            Principal = new(new ClaimsIdentity(new Claim[] { new("sub", user.ToString()), new("sid", session.ToString()),
                new(FullNetIdentityClaimTypes.SecurityStamp, "stamp"), new(FullNetIdentityClaimTypes.ActorScope, "host"),
                new(FullNetIdentityClaimTypes.Scope, $"tenant:{TenantId:N}"), new(FullNetIdentityClaimTypes.TenantId, TenantId.ToString()),
                new("exp", Now.AddMinutes(5).ToUnixTimeSeconds().ToString()) }, "test"));
            var oidc = new IdentityOidcAccessSessionValidator(Queries, Clock, Tenant, Options.Create(new IdentityOidcOptions()), Options.Create(new IdentityOptions()));
            Authorizer = new(new AccessSessionValidator(Queries, Clock, oidc, Options.Create(new IdentityOidcOptions())),
                Tenants, Tenant, Clock, new IdentityTenantResourceAccess(Queries, Tenant));
        }
    }
}
