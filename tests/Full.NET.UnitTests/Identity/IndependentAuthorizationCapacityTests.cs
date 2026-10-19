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

/// <summary>普通与独立授权的会话容量失败必须保留可重试语义。</summary>
[TestClass]
public sealed class IndependentAuthorizationCapacityTests
{
    [TestMethod]
    [DataRow(false, ServiceCapacityFailureKind.Rejected)]
    [DataRow(false, ServiceCapacityFailureKind.Timeout)]
    [DataRow(true, ServiceCapacityFailureKind.Rejected)]
    [DataRow(true, ServiceCapacityFailureKind.Timeout)]
    public async Task Session_authorization_preserves_capacity_failure(bool oidc, ServiceCapacityFailureKind kind)
    {
        var failure = new ServiceCapacityExceededException(kind, TimeSpan.FromSeconds(1));
        var fixture = CreateFixture(oidc, failure);
        var actual = await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(
            () => fixture.Validator.IsValidAsync(fixture.Principal));
        Assert.AreSame(failure, actual);
        Assert.AreEqual(fixture.TenantId, fixture.Tenant.Id);
        Assert.IsFalse(fixture.Tenant.IsHost);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Request_cancellation_is_not_converted_to_invalid_session(bool oidc)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = new OperationCanceledException(cancellation.Token);
        var fixture = CreateFixture(oidc, failure);
        var actual = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.Validator.IsValidAsync(fixture.Principal, cancellation.Token));
        Assert.AreSame(failure, actual);
        Assert.AreEqual(fixture.TenantId, fixture.Tenant.Id);
        Assert.IsFalse(fixture.Tenant.IsHost);
    }

    private static (AccessSessionValidator Validator, ClaimsPrincipal Principal, CurrentTenantAccessor Tenant, Guid TenantId)
        CreateFixture(bool oidc, Exception failure)
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<RefreshSessionRecord>(IdentitySql.FindRefreshSessionById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns<RefreshSessionRecord?>(_ => throw failure);
        query.QuerySingleOrDefaultAsync<IdentityOidcApplicationSessionValidationRecord>(
            IdentityOidcSessionSql.FindApplicationSessionValidationById,
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns<IdentityOidcApplicationSessionValidationRecord?>(_ => throw failure);
        var tenant = new CurrentTenantAccessor();
        var tenantId = Guid.CreateVersion7();
        tenant.SetTenant(new TenantContext(tenantId, "tenant-a", "Tenant A"));
        var oidcOptions = Options.Create(new IdentityOidcOptions { Enable = oidc, Issuer = "https://localhost/identity" });
        var clock = Substitute.For<IClock>();
        var oidcValidator = new IdentityOidcAccessSessionValidator(query, clock, tenant, oidcOptions,
            Options.Create(new IdentityOptions { Audience = "Full.NET.Api" }));
        var validator = new AccessSessionValidator(query, clock, oidcValidator, oidcOptions);
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

        return (validator, principal, tenant, tenantId);
    }
}
