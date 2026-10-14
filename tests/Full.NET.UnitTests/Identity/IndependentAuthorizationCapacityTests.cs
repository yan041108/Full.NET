using System.Security.Claims;
using Full.NET.Abstractions.Results;
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

/// <summary>独立授权容量拒绝与普通认证失败的边界回归。</summary>
[TestClass]
public sealed class IndependentAuthorizationCapacityTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task Only_independent_authorization_preserves_capacity_failure(bool oidc, bool independent)
    {
        var query = Substitute.For<IQueryExecutor>();
        var failure = new ServiceCapacityExceededException(ServiceCapacityFailureKind.Timeout, TimeSpan.FromSeconds(1));
        query.QuerySingleOrDefaultAsync<RefreshSessionRecord>(IdentitySql.FindRefreshSessionById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns<RefreshSessionRecord?>(_ => throw failure);
        query.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionValidationRecord>(
            IdentityOidcSessionSql.FindApplicationSessionValidationById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<IdentityOidcApplicationSessionValidationRecord?>(_ => throw failure);
        var scope = Substitute.For<IDatabaseAuthorizationReadScope>();
        scope.IsActive.Returns(independent);
        var tenant = new CurrentTenantAccessor();
        var tenantId = Guid.CreateVersion7();
        tenant.SetTenant(new TenantContext(tenantId, "tenant-a", "Tenant A"));
        var oidcOptions = Options.Create(new IdentityOidcOptions { Enable = oidc, Issuer = "https://localhost/identity" });
        var clock = Substitute.For<IClock>();
        var oidcValidator = new IdentityOidcAccessSessionValidator(query, clock, tenant, oidcOptions,
            Options.Create(new IdentityOptions { Audience = "Full.NET.Api" }), scope);
        var validator = new AccessSessionValidator(query, clock, oidcValidator, oidcOptions, scope);
        var claims = new List<Claim>
        {
            new("sub", Guid.CreateVersion7().ToString("D")),
            new(IdentityClaimTypes.ActorScope, "host"),
            new(IdentityClaimTypes.Scope, "host"),
        };
        if (oidc)
        {
            claims.Add(new("iss", "https://localhost/identity"));
            claims.Add(new("aud", "Full.NET.Api"));
            claims.Add(new(FullNetIdentityClaimTypes.ApplicationSessionId, Guid.CreateVersion7().ToString("D")));
            claims.Add(new(FullNetIdentityClaimTypes.TokenUse, IdentityOidcPrincipalFactory.TokenUseAccess));
        }
        else
        {
            claims.Add(new(IdentityClaimTypes.SessionId, Guid.CreateVersion7().ToString("D")));
            claims.Add(new(IdentityClaimTypes.SecurityStamp, "stamp"));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "unit-test"));

        if (independent)
        {
            var actual = await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(
                () => validator.IsValidAsync(principal));
            Assert.AreSame(failure, actual);
        }
        else
        {
            Assert.IsFalse(await validator.IsValidAsync(principal));
        }
        Assert.AreEqual(tenantId, tenant.Id);
        Assert.IsFalse(tenant.IsHost);
    }
}
