using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.Tenancy.Seeding;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

[TestClass]
public sealed class LocalTenantSeedContributorTests
{
    private static readonly SeedContext Context = new(
        Guid.Parse("019822d3-0700-7000-8000-000000000201"),
        SeedProfile.Development,
        "Development",
        "zh-CN",
        "trace-local-tenant");

    [TestMethod]
    public async Task Missing_tenant_is_provisioned_through_the_domain_service()
    {
        using var cancellation = new CancellationTokenSource();
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<LocalTenantSeedSummary>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                cancellation.Token)
            .Returns(Task.FromResult<LocalTenantSeedSummary?>(null));
        var provisioning = Substitute.For<ITenantProvisioningService>();
        provisioning.ProvisionAsync(
                Arg.Any<ProvisionTenantRequest>(),
                cancellation.Token)
            .Returns(Result<TenantSummary>.Success(Tenant()));
        var contributor = new LocalTenantSeedContributor(queryExecutor, provisioning);

        var result = await contributor.SeedAsync(Context, cancellation.Token);

        Assert.AreEqual(1, result.CreatedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(0, result.SkippedCount);
        await queryExecutor.Received(1).QuerySingleOrDefaultAsync<LocalTenantSeedSummary>(
            Arg.Is<SqlStatement>(statement =>
                statement != null &&
                statement.Name == "tenancy.tenant.find_summary_by_identifier" &&
                statement.Scope == SqlDataScope.Global),
            Arg.Is<object>(parameters =>
                parameters != null && ReadIdentifier(parameters) == "local"),
            cancellation.Token);
        await provisioning.Received(1).ProvisionAsync(
            new ProvisionTenantRequest("local", "Full.NET Local", "localhost"),
            cancellation.Token);
    }

    [TestMethod]
    public async Task Matching_tenant_is_skipped_without_provisioning()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<LocalTenantSeedSummary>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(new LocalTenantSeedSummary(
                Guid.CreateVersion7(),
                "local",
                "Full.NET Local",
                "localhost",
                true,
                1));
        var provisioning = Substitute.For<ITenantProvisioningService>();
        var contributor = new LocalTenantSeedContributor(queryExecutor, provisioning);

        var result = await contributor.SeedAsync(Context);

        Assert.AreEqual(0, result.CreatedCount);
        Assert.AreEqual(0, result.UpdatedCount);
        Assert.AreEqual(1, result.SkippedCount);
        await provisioning.DidNotReceive().ProvisionAsync(
            Arg.Any<ProvisionTenantRequest>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("Changed Name", "localhost")]
    [DataRow("Full.NET Local", "conflict.localhost")]
    public async Task Existing_natural_key_with_different_data_fails_without_overwrite(
        string name,
        string domain)
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<LocalTenantSeedSummary>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(new LocalTenantSeedSummary(
                Guid.CreateVersion7(),
                "local",
                name,
                domain,
                true,
                1));
        var provisioning = Substitute.For<ITenantProvisioningService>();
        var contributor = new LocalTenantSeedContributor(queryExecutor, provisioning);

        var exception = await Assert.ThrowsExactlyAsync<SeedContributionException>(
            () => contributor.SeedAsync(Context));

        Assert.AreEqual(SeedContributionErrorCodes.DataConflict, exception.Code);
        await provisioning.DidNotReceive().ProvisionAsync(
            Arg.Any<ProvisionTenantRequest>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Contributor_metadata_limits_execution_to_development()
    {
        var contributor = new LocalTenantSeedContributor(
            Substitute.For<IQueryExecutor>(),
            Substitute.For<ITenantProvisioningService>());

        Assert.AreEqual("tenancy.local_tenant", contributor.Name);
        Assert.AreEqual(1, contributor.Version);
        CollectionAssert.AreEquivalent(
            new[] { SeedProfile.Development },
            contributor.Profiles.ToArray());
        Assert.HasCount(0, contributor.Dependencies);
        Assert.HasCount(1, SeedContributorGraph.Order([contributor], SeedProfile.Development));
        Assert.HasCount(0, SeedContributorGraph.Order([contributor], SeedProfile.Demo));
    }

    [TestMethod]
    public void Tenancy_module_registers_one_scoped_contributor_when_added_twice()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var module = new TenancyModule();

        module.AddServices(services, configuration);
        module.AddServices(services, configuration);

        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IDataSeedContributor))
            .ToArray();
        Assert.HasCount(1, descriptors);
        Assert.AreEqual(ServiceLifetime.Scoped, descriptors[0].Lifetime);
        Assert.AreEqual(
            typeof(LocalTenantSeedContributor),
            descriptors[0].ImplementationType);
    }

    private static TenantSummary Tenant() => new(
        Guid.CreateVersion7(),
        "local",
        "Full.NET Local",
        "localhost",
        true,
        1);

    private static string? ReadIdentifier(object parameters) =>
        ReadSqlParameter<string?>(parameters, "Identifier");
}
