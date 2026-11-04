using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.ManageTenantOwnership;
using Full.NET.Modules.Identity.Oidc;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>生产环境不能回退为仅密码交接；Provider 合格也不能绕过其实际验证失败。</summary>
[TestClass]
public sealed class TenantOwnershipPolicyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Production_requires_eligible_and_successful_strong_reauthentication(bool eligible)
    {
        var queries = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "a", "企业 A"));
        var user = Guid.CreateVersion7(); var sessions = Substitute.For<ICurrentSessionAuthorization>();
        sessions.AuthorizeAsync(IdentityTenantMembershipPermissions.TransferOwnership, Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(user, tenant.Id, Guid.CreateVersion7()));
        var provider = new StrongProvider(eligible); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var environment = Substitute.For<IHostEnvironment>(); environment.EnvironmentName.Returns(Environments.Production);
        var database = Options.Create(new DatabaseOptions()); var oidc = Options.Create(new IdentityOidcOptions());
        var locks = Substitute.For<IDatabaseTransactionBoundSessionLock>();
        var service = new TenantOwnershipService(queries, commands, Substitute.For<ICommandTransaction>(), locks, tenant,
            Substitute.For<IIdentityActiveTenantDirectory>(), sessions, Substitute.For<IPermissionSnapshotReader>(), provider,
            new TenantOwnerAccountGuard(queries, database), new TenantMembershipQueryService(queries, database), environment,
            database, clock, Substitute.For<IIdGenerator>(), new AccessSessionValidator(queries, clock,
                new IdentityOidcAccessSessionValidator(queries, clock, tenant, oidc, Options.Create(new IdentityOptions())), oidc));
        var request = new TransferTenantOwnerRequest(Guid.CreateVersion7(), null, null, Guid.CreateVersion7(), 1, "private-password", "593712");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "test"));
        var result = await service.TransferAsync(request, principal, default);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(eligible ? IdentityErrorCodes.MfaTotpRequired : IdentityErrorCodes.TenantOwnershipStrongAuthenticationRequired, result.Error!.Code);
        Assert.AreEqual(eligible ? 1 : 0, provider.Calls);
        Assert.AreEqual(0, commands.ReceivedCalls().Count()); Assert.AreEqual(0, locks.ReceivedCalls().Count());
    }

    [TestMethod]
    public void Request_diagnostic_text_does_not_disclose_password_or_TOTP()
    {
        var request = new TransferTenantOwnerRequest(Guid.CreateVersion7(), null, null, Guid.CreateVersion7(), 1, "private-password", "593712");
        Assert.AreEqual(nameof(TransferTenantOwnerRequest), request.ToString());
    }

    private sealed class StrongProvider(bool eligible) : IStrongReauthenticationProvider
    {
        internal int Calls;
        public bool IsProductionEligible => eligible;
        public Task<Result<IdentityUser>> VerifyAsync(Guid operatorUserId, string currentPassword, string? totpCode, CancellationToken cancellationToken = default)
        {
            Calls++; return Task.FromResult(Result<IdentityUser>.Failure(new Error(IdentityErrorCodes.MfaTotpRequired, "A second factor is required.", ErrorType.Unauthorized)));
        }
    }
}
