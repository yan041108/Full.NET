using System.Security.Claims;
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

namespace Full.NET.UnitTests.Ai;

/// <summary>工具授权使用权威会话和权限，不能靠令牌中的旧权限继续执行。</summary>
[TestClass]
public sealed class AiToolCurrentSessionTests
{
    [TestMethod]
    [DataRow("valid", true)]
    [DataRow("expired", false)]
    [DataRow("revoked", false)]
    [DataRow("disabled", false)]
    [DataRow("permission", false)]
    [DataRow("tenant", false)]
    [DataRow("actor", false)]
    [DataRow("api_key", false)]
    public async Task Current_session_is_revalidated_for_every_tool_async(string scenario, bool allowed)
    {
        var now = DateTimeOffset.UtcNow;
        var user = Guid.NewGuid();
        var session = Guid.NewGuid();
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var record = new RefreshSessionRecord { UserId = scenario == "actor" ? Guid.NewGuid() : user, SessionId = session,
            SecurityStamp = "stamp", ScopeKey = "host", IsActive = scenario != "disabled",
            ExpiresAtUtc = now.AddHours(1), RevokedAtUtc = scenario == "revoked" ? now : null };
        var queries = Substitute.For<IQueryExecutor>();
        queries.QuerySingleOrDefaultAsync<RefreshSessionRecord>(IdentitySql.FindRefreshSessionById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(record);
        var claims = new List<Claim> { new("sub", user.ToString()), new("sid", session.ToString()),
            new(FullNetIdentityClaimTypes.SecurityStamp, "stamp"), new(FullNetIdentityClaimTypes.ActorScope, "host"),
            new(FullNetIdentityClaimTypes.Scope, "host"), new(FullNetIdentityClaimTypes.Permission, "permission"),
            new("exp", (scenario == "expired" ? now.AddMinutes(-1) : now.AddMinutes(5)).ToUnixTimeSeconds().ToString()) };
        if (scenario == "api_key") claims.Add(new(FullNetIdentityClaimTypes.ApiKeyId, Guid.NewGuid().ToString()));
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "trusted-test")) } };
        var tenant = new CurrentTenantAccessor();
        if (scenario == "tenant") tenant.SetTenant(new TenantContext(Guid.NewGuid(), "other", "其他租户")); else tenant.SetHost();
        var permissions = Substitute.For<IPermissionSnapshotReader>();
        permissions.ReadAsync(user, "host", null, Arg.Any<CancellationToken>()).Returns(new PermissionSnapshot(scenario == "permission" ? [] : ["permission"], false));
        var oidcValidator = new IdentityOidcAccessSessionValidator(
            queries,
            clock,
            tenant,
            Options.Create(new IdentityOidcOptions()),
            Options.Create(new IdentityOptions()));
        var authorization = new CurrentSessionAuthorization(
            http,
            new AccessSessionValidator(
                queries,
                clock,
                oidcValidator,
                Options.Create(new IdentityOidcOptions())),
            permissions,
            tenant,
            Substitute.For<IActiveTenantContextResolver>(),
            clock,
            Options.Create(new IdentityOidcOptions()));
        try
        {
            Assert.AreEqual(allowed, await authorization.AuthorizeAsync("permission") is not null);
            if (allowed)
            {
                permissions.ReadAsync(user, "host", null, Arg.Any<CancellationToken>()).Returns(new PermissionSnapshot([], false));
                Assert.IsNull(await authorization.AuthorizeAsync("permission"), "不能复用第一次调用的权限快照。");
            }
        }
        finally { http.HttpContext = null; }
    }
}
