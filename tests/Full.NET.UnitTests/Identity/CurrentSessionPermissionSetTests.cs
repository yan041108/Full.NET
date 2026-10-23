using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>集合授权只合并同一边界内的读取，后续边界仍观察撤销。</summary>
[TestClass]
public sealed class CurrentSessionPermissionSetTests
{
    [TestMethod]
    public async Task Four_permissions_share_one_authority_read_and_later_revocation_is_observed()
    {
        var f = new Fixture();
        Assert.IsNotNull(await f.Authorization.AuthorizeAllAsync(["a", "b", "c", "d"]));
        await f.Queries.Received(1).QuerySingleOrDefaultAsync<AccessSessionValidationRecord>(
            IdentitySql.FindAccessSessionValidationById, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Permissions.Received(1).ReadAsync(f.User, "host", null, Arg.Any<CancellationToken>());
        f.Permissions.ReadAsync(f.User, "host", null, Arg.Any<CancellationToken>()).Returns(new PermissionSnapshot(["a", "b", "c"], false));
        Assert.IsNull(await f.Authorization.AuthorizeAllAsync(["a", "b", "c", "d"]));
        await f.Permissions.Received(2).ReadAsync(f.User, "host", null, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("a")]
    [DataRow("b")]
    [DataRow("c")]
    [DataRow("d")]
    public async Task Missing_any_permission_fails_closed(string denied)
    {
        var f = new Fixture();
        f.Permissions.ReadAsync(f.User, "host", null, Arg.Any<CancellationToken>())
            .Returns(new PermissionSnapshot(new[] { "a", "b", "c", "d" }.Where(p => p != denied).ToArray(), false));
        Assert.IsNull(await f.Authorization.AuthorizeAllAsync(["a", "b", "c", "d"]));
    }

    [TestMethod]
    public async Task Caller_cannot_remove_required_permission_while_authority_read_waits()
    {
        var f = new Fixture();
        var gate = new TaskCompletionSource<AccessSessionValidationRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Queries.QuerySingleOrDefaultAsync<AccessSessionValidationRecord>(IdentitySql.FindAccessSessionValidationById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        List<string> required = ["a", "missing"];
        var pending = f.Authorization.AuthorizeAllAsync(required);
        required.Remove("missing");
        gate.SetResult(f.Session);
        Assert.IsNull(await pending);
    }

    [TestMethod]
    public async Task Empty_or_malformed_sets_do_not_grant_session_only_authority()
    {
        var f = new Fixture();
        Assert.IsNull(await f.Authorization.AuthorizeAllAsync([]));
        Assert.IsNull(await f.Authorization.AuthorizeAllAsync(["a", " "]));
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    /// <summary>已认证令牌不能越过会话撤销或可信租户上下文的变化。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Authenticated_session_revocation_or_tenant_mismatch_fails_closed(bool tenantMismatch)
    {
        var f = new Fixture();
        Assert.IsNotNull(await f.Authorization.AuthorizeAllAsync(["a", "b"]));
        if (tenantMismatch) f.Tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "other", "其他租户"));
        else f.Session.RevokedAtUtc = DateTimeOffset.UtcNow;
        Assert.IsNull(await f.Authorization.AuthorizeAllAsync(["a", "b"]));
        await f.Permissions.Received(1).ReadAsync(f.User, "host", null, Arg.Any<CancellationToken>());
    }

    /// <summary>容量拒绝和主动取消必须向调用边界传播，不能伪装成权限不足。</summary>
    [TestMethod]
    public async Task Authority_capacity_rejection_and_cancellation_propagate()
    {
        var f = new Fixture();
        f.Queries.QuerySingleOrDefaultAsync<AccessSessionValidationRecord>(IdentitySql.FindAccessSessionValidationById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<AccessSessionValidationRecord?>(_ => throw new ServiceCapacityExceededException(ServiceCapacityFailureKind.Rejected, TimeSpan.FromSeconds(1)));
        await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(() => f.Authorization.AuthorizeAllAsync(["a", "b"]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        f.Queries.QuerySingleOrDefaultAsync<AccessSessionValidationRecord>(IdentitySql.FindAccessSessionValidationById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<AccessSessionValidationRecord?>(_ => throw new OperationCanceledException(cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Authorization.AuthorizeAllAsync(["a", "b"], cancellation.Token));
        Assert.AreEqual(0, f.Permissions.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal Guid User = Guid.CreateVersion7();
        internal IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal IPermissionSnapshotReader Permissions = Substitute.For<IPermissionSnapshotReader>();
        internal AccessSessionValidationRecord Session;
        internal CurrentSessionAuthorization Authorization;
        internal CurrentTenantAccessor Tenant = new();

        internal Fixture()
        {
            var now = DateTimeOffset.UtcNow;
            var sessionId = Guid.CreateVersion7();
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
            Session = new AccessSessionValidationRecord { UserId = User, SecurityStamp = "stamp",
                ScopeKey = "host", IsActive = true, ExpiresAtUtc = now.AddHours(1) };
            Queries.QuerySingleOrDefaultAsync<AccessSessionValidationRecord>(IdentitySql.FindAccessSessionValidationById,
                Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Session);
            Permissions.ReadAsync(User, "host", null, Arg.Any<CancellationToken>()).Returns(new PermissionSnapshot(["a", "b", "c", "d"], false));
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
                new Claim[] { new("sub", User.ToString()), new("sid", sessionId.ToString()),
                    new(FullNetIdentityClaimTypes.SecurityStamp, "stamp"), new(FullNetIdentityClaimTypes.ActorScope, "host"),
                    new(FullNetIdentityClaimTypes.Scope, "host"), new("exp", now.AddMinutes(5).ToUnixTimeSeconds().ToString()) }, "test")) } };
            var tenant = Tenant; tenant.SetHost();
            var oidc = new IdentityOidcAccessSessionValidator(Queries, clock, tenant, Options.Create(new IdentityOidcOptions()), Options.Create(new IdentityOptions()));
            Authorization = new CurrentSessionAuthorization(http, new AccessSessionValidator(Queries, clock, oidc, Options.Create(new IdentityOidcOptions())),
                Permissions, tenant, Substitute.For<IActiveTenantContextResolver>(), clock, Options.Create(new IdentityOidcOptions()));
        }
    }
}
