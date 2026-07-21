using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.ManageSuperAdministrators;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using FullNetIdentityOptions = Full.NET.Modules.Identity.Configuration.IdentityOptions;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class SuperAdministratorManagementServiceTests
{
    private static readonly Guid OperatorUserId =
        Guid.Parse("01981f35-2300-7000-8000-000000000001");
    private static readonly Guid TargetUserId =
        Guid.Parse("01981f35-2300-7000-8000-000000000002");
    private const string Password = "FullNet!2026Password";

    [TestMethod]
    public async Task Disabled_remote_management_is_rejected_before_database_access()
    {
        var fixture = new Fixture(enabled: false);

        var result = await fixture.Service.GrantAsync(
            CreatePrincipal(),
            "target-admin",
            Password);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            IdentityErrorCodes.SuperAdministratorRemoteManagementDisabled,
            result.Error?.Code);
        await fixture.QueryExecutor.DidNotReceiveWithAnyArgs()
            .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                default!,
                default,
                default);
    }

    [TestMethod]
    public async Task Invalid_operator_password_is_rejected_without_calling_domain_service()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.GrantAsync(
            CreatePrincipal(),
            "target-admin",
            "wrong-password");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            IdentityErrorCodes.SuperAdministratorReauthenticationFailed,
            result.Error?.Code);
        await fixture.DomainService.DidNotReceiveWithAnyArgs()
            .GrantAsync(default, default, default);
    }

    [TestMethod]
    public async Task Grant_normalizes_target_username_and_calls_protected_domain_service()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.GrantAsync(
            CreatePrincipal(),
            "  Target-Admin  ",
            Password);

        Assert.IsTrue(result.IsSuccess);
        await fixture.QueryExecutor.Received(1)
            .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                Arg.Is<object>(value =>
                    ReadProperty<string>(value!, "NormalizedUsername")
                    == "TARGET-ADMIN"),
                Arg.Any<CancellationToken>());
        await fixture.DomainService.Received(1)
            .GrantAsync(
                OperatorUserId,
                TargetUserId,
                Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Revoke_reauthenticates_operator_and_calls_protected_domain_service()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RevokeAsync(
            CreatePrincipal(),
            TargetUserId,
            Password);

        Assert.IsTrue(result.IsSuccess);
        await fixture.DomainService.Received(1)
            .RevokeAsync(
                OperatorUserId,
                TargetUserId,
                Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Production_without_eligible_provider_is_rejected()
    {
        var fixture = new Fixture(
            enabled: true,
            environmentName: Environments.Production,
            productionEligible: false);

        var result = await fixture.Service.GrantAsync(
            CreatePrincipal(),
            "target-admin",
            Password,
            "123456");

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            IdentityErrorCodes.SuperAdministratorRemoteManagementDisabled,
            result.Error?.Code);
        await fixture.DomainService.DidNotReceiveWithAnyArgs()
            .GrantAsync(default, default, default);
    }

    [TestMethod]
    public async Task Production_eligible_provider_missing_totp_is_rejected()
    {
        var fixture = new Fixture(
            enabled: true,
            environmentName: Environments.Production,
            productionEligible: true,
            verifyResult: Result<IdentityUser>.Failure(new Error(
                IdentityErrorCodes.MfaTotpRequired,
                "totp required",
                ErrorType.Unauthorized)));

        var result = await fixture.Service.GrantAsync(
            CreatePrincipal(),
            "target-admin",
            Password);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.MfaTotpRequired, result.Error?.Code);
        await fixture.DomainService.DidNotReceiveWithAnyArgs()
            .GrantAsync(default, default, default);
    }

    [TestMethod]
    public async Task Production_eligible_provider_with_totp_calls_domain_service()
    {
        var fixture = new Fixture(
            enabled: true,
            environmentName: Environments.Production,
            productionEligible: true);

        var result = await fixture.Service.GrantAsync(
            CreatePrincipal(),
            "target-admin",
            Password,
            "123456");

        Assert.IsTrue(result.IsSuccess);
        await fixture.DomainService.Received(1)
            .GrantAsync(
                OperatorUserId,
                TargetUserId,
                Arg.Any<CancellationToken>());
    }

    private static ClaimsPrincipal CreatePrincipal() => new(
        new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, OperatorUserId.ToString("D"))],
            "unit-test"));

    private static T ReadProperty<T>(object value, string propertyName) =>
        (T)value.GetType().GetProperty(propertyName)!.GetValue(value)!;

    private sealed class Fixture
    {
        public Fixture(
            bool enabled = true,
            string environmentName = "Testing",
            bool productionEligible = false,
            Result<IdentityUser>? verifyResult = null)
        {
            var passwordHasher = new PasswordHasher<IdentityUser>();
            var operatorUser = CreateUser(OperatorUserId, "operator");
            var operatorRecord = CreateRecord(
                operatorUser,
                passwordHasher.HashPassword(operatorUser, Password));
            var targetUser = CreateUser(TargetUserId, "target-admin");
            var targetRecord = CreateRecord(targetUser, "unused");

            QueryExecutor = Substitute.For<IQueryExecutor>();
            QueryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindHostUserById,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(operatorRecord);
            QueryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindUserByScopeAndUsername,
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(targetRecord);
            DomainService = Substitute.For<ISuperAdministratorService>();
            DomainService.GrantAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result<SuperAdministratorChangeResponse>.Success(
                    new SuperAdministratorChangeResponse(TargetUserId, true)));
            DomainService.RevokeAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(Result<SuperAdministratorChangeResponse>.Success(
                    new SuperAdministratorChangeResponse(TargetUserId, true)));

            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns(environmentName);

            IStrongReauthenticationProvider provider;
            if (productionEligible || verifyResult is not null || environmentName == Environments.Production)
            {
                provider = new StubStrongReauthenticationProvider(
                    productionEligible,
                    verifyResult ?? Result<IdentityUser>.Success(operatorUser));
            }
            else
            {
                provider = new PasswordReauthenticationProvider(
                    QueryExecutor,
                    passwordHasher);
            }

            Service = new SuperAdministratorManagementService(
                QueryExecutor,
                DomainService,
                provider,
                Options.Create(new FullNetIdentityOptions
                {
                    EnableRemoteSuperAdministratorManagement = enabled,
                }),
                environment);
        }

        public IQueryExecutor QueryExecutor { get; }

        public ISuperAdministratorService DomainService { get; }

        public SuperAdministratorManagementService Service { get; }
    }

    private static IdentityUser CreateUser(Guid id, string username) => new(
        id,
        null,
        "host",
        username,
        username.ToUpperInvariant(),
        username,
        string.Empty,
        true,
        0,
        null,
        "stamp",
        DateTimeOffset.UtcNow.AddDays(-1),
        null,
        1);

    private static IdentityUserRecord CreateRecord(
        IdentityUser user,
        string passwordHash) => new(
        user.Id,
        user.TenantId,
        user.ScopeKey,
        user.Username,
        user.NormalizedUsername,
        user.DisplayName,
        passwordHash,
        user.IsActive,
        user.FailedLoginCount,
        user.LockoutEndUtc,
        user.SecurityStamp,
        user.CreatedAtUtc,
        user.UpdatedAtUtc,
        user.Version,
        user.PreferredLocale,
        user.ProfileVersion);
}
