using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Endpoint = Full.NET.Modules.Identity.Features.OidcSession.Endpoint;

namespace Full.NET.UnitTests.Identity;

/// <summary>多标签中心 Cookie 不能改变访问令牌指定的退出用户。</summary>
[TestClass]
public sealed class IdentityOidcLogoutTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Center_logout_revokes_token_owner_and_only_clears_matching_cookie(bool sameUser)
    {
        var owner = Guid.NewGuid();
        var cookieUser = sameUser ? owner : Guid.NewGuid();
        var authentication = Substitute.For<IAuthenticationService>();
        using var services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(IdentityOidcCenterAuthenticationDefaults.UserIdClaim, cookieUser.ToString()),
            new Claim(IdentityOidcCenterAuthenticationDefaults.CenterSessionIdClaim, Guid.NewGuid().ToString())
        }, "center"));
        authentication.AuthenticateAsync(context, IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
            .Returns(AuthenticateResult.Success(new AuthenticationTicket(principal, "center")));
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        query.QueryAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Guid>());
        var service = new IdentityOidcAuthorizationService(null!, null!, new PassthroughCommandTransaction(),
            new IdentityOidcSessionService(query, command, clock),
            new IdentityOidcGrantRevocationService(query, command, clock),
            null!, null!, null!, null!, query, clock,
            Options.Create(new IdentityOptions()), Options.Create(new IdentityOidcOptions()),
            new CurrentTenantAccessor());

        await service.SignOutCenterAsync(context, owner);

        await command.Received(1).ExecuteAsync(IdentityOidcSessionSql.RevokeAllCenterSessionsByUser,
            Arg.Is<object?>(value => value is Dictionary<string, object?> &&
                Equals(((Dictionary<string, object?>)value)["UserId"], owner)), Arg.Any<CancellationToken>());
        await authentication.Received(sameUser ? 1 : 0).SignOutAsync(context,
            IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme, Arg.Any<AuthenticationProperties?>());
    }

    [TestMethod]
    public async Task Center_logout_executes_host_only_queries_in_host_scope_and_restores_tenant_context()
    {
        var tenant = new CurrentTenantAccessor();
        ((ICurrentTenantContextWriter)tenant).SetTenant(
            new TenantContext(Guid.NewGuid(), "tenant", "Tenant"));
        var authentication = Substitute.For<IAuthenticationService>();
        using var services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        authentication.AuthenticateAsync(context, IdentityOidcCenterAuthenticationDefaults.AuthenticationScheme)
            .Returns(AuthenticateResult.NoResult());
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        query.QueryAsync<Guid>(
                IdentityOidcSessionSql.ListActiveHostOidcApplicationSessionIdsByUser,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsTrue(tenant.IsHost, "Host-only SQL must run in the trusted Host context.");
                return Array.Empty<Guid>();
            });
        var service = new IdentityOidcAuthorizationService(null!, null!, new PassthroughCommandTransaction(),
            new IdentityOidcSessionService(query, command, clock),
            new IdentityOidcGrantRevocationService(query, command, clock),
            null!, null!, null!, null!, query, clock,
            Options.Create(new IdentityOptions()), Options.Create(new IdentityOidcOptions()),
            tenant);

        await service.SignOutCenterAsync(context, Guid.NewGuid());

        Assert.IsFalse(tenant.IsHost);
        Assert.AreEqual("tenant", tenant.Identifier);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Invalid_or_non_oidc_bearer_cannot_fall_back_to_cookie(bool authenticated)
    {
        var authentication = Substitute.For<IAuthenticationService>();
        using var services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Authorization = "Bearer invalid";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", Guid.NewGuid().ToString())
        }, "Bearer"));
        authentication.AuthenticateAsync(context, "Bearer").Returns(authenticated
            ? AuthenticateResult.Success(new AuthenticationTicket(principal, "Bearer"))
            : AuthenticateResult.Fail("invalid"));

        Assert.AreEqual(Guid.Empty, await Endpoint.TryReadBearerUserIdAsync(context));
    }

    private sealed class PassthroughCommandTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }
}
