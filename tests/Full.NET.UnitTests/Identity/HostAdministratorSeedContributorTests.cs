using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Seeding;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostAdministratorSeedContributorTests
{
    private const string Password = "Local-only-password!42";
    private static readonly SeedContext Context = new(
        Guid.Parse("019822d3-0700-7000-8000-000000000202"),
        SeedProfile.Baseline,
        "Development",
        "zh-CN",
        "trace-host-administrator");

    [TestMethod]
    [DataRow("", Password)]
    [DataRow("administrator", "")]
    [DataRow("", "")]
    public async Task Missing_bootstrap_secret_fails_before_domain_service(
        string username,
        string password)
    {
        var bootstrap = Substitute.For<IIdentityBootstrapService>();
        var contributor = CreateContributor(bootstrap, username, password);

        var exception = await Assert.ThrowsExactlyAsync<SeedContributionException>(
            () => contributor.SeedAsync(Context));

        Assert.AreEqual(
            SeedContributionErrorCodes.BootstrapSecretMissing,
            exception.Code);
        if (!string.IsNullOrEmpty(password))
        {
            Assert.DoesNotContain(password, exception.ToString(), StringComparison.Ordinal);
        }
        await bootstrap.DidNotReceive().BootstrapHostAdminAsync(
            Arg.Any<BootstrapHostAdminRequest>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Complete_secrets_create_administrator_through_domain_service()
    {
        using var cancellation = new CancellationTokenSource();
        var bootstrap = Substitute.For<IIdentityBootstrapService>();
        bootstrap.BootstrapHostAdminAsync(
                Arg.Any<BootstrapHostAdminRequest>(),
                cancellation.Token)
            .Returns(Result<BootstrapHostAdminResult>.Success(
                new BootstrapHostAdminResult(Guid.CreateVersion7(), true, true)
                {
                    AuthorizationChanged = true,
                }));
        var contributor = CreateContributor(bootstrap, "administrator", Password);

        var result = await contributor.SeedAsync(Context, cancellation.Token);

        Assert.AreEqual(1, result.CreatedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(0, result.SkippedCount);
        Assert.DoesNotContain(Password, result.ToString(), StringComparison.Ordinal);
        await bootstrap.Received(1).BootstrapHostAdminAsync(
            new BootstrapHostAdminRequest(
                "administrator",
                Password,
                "系统管理员"),
            cancellation.Token);
    }

    [TestMethod]
    public async Task Existing_administrator_authorization_repair_is_reported_as_update()
    {
        var bootstrap = Substitute.For<IIdentityBootstrapService>();
        bootstrap.BootstrapHostAdminAsync(
                Arg.Any<BootstrapHostAdminRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<BootstrapHostAdminResult>.Success(
                new BootstrapHostAdminResult(Guid.CreateVersion7(), false, true)
                {
                    AuthorizationChanged = true,
                }));
        var contributor = CreateContributor(bootstrap, "administrator", Password);

        var result = await contributor.SeedAsync(Context);

        Assert.AreEqual(0, result.CreatedCount);
        Assert.AreEqual(1, result.UpdatedCount);
        Assert.AreEqual(0, result.SkippedCount);
    }

    [TestMethod]
    public async Task Existing_compliant_administrator_is_reported_as_skipped()
    {
        var bootstrap = Substitute.For<IIdentityBootstrapService>();
        bootstrap.BootstrapHostAdminAsync(
                Arg.Any<BootstrapHostAdminRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<BootstrapHostAdminResult>.Success(
                new BootstrapHostAdminResult(Guid.CreateVersion7(), false, true)));
        var contributor = CreateContributor(bootstrap, "administrator", Password);

        var result = await contributor.SeedAsync(Context);

        Assert.AreEqual(0, result.CreatedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(1, result.SkippedCount);
    }

    [TestMethod]
    public async Task Domain_failure_preserves_stable_code_without_secret()
    {
        const string errorCode = "identity.bootstrap.authorization_sync_failed";
        var bootstrap = Substitute.For<IIdentityBootstrapService>();
        bootstrap.BootstrapHostAdminAsync(
                Arg.Any<BootstrapHostAdminRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<BootstrapHostAdminResult>.Failure(new Error(
                errorCode,
                errorCode,
                ErrorType.Conflict)));
        var contributor = CreateContributor(bootstrap, "administrator", Password);

        var exception = await Assert.ThrowsExactlyAsync<SeedContributionException>(
            () => contributor.SeedAsync(Context));

        Assert.AreEqual(errorCode, exception.Code);
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
    }

    [TestMethod]
    public void Contributor_metadata_limits_super_administrator_to_baseline()
    {
        var contributor = CreateContributor(
            Substitute.For<IIdentityBootstrapService>(),
            "administrator",
            Password);

        Assert.AreEqual("identity.host_administrator", contributor.Name);
        Assert.AreEqual(1, contributor.Version);
        CollectionAssert.AreEquivalent(
            new[] { SeedProfile.Baseline },
            contributor.Profiles.ToArray());
        Assert.HasCount(0, contributor.Dependencies);
        Assert.HasCount(1, SeedContributorGraph.Order([contributor], SeedProfile.Baseline));
        Assert.HasCount(1, SeedContributorGraph.Order([contributor], SeedProfile.Development));
    }

    [TestMethod]
    public void Identity_module_registers_seed_contributors_once_when_added_twice()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var module = new IdentityModule();

        module.AddServices(services, configuration);
        module.AddServices(services, configuration);

        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IDataSeedContributor))
            .ToArray();
        Assert.HasCount(1, descriptors);
        Assert.IsTrue(
            descriptors.All(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped));
        CollectionAssert.AreEquivalent(
            new[] { typeof(HostAdministratorSeedContributor) },
            descriptors.Select(descriptor => descriptor.ImplementationType).ToArray());
    }

    private static HostAdministratorSeedContributor CreateContributor(
        IIdentityBootstrapService bootstrap,
        string username,
        string password) =>
        new(
            bootstrap,
            Options.Create(new IdentityOptions
            {
                Bootstrap = new IdentityBootstrapOptions
                {
                    Username = username,
                    Password = password,
                    DisplayName = "系统管理员",
                },
            }));
}
