using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>成员准入不得被旧权限声明缓存；恢复权限也不能夹带业务权限越过成员撤销。</summary>
[TestClass]
public sealed class IdentityTenantResourceAccessTests
{
    [TestMethod]
    public async Task Every_authorization_observes_current_membership_and_binds_one_trusted_tenant()
    {
        var f = new Fixture();
        f.Queries.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1, 0);
        Assert.IsTrue(await f.Access.IsAllowedAsync(f.Principal, [IdentityTenantMembershipPermissions.Read]));
        Assert.IsFalse(await f.Access.IsAllowedAsync(f.Principal, [IdentityTenantMembershipPermissions.Read]));
        await f.Queries.Received(2).QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(),
            Arg.Is<object?>(value => MatchesAnchor(value, f.User, f.TenantId)), Arg.Any<CancellationToken>());
        Assert.IsFalse(f.Queries.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "QueryAsync"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Recovery_permissions_do_not_allow_an_added_business_permission(bool mixed)
    {
        var f = new Fixture();
        List<string> permissions = [IdentityTenantMembershipPermissions.LeaveSelf, "tenancy.tenants.switch"];
        if (mixed) permissions.Add(IdentityTenantMembershipPermissions.Read);
        Assert.AreEqual(!mixed, await f.Access.IsAllowedAsync(f.Principal, permissions));
        Assert.AreEqual(mixed ? 1 : 0, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("local")]
    [DataRow("key")]
    [DataRow("signature")]
    public async Task Tenant_local_credentials_keep_their_existing_authentication_boundary(string kind)
    {
        var f = new Fixture();
        var identity = (ClaimsIdentity)f.Principal.Identity!;
        identity.RemoveClaim(identity.FindFirst(IdentityClaimTypes.ActorScope)!);
        identity.AddClaim(new(IdentityClaimTypes.ActorScope, kind == "local" ? $"tenant:{f.TenantId:N}" : "tenant"));
        var principal = kind == "local" ? f.Principal : new ClaimsPrincipal(new ClaimsIdentity(identity.Claims,
            kind == "key" ? "FullNET.ApiKey" : "FullNET.Signature"));
        Assert.IsTrue(await f.Access.IsAllowedAsync(principal, [IdentityTenantMembershipPermissions.Read]));
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("tenant")]
    [DataRow("scope")]
    [DataRow("actor")]
    [DataRow("empty")]
    public async Task Malformed_or_mismatched_authority_fails_before_database_read(string kind)
    {
        var f = new Fixture();
        var identity = (ClaimsIdentity)f.Principal.Identity!;
        var type = kind == "tenant" ? IdentityClaimTypes.TenantId : kind == "scope" ? IdentityClaimTypes.Scope : IdentityClaimTypes.ActorScope;
        identity.RemoveClaim(identity.FindFirst(type)!);
        identity.AddClaim(new(type, "other"));
        Assert.IsFalse(await f.Access.IsAllowedAsync(f.Principal, kind == "empty" ? [] : [IdentityTenantMembershipPermissions.LeaveSelf]));
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Cancelled_authority_read_is_not_success_or_cached_denial()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        f.Queries.QuerySingleOrDefaultAsync<int>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), cancellation.Token)
            .Returns<int>(_ => throw new OperationCanceledException(cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Access.IsAllowedAsync(f.Principal,
            [IdentityTenantMembershipPermissions.Read], cancellation.Token));
    }

    private static bool MatchesAnchor(object? value, Guid user, Guid tenant) =>
        value is IReadOnlyDictionary<string, object?> map && Equals(map["UserId"], user) && Equals(map["TenantId"], tenant);

    private sealed class Fixture
    {
        internal readonly Guid User = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ClaimsPrincipal Principal;
        internal readonly IdentityTenantResourceAccess Access;
        internal Fixture()
        {
            var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new TenantContext(TenantId, "a", "租户 A"));
            Principal = new(new ClaimsIdentity(new Claim[] { new("sub", User.ToString()),
                new(IdentityClaimTypes.ActorScope, "host"), new(IdentityClaimTypes.Scope, $"tenant:{TenantId:N}"),
                new(IdentityClaimTypes.TenantId, TenantId.ToString()) }, "test"));
            Access = new(Queries, tenant);
        }
    }
}
